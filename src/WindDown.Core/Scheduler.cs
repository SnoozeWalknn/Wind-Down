using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace WindDown.Core;

public sealed class Scheduler
{
    public static string UserKey => WindowsIdentity.GetCurrent().User!.Value;
    private readonly bool diagnostic;
    private readonly bool legacyIdentity;
    public string TaskName => $"{(legacyIdentity ? "Still" : "WindDown")}-{UserKey}{(diagnostic ? "-Test" : "")}";
    private string LegacyTaskName => $"Still-{UserKey}{(diagnostic ? "-Test" : "")}";
    private string WarningName(Guid id) => $"{TaskName}-Warning-{id:N}";
    public string ReceiptName => diagnostic ? "test-result.json" : "result.json";
    public Scheduler(bool diagnostic = false, bool legacyIdentity = false) { this.diagnostic = diagnostic; this.legacyIdentity = legacyIdentity; }
    public T Locked<T>(Func<T> operation)
    {
        using var mutex = new Mutex(false, $"Local\\{TaskName}-Schedule");
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(8)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("Windows is processing this schedule. Please try again.");
        try { return operation(); } finally { mutex.ReleaseMutex(); }
    }
    private T WithFolder<T>(Func<dynamic, dynamic, T> operation)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        dynamic? folder = null;
        try { service.Connect(); folder = service.GetFolder("\\"); return operation(service, folder); }
        finally { if (folder != null) Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(service); }
    }
    private static dynamic? Find(dynamic folder, string name)
    {
        try { return folder.GetTask(name); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
    }
    public Schedule? Read() => Locked(() => ReadUnlocked());
    public Schedule? ReadUnlocked() { var value = ReadRaw(); return value?.Result == null ? value : null; }
    public Receipt? GetReceipt() => Locked(() => ReadRaw()?.Result ?? Storage.Read<Receipt>(ReceiptName));
    private Schedule? ReadRaw() => ReadRaw(TaskName);
    private Schedule? ReadRaw(string name) => WithFolder<Schedule?>((service, folder) =>
    {
        dynamic? task = Find(folder, name);
        if (task == null) return null;
        try
        {
            var xml = System.Xml.Linq.XDocument.Parse((string)task.Xml);
            string json = xml.Descendants().First(x => x.Name.LocalName == "Documentation").Value;
            var value = JsonSerializer.Deserialize<Schedule>(json) ?? throw new InvalidDataException("Windows returned an unreadable schedule.");
            if (value.Version != 1 || value.Diagnostic != diagnostic || !Enum.IsDefined(value.Action))
                throw new InvalidDataException("This schedule belongs to an incompatible version of Wind Down.");
            if (value.Result == null && !(bool)task.Enabled) throw new InvalidOperationException("The Wind Down schedule was disabled in Windows Task Scheduler. Enable or remove it there before continuing.");
            return value;
        }
        finally { Marshal.FinalReleaseComObject(task); }
    });
    public void MigrateLegacy(string workerPath, string appPath)
    {
        Locked(() =>
        {
            using var legacyMutex = new Mutex(false, $"Local\\{LegacyTaskName}-Schedule");
            bool acquired;
            try { acquired = legacyMutex.WaitOne(TimeSpan.FromSeconds(8)); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("Windows is processing an earlier Wind Down schedule. Please try again.");
            try
            {
                var legacy = ReadRaw(LegacyTaskName);
                if (legacy == null)
                {
                    DeleteByPrefix(LegacyTaskName + "-Warning-");
                    return true;
                }
                if (legacy.Result != null)
                {
                    try { Storage.Write(ReceiptName, legacy.Result); } catch (Exception ex) { Storage.Log(ex); }
                    Delete(LegacyTaskName);
                    DeleteByPrefix(LegacyTaskName + "-Warning-");
                    return true;
                }
                if (ReadUnlocked() != null)
                {
                    Delete(LegacyTaskName);
                    DeleteByPrefix(LegacyTaskName + "-Warning-");
                    return true;
                }
                if (legacy.Target <= DateTimeOffset.UtcNow.AddSeconds(10))
                    throw new InvalidOperationException("An earlier Wind Down schedule is at its deadline. Wait a moment, then reopen the app.");
                bool warningCreated = false;
                try
                {
                    if (legacy.WarningMinutes > 0 && legacy.Target.AddMinutes(-legacy.WarningMinutes) > DateTimeOffset.UtcNow.AddSeconds(5))
                    {
                        RegisterTask(WarningName(legacy.Id), legacy, legacy.Target.AddMinutes(-legacy.WarningMinutes), appPath, $"--warning {legacy.Id:D}");
                        warningCreated = true;
                    }
                    RegisterTask(TaskName, legacy, legacy.Target, workerPath, $"--execute {legacy.Id:D}{(diagnostic ? " --test" : "")}");
                    if (ReadUnlocked()?.Id != legacy.Id) throw new IOException("Windows did not confirm the renamed schedule.");
                }
                catch
                {
                    if (warningCreated) TryDelete(WarningName(legacy.Id));
                    throw;
                }
                Delete(LegacyTaskName);
                DeleteByPrefix(LegacyTaskName + "-Warning-");
                return true;
            }
            finally { legacyMutex.ReleaseMutex(); }
        });
    }
    public void Register(Schedule schedule, string workerPath, string appPath, Guid? expectedId)
    {
        Locked(() =>
        {
            if (schedule.Diagnostic != diagnostic || !File.Exists(workerPath) || !File.Exists(appPath))
                throw new InvalidOperationException("Wind Down's installed files are incomplete. Restore the application before scheduling.");
            if (schedule.Target <= DateTimeOffset.UtcNow.AddSeconds(diagnostic ? 1 : 10)) throw new ArgumentException("Choose a time at least a few seconds in the future.");
            if (!diagnostic)
            {
                NativePower.CheckPrivilege();
                if (schedule.Action == PowerAction.Sleep && !NativePower.IsPwrSuspendAllowed()) throw new InvalidOperationException("Sleep is not available with this PC's current power configuration.");
            }
            var old = ReadUnlocked();
            if (old?.Id != expectedId) throw new InvalidOperationException("The schedule changed. Review the current schedule and try again.");
            bool warningCreated = false;
            try
            {
                if (schedule.WarningMinutes > 0 && schedule.Target.AddMinutes(-schedule.WarningMinutes) > DateTimeOffset.UtcNow.AddSeconds(5))
                {
                    RegisterTask(WarningName(schedule.Id), schedule, schedule.Target.AddMinutes(-schedule.WarningMinutes), appPath, $"--warning {schedule.Id:D}");
                    warningCreated = true;
                }
                RegisterTask(TaskName, schedule, schedule.Target, workerPath, $"--execute {schedule.Id:D}{(diagnostic ? " --test" : "")}");
            }
            catch
            {
                if (warningCreated) TryDelete(WarningName(schedule.Id));
                throw;
            }
            // Registration is Windows' atomic commit; a read-back failure must not undo an unknown result.
            if (ReadUnlocked()?.Id != schedule.Id) throw new InvalidOperationException("Windows did not confirm the schedule. Reopen Wind Down to check its status.");
            if (old != null) TryDelete(WarningName(old.Id));
            return true;
        });
    }
    private void RegisterTask(string name, Schedule schedule, DateTimeOffset at, string executable, string arguments)
    {
        WithFolder((service, folder) =>
        {
            dynamic definition = service.NewTask(0);
            try
            {
                definition.RegistrationInfo.Author = "Wind Down";
                definition.RegistrationInfo.Description = "A one-time power schedule created by Wind Down.";
                definition.RegistrationInfo.Documentation = JsonSerializer.Serialize(schedule);
                definition.Principal.UserId = UserKey;
                definition.Principal.LogonType = 3; // InteractiveToken: no password, no elevation.
                definition.Principal.RunLevel = 0;
                definition.Settings.Enabled = true;
                definition.Settings.StartWhenAvailable = false;
                definition.Settings.WakeToRun = false;
                definition.Settings.DisallowStartIfOnBatteries = false;
                definition.Settings.StopIfGoingOnBatteries = false;
                definition.Settings.ExecutionTimeLimit = "PT5M";
                definition.Settings.MultipleInstances = 2;
                definition.Settings.DeleteExpiredTaskAfter = "PT24H";
                dynamic trigger = definition.Triggers.Create(1);
                trigger.StartBoundary = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                trigger.EndBoundary = at.AddMinutes(2).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                dynamic action = definition.Actions.Create(0);
                action.Path = Path.GetFullPath(executable);
                action.Arguments = arguments;
                action.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable));
                dynamic registered = folder.RegisterTaskDefinition(name, definition, 6, UserKey, null, 3, null);
                Marshal.FinalReleaseComObject(registered);
                Marshal.FinalReleaseComObject(action);
                Marshal.FinalReleaseComObject(trigger);
            }
            finally { Marshal.FinalReleaseComObject(definition); }
            return true;
        });
    }
    private void Delete(string name) => WithFolder((service, folder) =>
    {
        dynamic? task = Find(folder, name);
        if (task != null) { Marshal.FinalReleaseComObject(task); folder.DeleteTask(name, 0); }
        dynamic? remaining = Find(folder, name);
        if (remaining != null) { Marshal.FinalReleaseComObject(remaining); throw new IOException("Windows did not remove the scheduled task."); }
        return true;
    });
    private void TryDelete(string name) { try { Delete(name); } catch (Exception ex) { Storage.Log(ex); } }
    private void DeleteByPrefix(string prefix) => WithFolder((service, folder) =>
    {
        var names = new List<string>();
        dynamic tasks = folder.GetTasks(0);
        try
        {
            int count = tasks.Count;
            for (int i = 1; i <= count; i++)
            {
                dynamic task = tasks.Item(i);
                try { string name = task.Name; if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(name); }
                finally { Marshal.FinalReleaseComObject(task); }
            }
        }
        finally { Marshal.FinalReleaseComObject(tasks); }
        foreach (var name in names) folder.DeleteTask(name, 0);
        return true;
    });
    public void Cancel(Guid id) => Locked(() =>
    {
        var current = ReadUnlocked();
        if (current == null) throw new InvalidOperationException("This schedule is no longer pending. Windows may already have started the action.");
        if (current.Id != id) throw new InvalidOperationException("This cancellation belongs to an older schedule. The current schedule has been kept.");
        Delete(TaskName);
        TryDelete(WarningName(id));
        SaveReceipt(id, "Cancelled", "Schedule cancelled.");
        return true;
    });
    public Schedule? Reconcile() => Locked(() =>
    {
        var schedule = ReadUnlocked();
        if (schedule == null) return null;
        if (schedule.Boot != NativePower.BootId() || DateTimeOffset.UtcNow > schedule.Target.AddMinutes(1))
        {
            TryDelete(WarningName(schedule.Id));
            CommitResult(schedule.Id, "Missed", "The schedule was skipped because Windows restarted, the PC was asleep, or the deadline was missed.");
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
        CommitResult(id, "Requesting", "The deadline was reached. Windows is being asked to perform the action.");
        try
        {
            if (!diagnostic)
            {
                if (schedule.Action == PowerAction.Shutdown) NativePower.Shutdown(); else NativePower.Sleep();
            }
        }
        catch (Exception ex) { CommitResult(id, "Failed", "Windows could not complete the power request: " + ex.Message); Storage.Log(ex); return true; }
        // A receipt update failure is not a failed power request. Preserve Requesting
        // if Windows accepted the request but the final result cannot be committed.
        if (diagnostic) CommitResult(id, "Diagnostic", "The Windows task ran successfully. No power API was called.");
        else CommitResult(id, "Accepted", schedule.Action == PowerAction.Shutdown ? "Windows accepted the shutdown request. An app with unsaved work may still prevent shutdown." : "Windows accepted the sleep request.");
        return true;
    });
    public void ClearFinished() => Locked(() =>
    {
        if (ReadRaw()?.Result != null) Delete(TaskName);
        if (diagnostic)
        {
            var receipt = Path.Combine(Storage.Root, ReceiptName);
            if (File.Exists(receipt)) File.Delete(receipt);
        }
        return true;
    });
    private void CommitResult(Guid id, string status, string message)
    {
        WithFolder((service, folder) =>
        {
            dynamic task = folder.GetTask(TaskName);
            dynamic definition = task.Definition;
            try
            {
                var schedule = JsonSerializer.Deserialize<Schedule>((string)definition.RegistrationInfo.Documentation)!;
                if (schedule.Id != id) throw new InvalidOperationException("The schedule changed before execution.");
                definition.RegistrationInfo.Documentation = JsonSerializer.Serialize(schedule with { Result = new Receipt(id, status, message, DateTimeOffset.UtcNow) });
                definition.Settings.Enabled = false;
                dynamic updated = folder.RegisterTaskDefinition(TaskName, definition, 4, UserKey, null, 3, null);
                Marshal.FinalReleaseComObject(updated);
            }
            finally { Marshal.FinalReleaseComObject(definition); Marshal.FinalReleaseComObject(task); }
            return true;
        });
        if (ReadRaw()?.Result?.Status != status) throw new IOException("Windows did not confirm the execution status.");
    }
    private void SaveReceipt(Guid id, string status, string message)
    {
        // A receipt failure must never reverse a successful OS cancellation or repeat a power request.
        try { Storage.Write(ReceiptName, new Receipt(id, status, message, DateTimeOffset.UtcNow)); } catch (Exception ex) { Storage.Log(ex); }
    }
}







