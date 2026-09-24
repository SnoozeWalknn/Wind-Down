namespace WindDown.Core;

// The schedule record in the user's data folder is authoritative; the systemd timer is only the
// rendezvous that launches the worker. Every read-modify-write happens under one per-user file
// lock, so the worker's durable Requesting claim and a cancellation can never both succeed.
public sealed class Scheduler
{
    private readonly bool diagnostic;
    private readonly ITimerBackend timers;
    private string Prefix => diagnostic ? "wind-down-test" : "wind-down";
    public string ActionUnit(Guid id) => $"{Prefix}-action-{id:N}";
    public string WarningUnit(Guid id) => $"{Prefix}-warning-{id:N}";
    public string ScheduleName => diagnostic ? "test-schedule.json" : "schedule.json";
    public string ReceiptName => diagnostic ? "test-result.json" : "result.json";
    public Scheduler(bool diagnostic = false, ITimerBackend? timers = null) { this.diagnostic = diagnostic; this.timers = timers ?? new SystemdTimers(); }

    public T Locked<T>(Func<T> operation)
    {
        var path = Path.Combine(PrivateStorage.Ensure(Storage.Root), Prefix + ".lock");
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (true)
        {
            FileStream? handle = null;
            try { handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(50); continue; }
            catch (IOException) { throw new InvalidOperationException("Wind Down is processing this schedule. Please try again."); }
            using (handle) return operation();
        }
    }
    public Schedule? Read() => Locked(ReadUnlocked);
    public Schedule? ReadUnlocked() { var value = ReadRaw(); return value?.Result == null ? value : null; }
    public Receipt? GetReceipt() => Locked(() => ReadRaw()?.Result ?? Storage.Read<Receipt>(ReceiptName));
    private Schedule? ReadRaw()
    {
        Schedule? value;
        try { value = Storage.Read<Schedule>(ScheduleName); }
        catch (System.Text.Json.JsonException) { throw new InvalidDataException("The saved Wind Down schedule is unreadable. Remove it with `wind-down cancel --force`."); }
        if (value == null) return null;
        if (value.Version != 1 || value.Diagnostic != diagnostic || !Enum.IsDefined(value.Action))
            throw new InvalidDataException("This schedule belongs to an incompatible version of Wind Down.");
        return value;
    }
    public void Register(Schedule schedule, IReadOnlyList<string> launcher, Guid? expectedId)
    {
        Locked(() =>
        {
            if (schedule.Diagnostic != diagnostic || launcher.Count == 0 || !File.Exists(launcher[0]))
                throw new InvalidOperationException("Wind Down's installed files are incomplete. Restore the application before scheduling.");
            if (schedule.Target <= DateTimeOffset.UtcNow.AddSeconds(diagnostic ? 1 : 10)) throw new ArgumentException("Choose a time at least a few seconds in the future.");
            if (!diagnostic) NativePower.CheckPrivilege(schedule.Action);
            var old = ReadUnlocked();
            if (old?.Id != expectedId) throw new InvalidOperationException("The schedule changed. Review the current schedule and try again.");
            var armed = new List<string>();
            try
            {
                if (schedule.WarningMinutes > 0 && schedule.Target.AddMinutes(-schedule.WarningMinutes) > DateTimeOffset.UtcNow.AddSeconds(5))
                {
                    timers.Arm(WarningUnit(schedule.Id), schedule.Target.AddMinutes(-schedule.WarningMinutes), [.. launcher, "--warning", schedule.Id.ToString("D")], "Wind Down reminder");
                    armed.Add(WarningUnit(schedule.Id));
                }
                timers.Arm(ActionUnit(schedule.Id), schedule.Target, [.. launcher, "--execute", schedule.Id.ToString("D"), .. diagnostic ? new[] { "--test" } : []], $"Wind Down {(schedule.Action == PowerAction.Shutdown ? "shutdown" : "sleep")}");
                armed.Add(ActionUnit(schedule.Id));
                // Replacing the record is the commit point. A timer left from the old schedule is
                // inert because the worker only acts on the id in this record.
                Storage.Write(ScheduleName, schedule);
            }
            catch
            {
                foreach (var unit in armed) TryDisarm(unit);
                throw;
            }
            if (ReadUnlocked()?.Id != schedule.Id) throw new InvalidOperationException("Wind Down could not confirm the schedule. Reopen Wind Down to check its status.");
            if (old != null) { TryDisarm(ActionUnit(old.Id)); TryDisarm(WarningUnit(old.Id)); }
            return true;
        });
    }
    private void TryDisarm(string unit) { try { timers.Disarm(unit); } catch (Exception ex) { Storage.Log(ex); } }
    public void Cancel(Guid id) => Locked(() =>
    {
        var current = ReadUnlocked();
        if (current == null) throw new InvalidOperationException("This schedule is no longer pending. The power action may already have started.");
        if (current.Id != id) throw new InvalidOperationException("This cancellation belongs to an older schedule. The current schedule has been kept.");
        timers.Disarm(ActionUnit(id));
        TryDisarm(WarningUnit(id));
        File.Delete(Path.Combine(Storage.Root, ScheduleName));
        SaveReceipt(id, "Cancelled", "Schedule cancelled.");
        return true;
    });
    /// <summary>Removes an unreadable or stuck schedule record and every Wind Down timer.</summary>
    public void Reset() => Locked(() =>
    {
        foreach (var unit in ListUnits()) TryDisarm(unit);
        File.Delete(Path.Combine(Storage.Root, ScheduleName));
        return true;
    });
    private IEnumerable<string> ListUnits()
    {
        if (timers is not SystemdTimers) return [];
        var result = Shell.Run("systemctl", "--user", "list-units", "--all", "--plain", "--no-legend", Prefix + "-*.timer");
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .Where(name => name.EndsWith(".timer")).Select(name => name[..^6]).ToList();
    }
    public Schedule? Reconcile() => Locked(() =>
    {
        var schedule = ReadUnlocked();
        if (schedule == null) return null;
        var now = DateTimeOffset.UtcNow;
        if (schedule.Boot != NativePower.BootId() || now > schedule.Target.AddMinutes(1))
        {
            TryDisarm(ActionUnit(schedule.Id)); TryDisarm(WarningUnit(schedule.Id));
            CommitResult(schedule.Id, "Missed", "The schedule was skipped because the PC restarted, was asleep, or the deadline was missed.");
            return null;
        }
        // Near the deadline the timer has legitimately elapsed while the worker waits for the lock.
        if (now < schedule.Target.AddSeconds(-2) && !timers.IsArmed(ActionUnit(schedule.Id)))
        {
            TryDisarm(WarningUnit(schedule.Id));
            CommitResult(schedule.Id, "Missed", "The systemd timer for this schedule was stopped outside Wind Down. No power action will be taken.");
            return null;
        }
        return schedule;
    });
    public void Execute(Guid id) => Locked(() =>
    {
        var schedule = ReadUnlocked();
        if (schedule == null || schedule.Id != id) return false;
        var now = DateTimeOffset.UtcNow;
        if (now < schedule.Target.AddSeconds(-1)) return false;
        if (schedule.Boot != NativePower.BootId() || now > schedule.Target.AddMinutes(1))
        { CommitResult(id, "Missed", "The deadline was missed. No power action was taken."); return false; }
        CommitResult(id, "Requesting", "The deadline was reached. Linux is being asked to perform the action.");
        TryDisarm(WarningUnit(id));
        string via = "";
        try
        {
            if (!diagnostic)
            {
                if (schedule.Action == PowerAction.Shutdown) via = NativePower.Shutdown(); else NativePower.Sleep();
            }
        }
        catch (Exception ex) { CommitResult(id, "Failed", "Linux could not complete the power request: " + ex.Message); Storage.Log(ex); return true; }
        // A receipt update failure is not a failed power request. Preserve Requesting
        // if the request was accepted but the final result cannot be committed.
        if (diagnostic) CommitResult(id, "Diagnostic", "The systemd timer ran successfully. No power action was requested.");
        else CommitResult(id, "Accepted", schedule.Action == PowerAction.Shutdown
            ? $"{(via == "systemd" ? "systemd" : via + " session")} accepted the shutdown request. An app with unsaved work may still prevent shutdown."
            : "systemd accepted the sleep request.");
        return true;
    });
    public void ClearFinished() => Locked(() =>
    {
        if (ReadRaw()?.Result != null) File.Delete(Path.Combine(Storage.Root, ScheduleName));
        if (diagnostic)
        {
            var receipt = Path.Combine(Storage.Root, ReceiptName);
            if (File.Exists(receipt)) File.Delete(receipt);
        }
        return true;
    });
    private void CommitResult(Guid id, string status, string message)
    {
        var schedule = ReadRaw() ?? throw new InvalidOperationException("The schedule changed before execution.");
        if (schedule.Id != id) throw new InvalidOperationException("The schedule changed before execution.");
        Storage.Write(ScheduleName, schedule with { Result = new Receipt(id, status, message, DateTimeOffset.UtcNow) });
        if (ReadRaw()?.Result?.Status != status) throw new IOException("Wind Down could not confirm the execution status.");
    }
    private void SaveReceipt(Guid id, string status, string message)
    {
        // A receipt failure must never reverse a successful cancellation or repeat a power request.
        try { Storage.Write(ReceiptName, new Receipt(id, status, message, DateTimeOffset.UtcNow)); } catch (Exception ex) { Storage.Log(ex); }
    }
}
