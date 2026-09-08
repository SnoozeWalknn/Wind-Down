using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WindDown.Core;
using Windows.Graphics;

namespace WindDown.App;
public sealed partial class MainWindow : Window
{
    private readonly Scheduler scheduler = new();
    private readonly Notifications notifications = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly nint hwnd;
    private TrayIcon? tray;
    private Schedule? current;
    private Preferences preferences = new();
    private PowerAction action;
    private bool clockMode, use12Hour, initializing = true, busy, editing, polling, exiting;
    private int durationHours = 2, durationMinutes = 0, clockHour = 23, clockMinute = 0, ticks;
    private Guid? receiptShown;
    private string? receiptStatusShown;
    private int stateRevision;
    private bool windowOpened;
    private bool legacyMigrated;

    public MainWindow()
    {
        InitializeComponent();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true; SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop();
        var scale = TrayIcon.GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = (int)(492 * scale), height = Math.Min((int)(880 * scale), area.Height - 40);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "WindDown.ico"));
        AppWindow.Closing += (_, e) =>
        {
            if (!exiting && (current != null || busy) && tray?.Available == true) { e.Cancel = true; AppWindow.Hide(); }
            else if (!exiting && busy) { e.Cancel = true; }
            else Exit();
        };
        try { Storage.MigrateLegacy(); preferences = Storage.Read<Preferences>("preferences.json") ?? new(); } catch (Exception ex) { Storage.Log(ex); }
        if (Enum.TryParse<ElementTheme>(preferences.Theme, out var theme)) Root.RequestedTheme = theme;
        SystemThemeChoice.IsChecked = preferences.Theme == "Default";
        LightThemeChoice.IsChecked = preferences.Theme == "Light";
        DarkThemeChoice.IsChecked = preferences.Theme == "Dark";
        Root.ActualThemeChanged += (_, _) => UpdateCaption();
        UpdateCaption();
        use12Hour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h');
        Period.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.AMDesignator);
        Period.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.PMDesignator);
        Hours.Configure("Hours", 0, 24, durationHours); Minutes.Configure("Minutes", 0, 59, durationMinutes);
        Hours.Changed += (_, _) => { UpdateDurationMinuteRange(); UpdatePreview(); }; Minutes.Changed += (_, _) => UpdatePreview();
        Warning.SelectedIndex = preferences.WarningMinutes switch { 0 => 0, 1 => 1, 10 => 3, _ => 2 };
        try { notifications.Initialize(); } catch (Exception ex) { Storage.Log(ex); ShowMessage("Notifications are unavailable", "Your schedule will still run. Windows may have disabled notifications for this app.", InfoBarSeverity.Warning); }
        try { tray = new TrayIcon(hwnd, ShowWindow, () => _ = CancelCurrent(), Exit); } catch (Exception ex) { Storage.Log(ex); }
        initializing = false;
        Setup.IsEnabled = false;
        timer.Tick += (_, _) => { UpdateCountdown(); if (++ticks % 8 == 0) { if (current == null || editing) UpdatePreview(); _ = Refresh(); } };
        timer.Start();
        _ = Refresh(); UpdatePreview();
    }
    private void UpdateCaption()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }
    public void ShowWindow() { windowOpened = true; AppWindow.Show(); Activate(); TrayIcon.SetForegroundWindow(hwnd); }
    public async void HandleArguments(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--warning" && Guid.TryParse(args[1], out var warningId))
        {
            try
            {
                var schedule = await Task.Run(scheduler.Reconcile);
                if (schedule?.Id == warningId && schedule.Target > DateTimeOffset.UtcNow) notifications.Show(schedule, true);
            }
            catch (Exception ex) { Storage.Log(ex); }
            // A warning-only launch must not leave an invisible application running under
            // Task Scheduler's execution-time limit. An already-open app stays open.
            if (!windowOpened && Program.Arguments.FirstOrDefault() == "--warning") Exit();
            return;
        }
        ShowWindow();
        if (args.FirstOrDefault() is string uriText && Uri.TryCreate(uriText, UriKind.Absolute, out var uri) && uri.Scheme == "wind-down-power-timer" && uri.Host == "cancel")
        {
            var value = uri.Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("id="))?[3..];
            if (Guid.TryParse(value, out var id)) await Cancel(id);
        }
    }
    private async Task Refresh()
    {
        if (busy || polling) return;
        polling = true;
        int revision = stateRevision;
        try
        {
            var found = await Task.Run(() =>
            {
                if (!legacyMigrated)
                {
                    scheduler.MigrateLegacy(Path.Combine(AppContext.BaseDirectory, "WindDown.Worker.exe"), Environment.ProcessPath!);
                    legacyMigrated = true;
                }
                return scheduler.Reconcile();
            });
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
                    ShowMessage(receipt.Status == "Failed" ? "Windows could not perform the action" : receipt.Status == "Missed" ? "Schedule skipped" : receipt.Status == "Requesting" ? "Power request started" : "Power request accepted", receipt.Message, receipt.Status == "Failed" ? InfoBarSeverity.Error : InfoBarSeverity.Informational);
                }
            }
        }
        catch (Exception ex) { ShowMessage("Unable to verify your schedule", ex.Message, InfoBarSeverity.Error); Setup.IsEnabled = false; }
        finally { polling = false; }
    }
    private void RenderState()
    {
        Setup.IsEnabled = !busy;
        bool active = current != null && !editing;
        Setup.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        Active.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = editing ? "Change schedule" : "Power timer";
        if (tray != null) tray.Active = current != null;
        if (current != null)
        {
            string name = current.Action == PowerAction.Shutdown ? "Shutdown" : "Sleep";
            ActiveTitle.Text = name + " scheduled"; CancelButton.Content = "Cancel " + name.ToLowerInvariant();
            ActiveIcon.Glyph = current.Action == PowerAction.Shutdown ? "\uE7E8" : "\uE708";
            ActiveTarget.Text = TimePolicy.TargetLabel(current.Target);
            ActiveWarning.Text = current.WarningMinutes == 0 ? "No warning · Skips missed deadlines" : current.Target.AddMinutes(-current.WarningMinutes) <= current.Created.AddSeconds(5) ? "Immediate reminder · Skips missed deadlines" : $"{current.WarningMinutes}-minute warning · Skips missed deadlines";
        }
        tray?.Update(current == null ? "Wind Down · Power timer" : $"Wind Down · {(current.Action == PowerAction.Shutdown ? "Shutdown" : "Sleep")} · {TimePolicy.TargetLabel(current.Target)}");
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
        if (now >= current.Target) ActiveTitle.Text = "Waiting for Windows…";
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
    private void OnShutdown(object sender, RoutedEventArgs e) => ChooseAction(PowerAction.Shutdown);
    private void OnSleep(object sender, RoutedEventArgs e) => ChooseAction(PowerAction.Sleep);
    private void SetMode(bool at)
    {
        if (clockMode) { clockHour = Hours.Value % (use12Hour ? 12 : 24) + (use12Hour && Period.SelectedIndex == 1 ? 12 : 0); clockMinute = Minutes.Value; }
        else { durationHours = Hours.Value; durationMinutes = Minutes.Value; }
        clockMode = at; InMode.IsChecked = !at; AtMode.IsChecked = at;
        Hours.Configure(at ? "Hour" : "Hours", at && use12Hour ? 1 : 0, at ? use12Hour ? 12 : 23 : 24, at ? use12Hour ? (clockHour % 12 == 0 ? 12 : clockHour % 12) : clockHour : Math.Min(durationHours, 24));
        Minutes.Configure(at ? "Minute" : "Minutes", 0, at || Hours.Value < 24 ? 59 : 0, at ? clockMinute : durationHours == 24 ? 0 : durationMinutes);
        Period.SelectedIndex = clockHour >= 12 ? 1 : 0;
        Period.Visibility = at && use12Hour ? Visibility.Visible : Visibility.Collapsed;
        Presets.Visibility = at ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreview();
    }
    private void UpdateDurationMinuteRange()
    {
        if (!clockMode && Hours.IsValid) Minutes.SetRange(0, Hours.Value == 24 ? 0 : 59);
    }
    private void OnIn(object sender, RoutedEventArgs e) => SetMode(false);
    private void OnAt(object sender, RoutedEventArgs e) => SetMode(true);
    private void OnPeriodChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void OnPreset(object sender, RoutedEventArgs e) { int mins = int.Parse((string)((Button)sender).Tag); Hours.Value = mins / 60; Minutes.Value = mins % 60; UpdatePreview(); }
    private void OnWarningChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        preferences = preferences with { WarningMinutes = int.Parse((string)((ComboBoxItem)Warning.SelectedItem).Tag) };
        SavePreferences();
    }
    private async void OnSchedule(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        stateRevision++;
        try
        {
            var target = Target();
            var schedule = new Schedule(Guid.NewGuid(), action, target, DateTimeOffset.UtcNow, NativePower.BootId(), preferences.WarningMinutes);
            busy = true; Setup.IsEnabled = false; ScheduleButton.Content = "Scheduling…"; Message.IsOpen = false;
            var previous = current?.Id;
            await Task.Run(() => scheduler.Register(schedule, Path.Combine(AppContext.BaseDirectory, "WindDown.Worker.exe"), Environment.ProcessPath!, previous));
            current = schedule; editing = false;
            try
            {
                if (previous != null) await notifications.Remove(previous.Value);
                if (!notifications.Show(schedule, target.AddMinutes(-schedule.WarningMinutes) <= DateTimeOffset.UtcNow))
                    ShowMessage("Scheduled · notification unavailable", "Windows has your schedule. Check Windows notification settings if you want reminders.", InfoBarSeverity.Warning);
            }
            catch (Exception ex) { Storage.Log(ex); ShowMessage("Scheduled · notification unavailable", "Windows has your schedule, but the notification could not be shown.", InfoBarSeverity.Warning); }
        }
        catch (Exception ex) { ShowMessage("Could not confirm the schedule", ex.Message, InfoBarSeverity.Error); }
        finally { busy = false; RenderState(); await Refresh(); if (current != null) CancelButton.Focus(FocusState.Programmatic); }
    }
    private void OnCancel(object sender, RoutedEventArgs e) => _ = CancelCurrent();
    private Task CancelCurrent() => current == null ? Task.CompletedTask : Cancel(current.Id);
    private async Task Cancel(Guid id)
    {
        if (busy) return;
        stateRevision++;
        busy = true; CancelButton.IsEnabled = false;
        try
        {
            await Task.Run(() => scheduler.Cancel(id));
            current = null; editing = false; Message.IsOpen = false;
            try { await notifications.Remove(id); } catch (Exception ex) { Storage.Log(ex); }
            ShowMessage("Schedule cancelled", "Wind Down won't request a power action.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowMessage("Could not cancel the schedule", ex.Message, InfoBarSeverity.Error); }
        finally { busy = false; CancelButton.IsEnabled = true; RenderState(); await Refresh(); ScheduleButton.Focus(FocusState.Programmatic); }
    }
    private void OnEdit(object sender, RoutedEventArgs e) { editing = true; if (current != null) ChooseAction(current.Action); RenderState(); }
    private void OnBack(object sender, RoutedEventArgs e) { editing = false; RenderState(); }
    private void OnTheme(object sender, RoutedEventArgs e)
    {
        var theme = (string)((RadioMenuFlyoutItem)sender).Tag;
        Root.RequestedTheme = Enum.Parse<ElementTheme>(theme); preferences = preferences with { Theme = theme }; SavePreferences(); UpdateCaption();
    }
    private void SavePreferences() { try { Storage.Write("preferences.json", preferences); } catch (Exception ex) { ShowMessage("Settings could not be saved", ex.Message, InfoBarSeverity.Warning); } }
    private void ShowMessage(string title, string detail, InfoBarSeverity severity) { Message.Title = title; Message.Message = detail; Message.Severity = severity; Message.IsOpen = true; }
    private async void OnAbout(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "A quiet finish", CloseButtonText = "Got it", Content = "Wind Down 1.0\n\nWindows keeps your schedule even if Wind Down closes. Stay signed in and keep this installation in place.\n\nYour PC won't be woken to perform an action. Missed deadlines and schedules from before a Windows restart are skipped. A selected clock time that has passed is scheduled for tomorrow.\n\nShutdown lets Windows protect unsaved work; an app can prevent it. Sleep availability depends on your PC. Notifications follow Windows settings and Do Not Disturb.\n\nTo remove Wind Down, cancel your schedule first, then use Uninstall.ps1 in the application folder." };
        await dialog.ShowAsync();
    }
    private void OnExit(object sender, RoutedEventArgs e) => Exit();
    private void Exit() { if (exiting || busy) return; exiting = true; timer.Stop(); tray?.Dispose(); notifications.Dispose(); Application.Current.Exit(); }
}

