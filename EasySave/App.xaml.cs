using System.Windows;
using EasyLog;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;

namespace EasySave;

public partial class App : Application
{
    public static MainViewModel MainViewModel { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var pathService     = new PathService();
        var settingsService = new SettingsService(pathService);
        AppConfig.Settings  = settingsService.Current;

        Translator.Initialize(pathService.GetLangFilePath);
        Translator.SetLanguage(settingsService.Current.Language);

        var repo   = new JobRepository(pathService);
        ILogger logger = LoggerFactory.Create(AppConfig.LogFormat, pathService.GetLogDirectory);
        repo.SetLogger(logger);

        // TODO (Oscar): instancier ProcessMonitorService (IBusinessSoftwareMonitor) et l'injecter dans BackupEngine.
        // TODO (Bastien): instancier CryptoSoftService (ICryptoSoft) et l'injecter dans BackupEngine.
        var engine = new BackupEngine(repo, logger);

        SettingsService = settingsService;
        MainViewModel = new MainViewModel(repo, engine, settingsService);

        var theme = string.Equals(settingsService.Current.Theme, "dark", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.Apply(theme);
    }
}
