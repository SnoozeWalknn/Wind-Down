namespace WindDown.Core;

// Linux power control. The graceful path asks the desktop session to end, which lets
// apps with unsaved work object; the fallback asks logind directly without --ignore-inhibitors,
// so block inhibitors still stop it. Nothing here forces a session closed or needs root.
public static class NativePower
{
    public static Guid BootId()
    {
        var text = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        return Guid.TryParse(text, out var id) ? id : throw new InvalidOperationException("The Linux boot identity could not be read.");
    }
    private static string Login1(string method)
    {
        var result = Shell.Run("busctl", "call", "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", method);
        if (!result.Ok) throw new InvalidOperationException("systemd-logind could not be reached: " + result.Detail);
        // busctl prints: s "yes"
        var text = result.Output.Trim();
        int start = text.IndexOf('"'), end = text.LastIndexOf('"');
        return start >= 0 && end > start ? text[(start + 1)..end] : text;
    }
    public static void CheckPrivilege(PowerAction action)
    {
        string verb = action == PowerAction.Shutdown ? "power off" : "sleep";
        switch (Login1(action == PowerAction.Shutdown ? "CanPowerOff" : "CanSuspend"))
        {
            case "yes": return;
            case "challenge": throw new InvalidOperationException($"Your account needs an administrator password to {verb} this PC, so Wind Down can't do it unattended. Sign in to a local graphical session, or allow it with a polkit rule.");
            case "na": throw new InvalidOperationException(action == PowerAction.Sleep ? "Sleep is not available with this PC's current power configuration." : "This system does not support powering off from a user session.");
            default: throw new InvalidOperationException($"Your account is not allowed to {verb} this PC.");
        }
    }
    public static bool IsSleepAllowed() { try { return Login1("CanSuspend") == "yes"; } catch { return false; } }
    private static bool HasSessionService(string name) => Shell.Run("busctl", "--user", "status", name).Ok;
    private static void UserCall(string name, string path, string iface, string method, params string[] extra)
    {
        var result = Shell.Run("busctl", ["--user", "call", name, path, iface, method, .. extra], TimeSpan.FromSeconds(30));
        if (!result.Ok) throw new InvalidOperationException(result.Detail);
    }
    /// <summary>Returns a short description of the path that accepted the request.</summary>
    public static string Shutdown()
    {
        CheckPrivilege(PowerAction.Shutdown);
        if (HasSessionService("org.kde.Shutdown"))
        {
            UserCall("org.kde.Shutdown", "/Shutdown", "org.kde.Shutdown", "logoutAndShutdown");
            return "KDE Plasma";
        }
        if (HasSessionService("org.gnome.SessionManager"))
        {
            UserCall("org.gnome.SessionManager", "/org/gnome/SessionManager", "org.gnome.SessionManager", "Shutdown");
            return "GNOME";
        }
        if (HasSessionService("org.xfce.SessionManager"))
        {
            UserCall("org.xfce.SessionManager", "/org/xfce/SessionManager", "org.xfce.Session.Manager", "Shutdown", "b", "true");
            return "Xfce";
        }
        var result = Shell.Run("systemctl", "poweroff", "--no-ask-password");
        if (!result.Ok) throw new InvalidOperationException(result.Detail);
        return "systemd";
    }
    public static void Sleep()
    {
        CheckPrivilege(PowerAction.Sleep);
        var result = Shell.Run("systemctl", "suspend", "--no-ask-password");
        if (!result.Ok) throw new InvalidOperationException(result.Detail);
    }
}
