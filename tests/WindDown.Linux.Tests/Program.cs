using System.Diagnostics;
using WindDown.Core;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
void Refuse(Action action, string name) { try { action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var now = new DateTimeOffset(2026, 9, 4, 22, 0, 0, TimeSpan.Zero);
string mode = args.FirstOrDefault() ?? "";

Check(TimePolicy.Duration(now, 23, 59) - now == TimeSpan.FromMinutes(1439), "23 hours 59 minutes accepted");
Check(TimePolicy.Duration(now, 24, 0) - now == TimeSpan.FromHours(24), "24 hours accepted as exact maximum");
Check(TimePolicy.Duration(now, 0, 1) - now == TimeSpan.FromMinutes(1), "one-minute minimum accepted");
Reject(() => TimePolicy.Duration(now, 24, 1), "24 hours 1 minute rejected");
Reject(() => TimePolicy.Duration(now, 0, 0), "zero duration rejected");
Reject(() => TimePolicy.Duration(now, 1, 60), "minute overflow rejected");
Reject(() => TimePolicy.Duration(now, -1, 5), "negative duration rejected");
Reject(() => TimePolicy.Duration(now, 25, 0), "hour overflow rejected");
Check(TimePolicy.Clock(now, 21, 0, TimeZoneInfo.Utc) == now.AddHours(23), "past clock time means tomorrow");
Check(TimePolicy.Clock(now, 22, 0, TimeZoneInfo.Utc) == now.AddDays(1), "exact current minute means tomorrow");
Check(TimePolicy.Clock(now, 0, 0, TimeZoneInfo.Utc) == now.AddHours(2), "midnight conversion");
Check(TimePolicy.Countdown(now.AddHours(26).AddSeconds(1), now) == "26:00:01", "countdown does not wrap at 24 hours");
Check(TimePolicy.Countdown(now.AddSeconds(-10), now) == "00:00:00", "overdue countdown clamps to zero");
Check(TimePolicy.Countdown(now.AddMilliseconds(1500), now) == "00:00:02", "countdown rounds up partial seconds");
var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
Reject(() => TimePolicy.Clock(new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), 2, 30, eastern), "DST spring gap rejected");
Check(TimePolicy.Clock(new DateTimeOffset(2026, 11, 1, 5, 45, 0, TimeSpan.Zero), 1, 30, eastern) == new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), "DST fold selects second future occurrence");
Check(TimePolicy.Clock(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), 1, 30, eastern) == new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), "DST fold selects first future occurrence");
Check(NativePower.BootId() != Guid.Empty, "Linux boot session identity available");

