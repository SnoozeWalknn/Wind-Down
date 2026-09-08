using System.Globalization;
using System.Text.Json;

namespace WindDown.Core;

public enum PowerAction { Shutdown, Sleep }
public sealed record Schedule(Guid Id, PowerAction Action, DateTimeOffset Target, DateTimeOffset Created, Guid Boot, int WarningMinutes, bool Diagnostic = false, int Version = 1, Receipt? Result = null);
public sealed record Receipt(Guid Id, string Status, string Message, DateTimeOffset Recorded);
public sealed record Preferences(string Theme = "Default", int WarningMinutes = 5);

public static class TimePolicy
{
    public static DateTimeOffset Duration(DateTimeOffset now, int hours, int minutes)
    {
        if (hours is < 0 or > 24 || minutes is < 0 or > 59 || (hours == 24 && minutes != 0) || hours + minutes == 0)
            throw new ArgumentException("Choose a duration between 1 minute and 24 hours.");
        return now.AddHours(hours).AddMinutes(minutes);
    }
    public static DateTimeOffset Clock(DateTimeOffset now, int hour, int minute, TimeZoneInfo zone)
    {
        if (hour is < 0 or > 23 || minute is < 0 or > 59) throw new ArgumentException("Enter a valid hour and minute.");
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        for (int day = 0; day < 3; day++)
        {
            var local = DateTime.SpecifyKind(localNow.Date.AddDays(day).AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local))
            {
                if (local > localNow.DateTime) throw new ArgumentException("That time does not exist because the clocks move forward. Choose another time.");
                continue;
            }
            var offsets = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local) : new[] { zone.GetUtcOffset(local) };
            foreach (var candidate in offsets.Select(o => new DateTimeOffset(local, o)).OrderBy(x => x.UtcDateTime))
                if (candidate > now) return candidate.ToUniversalTime();
        }
        throw new ArgumentException("Choose a future time.");
    }
    public static string Countdown(DateTimeOffset target, DateTimeOffset now)
    {
        long total = Math.Max(0, (long)Math.Ceiling((target - now).TotalSeconds));
        return $"{total / 3600:00}:{total / 60 % 60:00}:{total % 60:00}";
    }
    public static string TargetLabel(DateTimeOffset target)
    {
        var local = target.ToLocalTime();
        var day = local.Date == DateTime.Today ? "Today" : local.Date == DateTime.Today.AddDays(1) ? "Tomorrow" : local.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
        return $"{day} at {local.ToString("t", CultureInfo.CurrentCulture)}";
    }
}

public static class Storage
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wind Down");
    public static string LegacyRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Still");
    public static void MigrateLegacy()
    {
        if (!Directory.Exists(LegacyRoot)) return;
        Directory.CreateDirectory(Root);
        foreach (var name in new[] { "preferences.json", "result.json", "last-error.json" })
        {
            var source = Path.Combine(LegacyRoot, name);
            var destination = Path.Combine(Root, name);
            if (File.Exists(source) && !File.Exists(destination)) File.Move(source, destination);
            else if (File.Exists(source)) File.Delete(source);
        }
        var diagnostic = Path.Combine(LegacyRoot, "test-result.json");
        if (File.Exists(diagnostic)) File.Delete(diagnostic);
        if (!Directory.EnumerateFileSystemEntries(LegacyRoot).Any()) Directory.Delete(LegacyRoot);
    }
    public static T? Read<T>(string name)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
    }
    public static void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, name);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Log(Exception error)
    {
        try { Write("last-error.json", new { Time = DateTimeOffset.UtcNow, Error = error.ToString() }); } catch { }
    }
}
