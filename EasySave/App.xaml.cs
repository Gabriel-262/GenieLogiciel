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
        Translator.LanguageChanged += SyncLocalizedResources;
        Translator.SetLanguage(settingsService.Current.Language);
        SyncLocalizedResources();

        var repo   = new JobRepository(pathService);
        ILogger logger = LoggerFactory.Create(AppConfig.LogFormat, pathService.GetLogDirectory);
        repo.SetLogger(logger);

        // TODO (Oscar): instancier ProcessMonitorService (IBusinessSoftwareMonitor) et l'injecter dans BackupEngine.
        var crypto = new CryptoDispatcher(
            settingsService,
            new XorCryptoService(settingsService),
            new AesCryptoService(settingsService),
            new EciesCryptoService(settingsService));
        var engine = new BackupEngine(repo, logger, crypto: crypto, settings: settingsService);

        SettingsService = settingsService;
        MainViewModel = new MainViewModel(repo, engine, settingsService);

        var theme = string.Equals(settingsService.Current.Theme, "dark", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.Apply(theme);
    }

    private static void SyncLocalizedResources()
    {
        if (Current is null) return;
        var res = Current.Resources;
        foreach (var kv in Translator.FallbackStrings)
            res[kv.Key] = kv.Value;
        foreach (var kv in Translator.Strings)
            res[kv.Key] = kv.Value;
    }
}
