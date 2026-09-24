using System.Diagnostics;

namespace WindDown.Core;

public sealed record ShellResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    public string Detail => string.IsNullOrWhiteSpace(Error) ? Output.Trim() : Error.Trim();
}

public static class Shell
{
    public static ShellResult Run(string file, params string[] arguments) => Run(file, arguments, TimeSpan.FromSeconds(20));
    // System tools are only taken from root-owned system folders, never from PATH, so a
    // look-alike program dropped into ~/.local/bin or similar can't stand in for them.
    private static readonly string[] SystemDirs = ["/usr/bin", "/bin"];
    public static string Resolve(string command)
    {
        foreach (var dir in SystemDirs)
        {
            var path = Path.Combine(dir, command);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"{command} was not found in /usr/bin.");
    }
    public static ShellResult Run(string file, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(Resolve(file)) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["LC_ALL"] = "C";
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"{file} did not respond.");
        }
        return new ShellResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
    public static bool Exists(string command) => SystemDirs.Any(dir => File.Exists(Path.Combine(dir, command)));
}
