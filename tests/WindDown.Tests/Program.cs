using System.Diagnostics;
using WindDown.Core;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var now = new DateTimeOffset(2026, 9, 4, 22, 0, 0, TimeSpan.Zero);
if (args.Length > 0 && args[0] == "--smoke")
{
    Check(TimePolicy.Duration(now, 24, 0) - now == TimeSpan.FromHours(24), "24-hour maximum remains valid");
    Reject(() => TimePolicy.Duration(now, 24, 1), "duration above 24 hours remains rejected");
    string worker = Path.GetFullPath(args[1]);
    string app = Path.Combine(Path.GetDirectoryName(worker)!, "Wind Down.exe");
    var scheduler = new Scheduler(true);
    if (scheduler.Read() != null) throw new Exception("An existing diagnostic task must be removed before this smoke test.");
    Schedule New(PowerAction action) => new(Guid.NewGuid(), action, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, NativePower.BootId(), 0, true);
    try
    {
        var shutdown = New(PowerAction.Shutdown); scheduler.Register(shutdown, worker, app, null);
        Check(scheduler.Read() is { Action: PowerAction.Shutdown } readShutdown && readShutdown.Id == shutdown.Id, "shutdown schedule initializes through Windows");
        scheduler.Cancel(shutdown.Id); Check(scheduler.Read() == null, "shutdown smoke schedule cancels cleanly");
        var sleep = New(PowerAction.Sleep); scheduler.Register(sleep, worker, app, null);
        Check(scheduler.Read() is { Action: PowerAction.Sleep } readSleep && readSleep.Id == sleep.Id, "sleep schedule initializes through Windows");
        scheduler.Cancel(sleep.Id); Check(scheduler.Read() == null, "sleep smoke schedule cancels cleanly");
        Check(scheduler.GetReceipt()?.Status == "Cancelled", "cancellation receipt records the final smoke result");
    }
    finally { if (scheduler.Read() is {} left) scheduler.Cancel(left.Id); scheduler.ClearFinished(); }
    Console.WriteLine($"{passed} targeted smoke checks passed. No shutdown or sleep API was called.");
    return;
}
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
var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
Reject(() => TimePolicy.Clock(new DateTimeOffset(2026, 3, 8, 6, 0, 0, TimeSpan.Zero), 2, 30, eastern), "DST spring gap rejected");
Check(TimePolicy.Clock(new DateTimeOffset(2026, 11, 1, 5, 45, 0, TimeSpan.Zero), 1, 30, eastern) == new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), "DST fold selects second future occurrence");
Check(TimePolicy.Clock(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), 1, 30, eastern) == new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), "DST fold selects first future occurrence");
Check(NativePower.BootId() != Guid.Empty, "Windows boot session identity available");
NativePower.CheckPrivilege(); Check(true, "shutdown privilege enabled without elevation request");

if (args.Length > 0 && args[0] == "--integration")
{
    string worker = Path.GetFullPath(args[1]);
    string app = Path.Combine(Path.GetDirectoryName(worker)!, "Wind Down.exe");
    var scheduler = new Scheduler(true);
    var legacyScheduler = new Scheduler(true, true);
    if (scheduler.Read() != null) throw new Exception("An existing diagnostic task must be removed before this test.");
    if (legacyScheduler.Read() != null) throw new Exception("An existing legacy diagnostic task must be removed before this test.");
    Schedule New(int seconds = 300) => new(Guid.NewGuid(), PowerAction.Shutdown, DateTimeOffset.UtcNow.AddSeconds(seconds), DateTimeOffset.UtcNow, NativePower.BootId(), 0, true);
    try
    {
        var legacy = New(); legacyScheduler.Register(legacy, worker, app, null);
        Check(legacyScheduler.Read()?.Id == legacy.Id, "legacy task identity registered for migration test");
        scheduler.MigrateLegacy(worker, app);
        Check(scheduler.Read()?.Id == legacy.Id, "active legacy schedule migrated to Wind Down identity");
        Check(legacyScheduler.Read() == null, "legacy task identity removed after migration");
        scheduler.Cancel(legacy.Id);
        var first = New(); scheduler.Register(first, worker, app, null);
        Check(scheduler.Read()?.Id == first.Id, "Windows registration and metadata read-back");
        var second = New(); scheduler.Register(second, worker, app, first.Id);
        Check(scheduler.Read()?.Id == second.Id, "atomic replacement");
        try { scheduler.Cancel(first.Id); throw new Exception("Stale cancellation succeeded"); } catch (InvalidOperationException) { Check(scheduler.Read()?.Id == second.Id, "stale cancellation preserves replacement"); }
        scheduler.Cancel(second.Id); Check(scheduler.Read() == null, "Windows deletion verified");
        try { scheduler.Cancel(second.Id); throw new Exception("Double cancellation falsely succeeded"); } catch (InvalidOperationException) { Check(true, "absent schedule cancellation reports truth"); }
        var oldBoot = New() with { Boot = Guid.NewGuid() }; scheduler.Register(oldBoot, worker, app, null);
        Check(scheduler.Reconcile() == null, "restart invalidates pending schedule");
        Check(scheduler.GetReceipt()?.Status == "Missed", "restart skip receipt recorded");
        var early = New(); scheduler.Register(early, worker, app, null); scheduler.Execute(early.Id);
        Check(scheduler.Read()?.Id == early.Id, "early worker cannot run action"); scheduler.Cancel(early.Id);
        var execute = New(8); scheduler.Register(execute, worker, app, null);
        Console.WriteLine("Waiting for real Windows Task Scheduler to launch the inert worker…");
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(45))
        {
            var receipt = scheduler.GetReceipt();
            if (receipt?.Id == execute.Id && receipt.Status == "Diagnostic") break;
            Thread.Sleep(500);
        }
        Check(scheduler.GetReceipt() is { Status: "Diagnostic" } result && result.Id == execute.Id, "real task launches independent worker and records inert execution");
        Check(scheduler.Read() == null, "executed schedule is consumed exactly once");
        scheduler.Execute(execute.Id);
        Check(scheduler.GetReceipt()?.Id == execute.Id, "duplicate worker launch is inert");
    }
    finally
    {
        if (scheduler.Read() is {} left) scheduler.Cancel(left.Id);
        scheduler.ClearFinished();
        if (legacyScheduler.Read() is {} legacyLeft) legacyScheduler.Cancel(legacyLeft.Id);
        legacyScheduler.ClearFinished();
    }
}
Console.WriteLine($"{passed} checks passed. No shutdown or sleep API was called.");


