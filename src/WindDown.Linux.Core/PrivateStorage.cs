namespace WindDown.Core;

// Wind Down's folders hold the schedule, the error log and the single-instance socket.
// Keep them readable and writable by this account only (0700).
public static class PrivateStorage
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    public static string Ensure(string path)
    {
        if (OperatingSystem.IsWindows()) { Directory.CreateDirectory(path); return path; }
        Directory.CreateDirectory(path, OwnerOnly);
        if (File.GetUnixFileMode(path) != OwnerOnly) File.SetUnixFileMode(path, OwnerOnly);
        return path;
    }
}
