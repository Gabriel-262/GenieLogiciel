namespace EasySave;

public static class AppConfig
{
    public const string DefaultLanguage = "en";
    private const int FallbackMaxJobs = 5;

    private static int? _cachedMaxJobs;

    public static int MaxJobs
    {
        get
        {
            if (_cachedMaxJobs.HasValue) return _cachedMaxJobs.Value;
            _cachedMaxJobs = LoadMaxJobsFromEnv();
            return _cachedMaxJobs.Value;
        }
    }

    private static int LoadMaxJobsFromEnv()
    {
        try
        {
            string envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (!File.Exists(envPath)) envPath = ".env";
            if (!File.Exists(envPath)) return FallbackMaxJobs;

            string? line = File.ReadLines(envPath)
                .FirstOrDefault(l => l.StartsWith("MAX_JOBS=", StringComparison.OrdinalIgnoreCase));
            if (line is null) return FallbackMaxJobs;

            return int.TryParse(line.Substring("MAX_JOBS=".Length).Trim(), out int v) && v > 0
                ? v
                : FallbackMaxJobs;
        }
        catch
        {
            return FallbackMaxJobs;
        }
    }
}
