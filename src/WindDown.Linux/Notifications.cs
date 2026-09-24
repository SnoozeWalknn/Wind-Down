using System.Diagnostics;
using WindDown.Core;

namespace WindDown.App;
// Desktop notifications through libnotify's notify-send (org.freedesktop.Notifications).
// Each notification is bound to a schedule id; its Cancel button can only cancel that schedule.
public sealed class Notifications : IDisposable
{
    private sealed record Entry(Process Process, uint NotificationId);
    private readonly Dictionary<Guid, Entry> open = [];
    public bool Available { get; } = Shell.Exists("notify-send");

    /// <summary>Shows the notification and resolves to the chosen action ("cancel", "default"), or null when dismissed.</summary>
    public Task<string?>? Show(Schedule schedule, bool warning)
    {
        if (!Available) return null;
        string action = schedule.Action == PowerAction.Shutdown ? "Shutdown" : "Sleep";
        string title = warning ? $"{action} is coming up" : $"{action} scheduled";
        string body = warning ? $"{TimePolicy.TargetLabel(schedule.Target)}. Save your work, or cancel below." : $"{TimePolicy.TargetLabel(schedule.Target)}. You can close Wind Down; your schedule stays active.";
        var info = new ProcessStartInfo(Shell.Resolve("notify-send")) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[]
        {
            "--app-name=Wind Down", "--icon=wind-down", "--hint=string:desktop-entry:wind-down",
            "--urgency=" + (warning ? "critical" : "normal"), "--print-id", "--wait",
            $"--action=cancel=Cancel {action.ToLowerInvariant()}", "--action=default=Open Wind Down", title, body,
        }) info.ArgumentList.Add(argument);
        Remove(schedule.Id);
        var process = Process.Start(info);
        if (process == null) return null;
        var first = process.StandardOutput.ReadLine();
        if (!uint.TryParse(first, out var notificationId))
        {
            // No id means no notification server accepted it.
            try { if (!process.WaitForExit(2000)) process.Kill(); } catch { }
            Storage.Log(new InvalidOperationException("notify-send failed: " + process.StandardError.ReadToEnd().Trim()));
            return null;
        }
        lock (open) open[schedule.Id] = new Entry(process, notificationId);
        return Task.Run(() =>
        {
            string? chosen = null;
            string? line;
            while ((line = process.StandardOutput.ReadLine()) != null) if (line.Length > 0) chosen = line.Trim();
            process.WaitForExit();
            lock (open) if (open.TryGetValue(schedule.Id, out var entry) && entry.Process == process) open.Remove(schedule.Id);
            return chosen;
        });
    }
    public void ShowPlain(string title, string body)
    {
        if (!Available) return;
        try { Shell.Run("notify-send", "--app-name=Wind Down", "--icon=wind-down", "--hint=string:desktop-entry:wind-down", title, body); }
        catch (Exception ex) { Storage.Log(ex); }
    }
    public void Remove(Guid id)
    {
        Entry? entry;
        lock (open) { if (!open.Remove(id, out entry)) return; }
        Close(entry);
    }
    private static void Close(Entry entry)
    {
        try { Shell.Run("busctl", "--user", "call", "org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.Notifications", "CloseNotification", "u", entry.NotificationId.ToString()); }
        catch (Exception ex) { Storage.Log(ex); }
        try { if (!entry.Process.HasExited) entry.Process.Kill(); } catch { }
    }
    public void Dispose()
    {
        List<Entry> entries;
        lock (open) { entries = [.. open.Values]; open.Clear(); }
        foreach (var entry in entries) Close(entry);
    }
}
