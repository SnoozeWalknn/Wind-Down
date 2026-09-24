using System.Globalization;

namespace WindDown.Core;

public interface ITimerBackend
{
    void Arm(string unit, DateTimeOffset at, IReadOnlyList<string> command, string description);
    bool IsArmed(string unit);
    void Disarm(string unit);
}

// Transient systemd --user timers. They live in the user's service manager, so they outlive
// the app window, stop at sign-out, and vanish on reboot. Persistent=false and WakeSystem=false
// mean a missed deadline is never caught up and a sleeping PC is never woken.
public sealed class SystemdTimers : ITimerBackend
{
    public void Arm(string unit, DateTimeOffset at, IReadOnlyList<string> command, string description)
    {
        var arguments = new List<string>
        {
            "--user", "--no-ask-password", "--collect", $"--unit={unit}", $"--description={description}",
            "--on-calendar=" + at.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC",
            "--timer-property=AccuracySec=1s", "--timer-property=Persistent=false",
            "--timer-property=WakeSystem=false", "--timer-property=RemainAfterElapse=false",
            "--property=RuntimeMaxSec=15min", "--setenv=DOTNET_EnableDiagnostics=0", "--",
        };
        arguments.AddRange(command);
        var result = Shell.Run("systemd-run", arguments, TimeSpan.FromSeconds(20));
        if (!result.Ok) throw new InvalidOperationException("systemd could not create the timer: " + result.Detail);
    }
    public bool IsArmed(string unit) => Shell.Run("systemctl", "--user", "is-active", "--quiet", unit + ".timer").Ok;
    public void Disarm(string unit)
    {
        Shell.Run("systemctl", "--user", "stop", unit + ".timer");
        Shell.Run("systemctl", "--user", "reset-failed", unit + ".timer", unit + ".service");
        if (IsArmed(unit)) throw new IOException("systemd did not remove the scheduled timer.");
    }
    public static bool Available()
    {
        try { var r = Shell.Run("systemctl", "--user", "is-system-running"); return r.Output.Trim() is "running" or "degraded" or "starting"; }
        catch { return false; }
    }
}
