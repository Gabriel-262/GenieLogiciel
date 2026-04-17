using EasySave.Resources;
using EasySave.Services;
using EasySave.Views;

Translator.SetLanguage(AppConfig.DefaultLanguage);

var pathService  = new PathService();
var jobService   = new BackupJobService(pathService);
var stateService = new StateService(pathService);
var engine       = new BackupEngine(jobService, stateService, pathService);

if (args.Length > 0)
{
    // Headless / CLI mode — task 3.5
    var ids = CliParser.Parse(args[0]);
    if (ids.Count == 0)
    {
        Console.Error.WriteLine(Translator.Get("Error_InvalidArgs"));
        Environment.Exit(1);
        return;
    }
    engine.ExecuteJobs(ids);
    return;
}

// Interactive console mode — task 3.2
var menu = new ConsoleMenu(jobService, engine);
menu.Run();
