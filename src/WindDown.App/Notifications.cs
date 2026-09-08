using Microsoft.Windows.AppNotifications;
using Microsoft.Win32;
using WindDown.Core;
using System.Security;

namespace WindDown.App;
public sealed class Notifications
{
    private bool available;
    public void Initialize()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\still-power-timer", false);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\wind-down-power-timer");
        key.SetValue("", "URL:Wind Down Power Timer"); key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{Environment.ProcessPath}\" \"%1\"");
        AppNotificationManager.Default.Register();
        AppNotificationManager.Default.RemoveByGroupAsync("Still").AsTask().GetAwaiter().GetResult();
        available = true;
    }
    public bool Show(Schedule schedule, bool warning)
    {
        if (!available) return false;
        string action = schedule.Action == PowerAction.Shutdown ? "Shutdown" : "Sleep";
        string title = warning ? $"{action} is coming up" : $"{action} scheduled";
        string body = warning ? $"{TimePolicy.TargetLabel(schedule.Target)}. Save your work, or cancel below." : $"{TimePolicy.TargetLabel(schedule.Target)}. You can close Wind Down; your schedule stays active.";
        string escape(string value) => SecurityElement.Escape(value)!;
        var toast = new AppNotification($"<toast activationType='protocol' launch='wind-down-power-timer://open'><visual><binding template='ToastGeneric'><text>{escape(title)}</text><text>{escape(body)}</text></binding></visual><actions><action content='Cancel {action.ToLowerInvariant()}' activationType='protocol' arguments='wind-down-power-timer://cancel?id={schedule.Id:D}'/></actions></toast>") { Tag = schedule.Id.ToString("N"), Group = "Wind Down", Expiration = schedule.Target };
        AppNotificationManager.Default.Show(toast);
        return toast.Id != 0;
    }
    public async Task Remove(Guid id)
    {
        if (available) await AppNotificationManager.Default.RemoveByTagAndGroupAsync(id.ToString("N"), "Wind Down");
    }
    public void Dispose() { if (available) AppNotificationManager.Default.Unregister(); }
}
