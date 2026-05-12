using EasyLog;

namespace EasySave;

// Utilitaires purement statiques liés au système de fichiers et à l'env.
// IMPORTANT : ne contient plus d'état mutable global (l'ancienne propriété
// `Settings` a été supprimée). La source de vérité des paramètres applicatifs
// est maintenant SettingsService, injecté là où c'est nécessaire.
public static class AppConfig
{
    public const string DefaultLanguage = "en";
    internal const int FallbackMaxJobs = 5;

    private static Dictionary<string, string>? _cachedEnv;
    private static string? _cachedBaseDirectory;

    public static string BaseDirectory
    {
        get
        {
            if (_cachedBaseDirectory is not null) return _cachedBaseDirectory;

            string appBase = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(appBase);
            while (dir is not null)
            {
                if (dir.EnumerateFiles("*.sln").Any())
                {
                    _cachedBaseDirectory = dir.FullName;
                    return _cachedBaseDirectory;
                }
                dir = dir.Parent;
            }

            _cachedBaseDirectory = appBase;
            return _cachedBaseDirectory;
        }
    }

    // Résolution d'un répertoire applicatif. Priorité :
    //   1. Chemin fourni par l'utilisateur (depuis les settings, si non vide)
    //   2. Variable d'env (envKey)
    //   3. Sous-dossier par défaut de BaseDirectory
    internal static string ResolveDirectory(string? settingsPath, string envKey, string defaultSubfolder)
    {
        string? raw = !string.IsNullOrWhiteSpace(settingsPath) ? settingsPath : Env(envKey);
        if (string.IsNullOrWhiteSpace(raw))
            return Path.Combine(BaseDirectory, defaultSubfolder);
        return Path.IsPathRooted(raw) ? raw : Path.Combine(BaseDirectory, raw);
    }

    internal static LogFormat ResolveLogFormat(string? settingsValue)
        => LoggerFactory.Parse(settingsValue ?? Env("LOG_FORMAT"));

    internal static int ResolveMaxJobs(int? settingsValue)
    {
        if (settingsValue is int v && v > 0) return v;
        return int.TryParse(Env("MAX_JOBS"), out int e) && e > 0 ? e : FallbackMaxJobs;
    }

    private static string? Env(string key)
    {
        _cachedEnv ??= LoadEnv();
        return _cachedEnv.TryGetValue(key, out string? v) ? v : null;
    }

    private static Dictionary<string, string> LoadEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string envPath = Path.Combine(BaseDirectory, ".env");
            if (!File.Exists(envPath)) envPath = ".env";
            if (!File.Exists(envPath)) return result;

            foreach (string line in File.ReadAllLines(envPath))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                result[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
            }
        }
        catch { }
        return result;
    }
}
