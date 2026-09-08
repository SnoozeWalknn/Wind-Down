using WindDown.Core;

try
{
    if (args.Length >= 2 && args[0] == "--execute" && Guid.TryParse(args[1], out var id))
        new Scheduler(args.Contains("--test")).Execute(id);
}
catch (Exception ex) { Storage.Log(ex); Environment.ExitCode = 1; }


