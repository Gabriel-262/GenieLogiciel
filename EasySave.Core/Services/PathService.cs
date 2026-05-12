namespace EasySave.Services;

// Résout les chemins applicatifs (logs, config, state, lang). Pour casser la
// dépendance circulaire avec SettingsService (qui nous appelle pour trouver
// settings.json), on commence par utiliser env + défaut au boot. Après le
// chargement des settings, le bootstrap appelle Bind(settings) pour qu'on
// puisse remonter les chemins paramétrés par l'utilisateur.
public class PathService
{
    private SettingsService? _settings;

    public PathService()
    {
        // Création des dossiers par défaut (env-only à ce stade).
        Directory.CreateDirectory(GetLogDirectory());
        Directory.CreateDirectory(GetConfigDirectory());
        Directory.CreateDirectory(GetStateDirectory());
    }

    public void Bind(SettingsService settings)
    {
        _settings = settings;
        // Si l'utilisateur a paramétré des dossiers personnalisés, on s'assure
        // qu'ils existent maintenant.
        Directory.CreateDirectory(GetLogDirectory());
        Directory.CreateDirectory(GetConfigDirectory());
        Directory.CreateDirectory(GetStateDirectory());
    }

    public string GetLogDirectory()    => AppConfig.ResolveDirectory(_settings?.Current.LogPath,    "LOG_PATH",    "Logs");
    public string GetConfigDirectory() => AppConfig.ResolveDirectory(_settings?.Current.ConfigPath, "CONFIG_PATH", "Config");
    public string GetStateDirectory()  => AppConfig.ResolveDirectory(_settings?.Current.StatePath,  "STATE_PATH",  "State");
    public string GetLangDirectory()   => AppConfig.ResolveDirectory(_settings?.Current.LangPath,   "LANG_PATH",   "Lang");

    public string GetSettingsFilePath()       => Path.Combine(GetConfigDirectory(), "settings.json");
    public string GetStateFilePath()          => Path.Combine(GetStateDirectory(), "state.json");
    public string GetLangFilePath(string code) => Path.Combine(GetLangDirectory(), $"{code}.json");
}
