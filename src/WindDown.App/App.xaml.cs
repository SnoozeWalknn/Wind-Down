using Microsoft.UI.Xaml;
using WindDown.Core;

namespace WindDown.App;
public partial class App : Application
{
    public MainWindow? Window { get; private set; }
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Storage.Log(new Exception(e.Message, e.Exception));
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        _ = Program.Listen(a => Window.DispatcherQueue.TryEnqueue(() => Window.HandleArguments(a)));
        Window.HandleArguments(Program.Arguments);
    }
}

