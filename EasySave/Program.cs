using System.Text;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;
using EasySave.Views;

Console.OutputEncoding = Encoding.UTF8;

var pathService     = new PathService();
var settingsService = new SettingsService(pathService);
Translator.SetLanguage(settingsService.Current.Language);

var jobService   = new BackupJobService(pathService);
var stateService = new StateService(pathService);
var engine       = new BackupEngine(jobService, stateService, pathService);

if (args.Length > 0)
{
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

var mainVm = new MainViewModel(jobService, engine, settingsService);
var menu = new ConsoleMenu(mainVm);
menu.Run();
