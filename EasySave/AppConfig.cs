using EasySave.Models;

namespace EasySave;

public enum LogFormat { Json, Xml }

public static class AppConfig
{
    public const string DefaultLanguage = "en";
    private const int FallbackMaxJobs = 5;

    private static Dictionary<string, string>? _cachedEnv;

    public static AppSettings? Settings { get; set; }

    public static int MaxJobs
    {
        get
        {
            if (Settings?.MaxJobs is int v && v > 0) return v;
            return int.TryParse(Env("MAX_JOBS"), out int e) && e > 0 ? e : FallbackMaxJobs;
        }
    }

    public static LogFormat LogFormat
    {
        get
        {
            string? raw = Settings?.LogFormat ?? Env("LOG_FORMAT");
            return string.Equals(raw, "xml", StringComparison.OrdinalIgnoreCase)
                ? LogFormat.Xml
                : LogFormat.Json;
        }
    }

    public static string LogDirectory    => ResolveDirectory(Settings?.LogPath    ?? Env("LOG_PATH"),    "Logs");
    public static string StateDirectory  => ResolveDirectory(Settings?.StatePath  ?? Env("STATE_PATH"),  "State");
    public static string ConfigDirectory => ResolveDirectory(Settings?.ConfigPath ?? Env("CONFIG_PATH"), "Config");
    public static string LangDirectory   => ResolveDirectory(Settings?.LangPath   ?? Env("LANG_PATH"),   "Lang");

    private static string ResolveDirectory(string? raw, string defaultSubfolder)
    {
        string path = string.IsNullOrWhiteSpace(raw)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, defaultSubfolder)
            : (Path.IsPathRooted(raw)
                ? raw
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, raw));
        return path;
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
            string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
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
