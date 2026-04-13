namespace EasySave.Services;

public class PathService
{
    private const string AppName = "EasySave";

    public string GetAppDataDirectory()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppName);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetLogsDirectory()
    {
        string path = Path.Combine(GetAppDataDirectory(), "Logs");
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetConfigFilePath() =>
        Path.Combine(GetAppDataDirectory(), "jobs.json");

    public string GetStateFilePath() =>
        Path.Combine(GetAppDataDirectory(), "state.json");
}
