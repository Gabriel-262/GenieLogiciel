namespace EasySave.Services;

public class PathService
{
    public PathService()
    {
        Directory.CreateDirectory(AppConfig.LogDirectory);
        Directory.CreateDirectory(AppConfig.ConfigDirectory);
        Directory.CreateDirectory(AppConfig.StateDirectory);
    }

    public string GetLogDirectory() => AppConfig.LogDirectory;

    public string GetSettingsFilePath() =>
        Path.Combine(AppConfig.ConfigDirectory, "settings.json");

    public string GetStateFilePath() =>
        Path.Combine(AppConfig.StateDirectory, "state.json");

    public string GetLangFilePath(string code) =>
        Path.Combine(AppConfig.LangDirectory, $"{code}.json");
}
