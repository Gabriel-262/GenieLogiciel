namespace EasySave.Services;

public class PathService
{
    public PathService()
    {
        Directory.CreateDirectory(AppConfig.LogDirectory);
        Directory.CreateDirectory(AppConfig.ConfigDirectory);
        Directory.CreateDirectory(AppConfig.StateDirectory);
    }

    public string GetDailyLogFilePath()
    {
        string ext = AppConfig.LogFormat == LogFormat.Xml ? "xml" : "json";
        return Path.Combine(AppConfig.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.{ext}");
    }

    public string GetSettingsFilePath() =>
        Path.Combine(AppConfig.ConfigDirectory, "settings.json");

    public string GetStateFilePath() =>
        Path.Combine(AppConfig.StateDirectory, "state.json");

    public string GetLangFilePath(string code) =>
        Path.Combine(AppConfig.LangDirectory, $"{code}.json");
}