// Scheduling state machine against an in-memory timer backend: runs anywhere, never touches systemd.
string launcher = Environment.ProcessPath!;
{
    var timers = new FakeTimers();
    var scheduler = new Scheduler(true, timers);
    scheduler.ClearFinished();
    if (scheduler.Read() != null) scheduler.Reset();
    Schedule New(int seconds = 300, int warning = 0) => new(Guid.NewGuid(), PowerAction.Shutdown, DateTimeOffset.UtcNow.AddSeconds(seconds), DateTimeOffset.UtcNow, NativePower.BootId(), warning, true);
    try
    {
        var first = New(warning: 1); scheduler.Register(first, [launcher], null);
        Check(scheduler.Read()?.Id == first.Id, "registration and read-back");
        Check(timers.IsArmed(scheduler.ActionUnit(first.Id)) && timers.IsArmed(scheduler.WarningUnit(first.Id)), "action and warning timers armed");
        Check(timers.Commands[scheduler.ActionUnit(first.Id)].SequenceEqual([launcher, "--execute", first.Id.ToString("D"), "--test"]), "action timer launches the inert worker");
        var second = New(); scheduler.Register(second, [launcher], first.Id);
        Check(scheduler.Read()?.Id == second.Id, "atomic replacement");
        Check(!timers.IsArmed(scheduler.ActionUnit(first.Id)) && !timers.IsArmed(scheduler.WarningUnit(first.Id)), "replacement disarms previous timers");
        Refuse(() => scheduler.Register(New(), [launcher], first.Id), "stale replacement refused");
        Refuse(() => scheduler.Cancel(first.Id), "stale cancellation preserves replacement");
        Check(scheduler.Read()?.Id == second.Id, "replacement still pending after stale cancellation");
        scheduler.Cancel(second.Id);
        Check(scheduler.Read() == null && !timers.IsArmed(scheduler.ActionUnit(second.Id)), "cancellation removes record and timer");
        Check(scheduler.GetReceipt()?.Status == "Cancelled", "cancellation receipt recorded");
        Refuse(() => scheduler.Cancel(second.Id), "absent schedule cancellation reports truth");
        timers.FailNext = true;
        var failing = New(warning: 1);
        try { scheduler.Register(failing, [launcher], null); throw new Exception("FAIL: timer failure surfaced"); }
        catch (InvalidOperationException) { Check(scheduler.Read() == null && !timers.IsArmed(scheduler.WarningUnit(failing.Id)), "failed action timer rolls back warning and never commits"); }
        var oldBoot = New() with { Boot = Guid.NewGuid() }; scheduler.Register(oldBoot, [launcher], null);
        Check(scheduler.Reconcile() == null && scheduler.GetReceipt()?.Status == "Missed", "restart invalidates pending schedule");
        var removed = New(); scheduler.Register(removed, [launcher], null); timers.Disarm(scheduler.ActionUnit(removed.Id));
        Check(scheduler.Reconcile() == null && scheduler.GetReceipt()?.Status == "Missed", "externally stopped timer is reported, not trusted");
        var early = New(); scheduler.Register(early, [launcher], null); scheduler.Execute(early.Id);
        Check(scheduler.Read()?.Id == early.Id, "early worker cannot run action");
        scheduler.Execute(Guid.NewGuid());
        Check(scheduler.Read()?.Id == early.Id, "worker for another schedule is inert");
        scheduler.Cancel(early.Id);
        var due = New(2); scheduler.Register(due, [launcher], null);
        Thread.Sleep(2100); scheduler.Execute(due.Id);
        Check(scheduler.GetReceipt() is { Status: "Diagnostic" } r && r.Id == due.Id, "due worker records inert execution");
        Check(scheduler.Read() == null, "executed schedule is consumed exactly once");
        Refuse(() => scheduler.Cancel(due.Id), "cancellation after execution claim reports too late");
        scheduler.Execute(due.Id);
        Check(scheduler.GetReceipt()?.Status == "Diagnostic", "duplicate worker launch is inert");
        var contended = scheduler.Locked(() => { try { new Scheduler(true, timers).Read(); return false; } catch (InvalidOperationException) { return true; } });
        Check(contended, "per-user lock serializes schedule access");
    }
    finally { if (scheduler.Read() is {} left) scheduler.Cancel(left.Id); scheduler.ClearFinished(); }
}

if (mode == "--integration")
{
    // Real systemd user timers with the inert worker. Requires a logged-in systemd user session.
    string app = Path.GetFullPath(args[1]);
    var scheduler = new Scheduler(true);
    if (scheduler.Read() != null) throw new Exception("An existing diagnostic schedule must be removed before this test.");
    Schedule New(int seconds = 300) => new(Guid.NewGuid(), PowerAction.Sleep, DateTimeOffset.UtcNow.AddSeconds(seconds), DateTimeOffset.UtcNow, NativePower.BootId(), 0, true);
    try
    {
        var first = New(); scheduler.Register(first, [app], null);
        Check(scheduler.Reconcile()?.Id == first.Id, "systemd timer registered and verified");
        scheduler.Cancel(first.Id);
        Check(!new SystemdTimers().IsArmed(scheduler.ActionUnit(first.Id)), "systemd timer removed on cancel");
        var execute = New(8); scheduler.Register(execute, [app], null);
        Console.WriteLine("Waiting for systemd to launch the inert worker…");
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(45) && scheduler.GetReceipt() is not { Status: "Diagnostic" }) Thread.Sleep(500);
        Check(scheduler.GetReceipt() is { Status: "Diagnostic" } result && result.Id == execute.Id, "real timer launches independent worker and records inert execution");
    }
    finally { if (scheduler.Read() is {} left) scheduler.Cancel(left.Id); scheduler.ClearFinished(); }
}
Console.WriteLine($"{passed} checks passed. No shutdown or sleep was requested.");

sealed class FakeTimers : ITimerBackend
{
    public readonly Dictionary<string, IReadOnlyList<string>> Commands = [];
    private readonly HashSet<string> armed = [];
    public bool FailNext;
    public void Arm(string unit, DateTimeOffset at, IReadOnlyList<string> command, string description)
    {
        if (FailNext && unit.Contains("-action-")) { FailNext = false; throw new InvalidOperationException("simulated systemd failure"); }
        armed.Add(unit); Commands[unit] = command;
    }
    public bool IsArmed(string unit) => armed.Contains(unit);
    public void Disarm(string unit) => armed.Remove(unit);
}
