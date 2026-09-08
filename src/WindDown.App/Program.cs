using System.IO.Pipes;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WindDown.Core;

namespace WindDown.App;
internal static class Program
{
    private static Mutex? instance;
    public static string[] Arguments = [];
    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        instance = new Mutex(true, $"Local\\WindDown-{Scheduler.UserKey}-UI", out bool first);
        if (!first)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", $"WindDown-{Scheduler.UserKey}", PipeDirection.Out);
                client.Connect(5000);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(args));
            }
            catch (Exception ex) { Storage.Log(ex); }
            return;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        GC.KeepAlive(instance);
    }
    public static async Task Listen(Action<string[]> receive)
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream($"WindDown-{Scheduler.UserKey}", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync();
                if (line != null && line.Length < 2048) receive(JsonSerializer.Deserialize<string[]>(line) ?? []);
            }
            catch (Exception ex) { Storage.Log(ex); await Task.Delay(500); }
        }
    }
}

