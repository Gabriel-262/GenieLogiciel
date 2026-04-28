using System.Text;
using EasyLog;
using EasySave;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;
using EasySave.Views;

Console.OutputEncoding = Encoding.UTF8;

var pathService     = new PathService();
var settingsService = new SettingsService(pathService);
AppConfig.Settings  = settingsService.Current;

Translator.Initialize(pathService.GetLangFilePath);
Translator.SetLanguage(settingsService.Current.Language);

var repo   = new JobRepository(pathService);
ILogger logger = LoggerFactory.Create(AppConfig.LogFormat, pathService.GetLogDirectory);
repo.SetLogger(logger);
var crypto = new CryptoDispatcher(
    settingsService,
    new XorCryptoService(settingsService),
    new AesCryptoService(settingsService),
    new EciesCryptoService(settingsService));
var engine = new BackupEngine(repo, logger, crypto: crypto, settings: settingsService);

if (args.Length > 0)
{
    var indices = CliParser.Parse(args[0]);
    if (indices.Count == 0)
    {
        Console.Error.WriteLine(Translator.Get("Error_InvalidArgs"));
        Environment.Exit(1);
        return;
    }
    var jobs = repo.GetAllJobs();
    var ids = new List<int>();
    foreach (int index in indices)
    {
        if (index < 1 || index > jobs.Count) continue;
        ids.Add(jobs[index - 1].Id);
    }
    engine.ExecuteJobs(ids);
    return;
}

var mainVm = new MainViewModel(repo, engine, settingsService);
var menu = new ConsoleMenu(mainVm);
menu.Run();
