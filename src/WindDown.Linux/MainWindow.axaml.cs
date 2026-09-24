using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using WindDown.Core;

namespace WindDown.App;
public enum Severity { Informational, Success, Warning, Error }

public partial class MainWindow : Window
{
    private readonly Scheduler scheduler = new();
    private readonly Notifications notifications = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Schedule? current;
    private Preferences preferences = new();
    private PowerAction action;
    private bool clockMode, use12Hour, initializing = true, busy, editing, polling, exiting;
    private int durationHours = 2, durationMinutes = 0, clockHour = 23, clockMinute = 0, ticks;
    private Guid? receiptShown;
    private string? receiptStatusShown;
    private int stateRevision;
    public event Action<bool, string>? TrayChanged;

    public MainWindow()
    {
        InitializeComponent();
        Icon = App.LoadIcon();
        if (Screens.Primary is { } screen) Height = Math.Min(880, screen.WorkingArea.Height / screen.Scaling - 40);
        Closing += (_, e) =>
        {
            // systemd owns the schedule, so hiding is only a convenience; relaunching brings the window back.
            if (!exiting && (current != null || busy)) { e.Cancel = true; Hide(); }
            else if (!exiting) { e.Cancel = true; Exit(); }
        };
        try { preferences = Storage.Read<Preferences>("preferences.json") ?? new(); } catch (Exception ex) { Storage.Log(ex); }
        ApplyTheme(preferences.Theme);
        SystemThemeChoice.IsChecked = preferences.Theme == "Default";
        LightThemeChoice.IsChecked = preferences.Theme == "Light";
        DarkThemeChoice.IsChecked = preferences.Theme == "Dark";
        use12Hour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');
        Period.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.AMDesignator is { Length: > 0 } am ? am : "AM");
        Period.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.PMDesignator is { Length: > 0 } pm ? pm : "PM");
        Hours.Configure("Hours", 0, 24, durationHours); Minutes.Configure("Minutes", 0, 59, durationMinutes);
        Hours.Changed += (_, _) => { UpdateDurationMinuteRange(); UpdatePreview(); }; Minutes.Changed += (_, _) => UpdatePreview();
        Warning.SelectedIndex = preferences.WarningMinutes switch { 0 => 0, 1 => 1, 10 => 3, _ => 2 };
        if (!notifications.Available) ShowMessage("Notifications are unavailable", "Your schedule will still run. Install libnotify (notify-send) to get reminders.", Severity.Warning);
        if (!SystemdTimers.Available()) ShowMessage("systemd user session not found", "Wind Down schedules through systemd --user timers. Sign in to a desktop session managed by systemd to schedule.", Severity.Error);
        initializing = false;
        Setup.IsEnabled = false;
        timer.Tick += (_, _) => { UpdateCountdown(); if (++ticks % 8 == 0) { if (current == null || editing) UpdatePreview(); _ = Refresh(); } };
        timer.Start();
        _ = Refresh(); UpdatePreview();
    }
    private static void ApplyTheme(string theme) =>
        Application.Current!.RequestedThemeVariant = theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
    public void ShowWindow() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    public void HandleArguments(string[] args) => ShowWindow();
    public void CancelFromTray() => _ = CancelCurrent();
    private async Task Refresh()
    {
        if (busy || polling) return;
        polling = true;
        int revision = stateRevision;
        try
        {
            var found = await Task.Run(scheduler.Reconcile);
            if (revision != stateRevision || busy) return;
            bool changed = current?.Id != found?.Id;
            current = found;
            if (changed || !Setup.IsEnabled) { editing = false; RenderState(); }
            if (current == null)
            {
                var receipt = await Task.Run(scheduler.GetReceipt);
                if (revision != stateRevision || busy) return;
                if (receipt != null && (receipt.Id != receiptShown || receipt.Status != receiptStatusShown) && receipt.Status != "Cancelled")
                {
                    receiptShown = receipt.Id;
                    receiptStatusShown = receipt.Status;
                    ShowMessage(receipt.Status == "Failed" ? "Linux could not perform the action" : receipt.Status == "Missed" ? "Schedule skipped" : receipt.Status == "Requesting" ? "Power request started" : "Power request accepted", receipt.Message, receipt.Status == "Failed" ? Severity.Error : Severity.Informational);
                }
            }
        }
        catch (Exception ex) { ShowMessage("Unable to verify your schedule", ex.Message, Severity.Error); Setup.IsEnabled = false; }
        finally { polling = false; }
    }
    private void RenderState()
    {
        Setup.IsEnabled = !busy;
        bool active = current != null && !editing;
        Setup.IsVisible = !active;
        Active.IsVisible = active;
        BackButton.IsVisible = editing;
        PageTitle.Text = editing ? "Change schedule" : "Power timer";
        if (current != null)
        {
            string name = current.Action == PowerAction.Shutdown ? "Shutdown" : "Sleep";
            ActiveTitle.Text = name + " scheduled"; CancelButton.Content = "Cancel " + name.ToLowerInvariant();
            ActiveIcon.Data = (Geometry)this.FindResource(current.Action == PowerAction.Shutdown ? "PowerIcon" : "MoonIcon")!;
            ActiveTarget.Text = TimePolicy.TargetLabel(current.Target);
            ActiveWarning.Text = current.WarningMinutes == 0 ? "No warning · Skips missed deadlines" : current.Target.AddMinutes(-current.WarningMinutes) <= current.Created.AddSeconds(5) ? "Immediate reminder · Skips missed deadlines" : $"{current.WarningMinutes}-minute warning · Skips missed deadlines";
        }
        TrayChanged?.Invoke(current != null, current == null ? "Wind Down · Power timer" : $"Wind Down · {(current.Action == PowerAction.Shutdown ? "Shutdown" : "Sleep")} · {TimePolicy.TargetLabel(current.Target)}");
        UpdateCountdown(); UpdatePreview();
    }
    private void UpdateCountdown()
    {
        if (current == null) return;
        var now = DateTimeOffset.UtcNow;
        Countdown.Text = TimePolicy.Countdown(current.Target, now);
        double total = (current.Target - current.Created).TotalSeconds;
        Progress.Value = Math.Clamp((current.Target - now).TotalSeconds / Math.Max(1, total) * 100, 0, 100);
        ActiveTarget.Text = TimePolicy.TargetLabel(current.Target);
        if (now >= current.Target) ActiveTitle.Text = "Waiting for systemd…";
    }
    private DateTimeOffset Target()
    {
        if (!Hours.IsValid || !Minutes.IsValid) throw new ArgumentException($"Enter hours from {(clockMode ? use12Hour ? "1–12" : "0–23" : "0–24")} and minutes from {(clockMode || Hours.Value < 24 ? "0–59" : "0")}.");
        int hour = Hours.Value;
        if (clockMode && use12Hour) hour = hour % 12 + (Period.SelectedIndex == 1 ? 12 : 0);
        return clockMode ? TimePolicy.Clock(DateTimeOffset.UtcNow, hour, Minutes.Value, TimeZoneInfo.Local) : TimePolicy.Duration(DateTimeOffset.UtcNow, hour, Minutes.Value);
    }
    private void UpdatePreview()
    {
        if (initializing) return;
        ScheduleButton.Content = (editing ? "Update " : "Schedule ") + (action == PowerAction.Shutdown ? "shutdown" : "sleep");
        try { TargetPreview.Text = TimePolicy.TargetLabel(Target()); ScheduleButton.IsEnabled = !busy; }
        catch (ArgumentException ex) { TargetPreview.Text = ex.Message; ScheduleButton.IsEnabled = false; }
    }
    private void ChooseAction(PowerAction selected) { action = selected; ShutdownChoice.IsChecked = selected == PowerAction.Shutdown; SleepChoice.IsChecked = selected == PowerAction.Sleep; UpdatePreview(); }
    private void OnShutdown(object? sender, RoutedEventArgs e) => ChooseAction(PowerAction.Shutdown);
    private void OnSleep(object? sender, RoutedEventArgs e) => ChooseAction(PowerAction.Sleep);
    private void SetMode(bool at)
    {
        if (clockMode) { clockHour = Hours.Value % (use12Hour ? 12 : 24) + (use12Hour && Period.SelectedIndex == 1 ? 12 : 0); clockMinute = Minutes.Value; }
        else { durationHours = Hours.Value; durationMinutes = Minutes.Value; }
        clockMode = at; InMode.IsChecked = !at; AtMode.IsChecked = at;
        Hours.Configure(at ? "Hour" : "Hours", at && use12Hour ? 1 : 0, at ? use12Hour ? 12 : 23 : 24, at ? use12Hour ? (clockHour % 12 == 0 ? 12 : clockHour % 12) : clockHour : Math.Min(durationHours, 24));
        Minutes.Configure(at ? "Minute" : "Minutes", 0, at || Hours.Value < 24 ? 59 : 0, at ? clockMinute : durationHours == 24 ? 0 : durationMinutes);
        Period.SelectedIndex = clockHour >= 12 ? 1 : 0;
        Period.IsVisible = at && use12Hour;
        Presets.IsVisible = !at;
        UpdatePreview();
    }
    private void UpdateDurationMinuteRange()
    {
        if (!clockMode && Hours.IsValid) Minutes.SetRange(0, Hours.Value == 24 ? 0 : 59);
    }
    private void OnIn(object? sender, RoutedEventArgs e) => SetMode(false);
    private void OnAt(object? sender, RoutedEventArgs e) => SetMode(true);
    private void OnPeriodChanged(object? sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void OnPreset(object? sender, RoutedEventArgs e) { int mins = int.Parse((string)((Button)sender!).Tag!); Hours.Value = mins / 60; UpdateDurationMinuteRange(); Minutes.Value = mins % 60; UpdatePreview(); }
    private void OnWarningChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (initializing || Warning.SelectedItem is not ComboBoxItem item) return;
        preferences = preferences with { WarningMinutes = int.Parse((string)item.Tag!) };
        SavePreferences();
    }
    private async void OnSchedule(object? sender, RoutedEventArgs e)
    {
        if (busy) return;
        stateRevision++;
        try
        {
            var target = Target();
            var schedule = new Schedule(Guid.NewGuid(), action, target, DateTimeOffset.UtcNow, NativePower.BootId(), preferences.WarningMinutes);
            busy = true; Setup.IsEnabled = false; ScheduleButton.Content = "Scheduling…"; Message.IsVisible = false;
            var previous = current?.Id;
            await Task.Run(() => scheduler.Register(schedule, Program.Launcher, previous));
            current = schedule; editing = false;
            try
            {
                if (previous != null) notifications.Remove(previous.Value);
                // StartNew keeps the inner task (Task.Run would unwrap it).
                var shown = await Task.Factory.StartNew(() => notifications.Show(schedule, target.AddMinutes(-schedule.WarningMinutes) <= DateTimeOffset.UtcNow), CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
                if (shown == null)
                    ShowMessage("Scheduled · notification unavailable", "systemd has your schedule. Check your desktop's notification settings if you want reminders.", Severity.Warning);
                else _ = HandleNotification(schedule.Id, shown);
            }
            catch (Exception ex) { Storage.Log(ex); ShowMessage("Scheduled · notification unavailable", "systemd has your schedule, but the notification could not be shown.", Severity.Warning); }
        }
        catch (Exception ex) { ShowMessage("Could not confirm the schedule", ex.Message, Severity.Error); }
        finally { busy = false; RenderState(); await Refresh(); if (current != null) CancelButton.Focus(); }
    }
    private async Task HandleNotification(Guid id, Task<string?> pending)
    {
        var chosen = await pending;
        if (chosen == "cancel") await Cancel(id);
        else if (chosen == "default") ShowWindow();
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => _ = CancelCurrent();
    private Task CancelCurrent() => current == null ? Task.CompletedTask : Cancel(current.Id);
    private async Task Cancel(Guid id)
    {
        if (busy) return;
        stateRevision++;
        busy = true; CancelButton.IsEnabled = false;
        try
        {
            await Task.Run(() => scheduler.Cancel(id));
            current = null; editing = false; Message.IsVisible = false;
            try { notifications.Remove(id); } catch (Exception ex) { Storage.Log(ex); }
            ShowMessage("Schedule cancelled", "Wind Down won't request a power action.", Severity.Success);
        }
        catch (Exception ex) { ShowMessage("Could not cancel the schedule", ex.Message, Severity.Error); }
        finally { busy = false; CancelButton.IsEnabled = true; RenderState(); await Refresh(); ScheduleButton.Focus(); }
    }
    private void OnEdit(object? sender, RoutedEventArgs e) { editing = true; if (current != null) ChooseAction(current.Action); RenderState(); }
    private void OnBack(object? sender, RoutedEventArgs e) { editing = false; RenderState(); }
    private void OnTheme(object? sender, RoutedEventArgs e)
    {
        var theme = (string)((MenuItem)sender!).Tag!;
        ApplyTheme(theme); preferences = preferences with { Theme = theme }; SavePreferences();
        SystemThemeChoice.IsChecked = theme == "Default"; LightThemeChoice.IsChecked = theme == "Light"; DarkThemeChoice.IsChecked = theme == "Dark";
    }
    private void SavePreferences() { try { Storage.Write("preferences.json", preferences); } catch (Exception ex) { ShowMessage("Settings could not be saved", ex.Message, Severity.Warning); } }
    private void ShowMessage(string title, string detail, Severity severity)
    {
        MessageTitle.Text = title; MessageDetail.Text = detail;
        MessageMark.Background = severity switch
        {
            Severity.Error => new SolidColorBrush(Color.Parse("#D13438")),
            Severity.Warning => new SolidColorBrush(Color.Parse("#E3A21A")),
            Severity.Success => new SolidColorBrush(Color.Parse("#2E9E5B")),
            _ => (IBrush)this.FindResource("AccentBrush")!,
        };
        Message.IsVisible = true;
    }
    private void OnDismissMessage(object? sender, RoutedEventArgs e) => Message.IsVisible = false;
    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var close = new Button { Content = "Got it", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Classes = { "accent" } };
        var dialog = new Window
        {
            Title = "A quiet finish", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = Icon,
            Content = new StackPanel
            {
                Margin = new Thickness(24), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "A quiet finish", FontSize = 20, FontWeight = FontWeight.SemiBold },
                    new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Wind Down 1.0\n\nsystemd keeps your schedule even if Wind Down closes. Stay signed in and keep this installation in place.\n\nYour PC won't be woken to perform an action. Missed deadlines and schedules from before a restart are skipped. A selected clock time that has passed is scheduled for tomorrow.\n\nShutdown asks your desktop session to log out first, so apps can protect unsaved work; an app can prevent it. Sleep availability depends on your PC. Notifications follow your desktop's settings and Do Not Disturb.\n\nTo remove Wind Down, cancel your schedule first, then run `wind-down uninstall` or remove the package." },
                    close,
                },
            },
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
    private void OnExit(object? sender, RoutedEventArgs e) => Exit();
    public void Exit()
    {
        if (exiting || busy) return;
        exiting = true; timer.Stop(); notifications.Dispose();
        (Application.Current as App)?.DisposeTray();
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
