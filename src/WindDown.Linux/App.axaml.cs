using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using WindDown.Core;

namespace WindDown.App;
public partial class App : Application
{
    public MainWindow? Window { get; private set; }
    private TrayIcon? tray;
    private NativeMenuItem? cancelItem;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window = new MainWindow();
            desktop.MainWindow = Window;
            CreateTray(Window);
            _ = Program.Listen(a => Dispatcher.UIThread.Post(() => Window.HandleArguments(a)));
            Window.HandleArguments(Program.Arguments);
        }
        base.OnFrameworkInitializationCompleted();
    }
    public static WindowIcon LoadIcon() => new(AssetLoader.Open(new Uri("avares://WindDown/Assets/WindDown.png")));
    private void CreateTray(MainWindow window)
    {
        try
        {
            var open = new NativeMenuItem("Open Wind Down"); open.Click += (_, _) => window.ShowWindow();
            cancelItem = new NativeMenuItem("Cancel schedule") { IsEnabled = false }; cancelItem.Click += (_, _) => window.CancelFromTray();
            var exit = new NativeMenuItem("Exit Wind Down"); exit.Click += (_, _) => window.Exit();
            tray = new TrayIcon { Icon = LoadIcon(), ToolTipText = "Wind Down · Power timer", Menu = [open, cancelItem, new NativeMenuItemSeparator(), exit], IsVisible = true };
            tray.Clicked += (_, _) => window.ShowWindow();
            TrayIcon.SetIcons(this, [tray]);
            window.TrayChanged += (active, tip) => { if (cancelItem != null) cancelItem.IsEnabled = active; if (tray != null) tray.ToolTipText = tip; };
        }
        catch (Exception ex) { Storage.Log(ex); }
    }
    public void DisposeTray() { if (tray != null) { tray.IsVisible = false; tray.Dispose(); tray = null; } }
}
