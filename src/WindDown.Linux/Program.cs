using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia;
using WindDown.Core;

namespace WindDown.App;
internal static class Program
{
    private static FileStream? instance;
    public static string[] Arguments = [];
    public static string RuntimeDir
    {
        get
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return PrivateStorage.Ensure(string.IsNullOrEmpty(runtime) ? Storage.Root : Path.Combine(runtime, "wind-down"));
        }
    }
    private static string SocketPath => Path.Combine(RuntimeDir, "ui.sock");
    /// <summary>The command systemd runs to reach this same executable in worker or reminder mode.</summary>
    public static IReadOnlyList<string> Launcher
    {
        get
        {
            var process = Environment.ProcessPath!;
            return Path.GetFileNameWithoutExtension(process) == "dotnet" ? [process, typeof(Program).Assembly.Location] : [process];
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        // Keep diagnostics out of the system journal.
        Trace.Listeners.Clear();
        try
        {
            PrivateStorage.Ensure(Storage.Root);
            switch (args.FirstOrDefault())
            {
                case "--execute" when args.Length >= 2 && Guid.TryParse(args[1], out var id):
                    new Scheduler(args.Contains("--test")).Execute(id);
                    return 0;
                case "--warning" when args.Length >= 2 && Guid.TryParse(args[1], out var warningId):
                    return Warn(warningId);
                case "--status":
                    return Status();
                case "--cancel":
                    return Cancel(args.Contains("--force"));
                case "--help" or "-h":
                    Console.WriteLine("Usage: wind-down [status | cancel [--force]]");
                    return 0;
            }
        }
        catch (Exception ex) { Storage.Log(ex); Console.Error.WriteLine(ex.Message); return 1; }
        return RunInterface(args);
    }
    private static int RunInterface(string[] args)
    {
        try { instance = new FileStream(Path.Combine(RuntimeDir, "ui.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException)
        {
            // Another window is already running for this user; ask it to come forward.
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
                socket.Send(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args) + "\n"));
            }
            catch (Exception ex) { Storage.Log(ex); }
            return 0;
        }
        int code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        GC.KeepAlive(instance);
        return code;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
    public static async Task Listen(Action<string[]> receive)
    {
        File.Delete(SocketPath);
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(SocketPath));
        server.Listen(4);
        while (true)
        {
            try
            {
                using var client = await server.AcceptAsync();
                using var reader = new StreamReader(new NetworkStream(client, true));
                var line = await reader.ReadLineAsync();
                if (line != null && line.Length < 2048) receive(JsonSerializer.Deserialize<string[]>(line) ?? []);
            }
            catch (Exception ex) { Storage.Log(ex); await Task.Delay(500); }
        }
    }
    // Launched by the reminder timer. Runs without a window: shows an actionable notification,
    // honours its Cancel button, and exits once the notification is resolved or the deadline passes.
    private static int Warn(Guid id)
    {
        var scheduler = new Scheduler();
        var schedule = scheduler.Reconcile();
        if (schedule?.Id != id || schedule.Target <= DateTimeOffset.UtcNow) return 0;
        using var notifications = new Notifications();
        var pending = notifications.Show(schedule, true);
        if (pending == null) return 0;
        var remaining = schedule.Target - DateTimeOffset.UtcNow;
        if (!pending.Wait(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero)) return 0;
        if (pending.Result == "cancel")
        {
            try { scheduler.Cancel(id); notifications.ShowPlain("Schedule cancelled", "Wind Down won't request a power action."); }
            catch (Exception ex) { notifications.ShowPlain("Could not cancel the schedule", ex.Message); }
        }
        else if (pending.Result == "default") OpenInterface();
        return 0;
    }
    public static void OpenInterface()
    {
        // Leave the reminder's systemd unit so the window is not stopped when the reminder exits.
        try { Process.Start(new ProcessStartInfo(Shell.Resolve("systemd-run"), ["--user", "--collect", "--quiet", "--setenv=DOTNET_EnableDiagnostics=0", "--", .. Launcher]) { UseShellExecute = false })?.WaitForExit(10000); }
        catch (Exception ex) { Storage.Log(ex); }
    }
    private static int Status()
    {
        var scheduler = new Scheduler();
        var schedule = scheduler.Reconcile();
        if (schedule == null)
        {
            Console.WriteLine("No power schedule is active.");
            if (scheduler.GetReceipt() is { } receipt) Console.WriteLine($"Last result: {receipt.Status} — {receipt.Message}");
            return 0;
        }
        Console.WriteLine($"{(schedule.Action == PowerAction.Shutdown ? "Shutdown" : "Sleep")} scheduled {TimePolicy.TargetLabel(schedule.Target)} ({TimePolicy.Countdown(schedule.Target, DateTimeOffset.UtcNow)} remaining).");
        return 3;
    }
    private static int Cancel(bool force)
    {
        var scheduler = new Scheduler();
        if (force) { scheduler.Reset(); Console.WriteLine("Wind Down's schedule record and timers were removed."); return 0; }
        var schedule = scheduler.Reconcile();
        if (schedule == null) { Console.WriteLine("No power schedule is active."); return 0; }
        scheduler.Cancel(schedule.Id);
        Console.WriteLine("Schedule cancelled. Wind Down won't request a power action.");
        return 0;
    }
}
