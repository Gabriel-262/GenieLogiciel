using System.Windows;
using EasySave.Client;
using EasySave.Protocol;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;

namespace EasySave;

public partial class App : Application
{
    public static MainViewModel MainViewModel { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;
    private static RemoteBackupSession? _session;

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_session is not null) await _session.DisposeAsync();
        base.OnExit(e);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // === Bootstrap CLIENT (UI/locale uniquement) ===
        var pathService     = new PathService();
        var settingsService = new SettingsService(pathService);
        AppConfig.Settings  = settingsService.Current;

        Translator.Initialize(pathService.GetLangFilePath);
        Translator.LanguageChanged += SyncLocalizedResources;
        Translator.SetLanguage(settingsService.Current.Language);
        SyncLocalizedResources();

        var theme = string.Equals(settingsService.Current.Theme, "dark", StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.Apply(theme);

        SettingsService = settingsService;

        // === Connexion au serveur EasySave ===
        // 1) tenter 127.0.0.1 (timeout court : si rien n'écoute en local, on
        //    bascule vite sur la saisie manuelle)
        // 2) sinon : prompter l'utilisateur jusqu'à connexion ou abandon.
        _session = await ConnectAsync(settingsService);
        if (_session is null)
        {
            Shutdown();
            return;
        }

        _session.Connection.Disconnected += OnServerDisconnected;

        MainViewModel = new MainViewModel(_session.Repository, _session.Engine, settingsService);

        // Maintenant que la VM est prête, on ouvre la fenêtre principale.
        // (StartupUri retiré d'App.xaml pour pouvoir attendre la connexion.)
        var main = new Views.MainWindow();
        MainWindow = main;
        main.Show();
    }

    private static async Task<RemoteBackupSession?> ConnectAsync(SettingsService settings)
    {
        // 1. Essai localhost
        var local = await RemoteBackupSession.TryConnectAsync(
            ProtocolConstants.DefaultLocalHost,
            ProtocolConstants.DefaultPort,
            TimeSpan.FromMilliseconds(500));
        if (local is not null) return local;

        // 2. Boucle de saisie IP
        string? lastHost = settings.Current.RemoteServerHost;
        int lastPort = settings.Current.RemoteServerPort ?? ProtocolConstants.DefaultPort;
        string? error = "Aucun serveur EasySave détecté en local. Indique l'IP d'un serveur distant.";

        while (true)
        {
            var input = ServerConnectionPrompt.Ask(lastHost, lastPort, error);
            if (input is null) return null; // l'utilisateur a annulé → on ferme l'appli

            var session = await RemoteBackupSession.TryConnectAsync(
                input.Host, input.Port, TimeSpan.FromSeconds(3));
            if (session is not null)
            {
                settings.Current.RemoteServerHost = input.Host;
                settings.Current.RemoteServerPort = input.Port;
                settings.Save();
                return session;
            }

            lastHost = input.Host;
            lastPort = input.Port;
            error = $"Impossible de joindre {input.Host}:{input.Port}. Réessaye.";
        }
    }

    private static void OnServerDisconnected(object? sender, EventArgs e)
    {
        // Choix archi : pas de reconnexion auto. On informe l'utilisateur et
        // on quitte. Au prochain lancement, il ressaisira l'IP si nécessaire.
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            MessageBox.Show(
                "Connexion au serveur EasySave perdue.\nL'application va se fermer.",
                "EasySave",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Application.Current.Shutdown();
        });
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
