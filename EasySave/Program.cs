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
ILogger logger = AppConfig.LogFormat == LogFormat.Xml
    ? new XmlAppendLogger(pathService.GetDailyLogFilePath)
    : new JsonLineLogger(pathService.GetDailyLogFilePath);
repo.SetLogger(logger);
var crypto = new CryptoDispatcher(
    settingsService,
    new XorCryptoService(settingsService),
    new AesCryptoService(settingsService),
    new EciesCryptoService(settingsService));
var engine = new BackupEngine(repo, logger, crypto, settingsService);

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

var mainVm = new MainViewModel(repo, engine, settingsService);
var menu = new ConsoleMenu(mainVm);
menu.Run();
