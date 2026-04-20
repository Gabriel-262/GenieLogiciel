namespace EasySave.Services;

public class PathService
{
    private readonly string _baseDirectory;
    private readonly string _logsDirectory;
    private readonly string _configDirectory;
    private readonly string _stateDirectory;

    public PathService()
    {
        _baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProSoft",
            "EasySave");
        _logsDirectory   = Path.Combine(_baseDirectory, "Logs");
        _configDirectory = Path.Combine(_baseDirectory, "Config");
        _stateDirectory  = Path.Combine(_baseDirectory, "State");

        Directory.CreateDirectory(_logsDirectory);
        Directory.CreateDirectory(_configDirectory);
        Directory.CreateDirectory(_stateDirectory);
    }

    public string GetDailyLogFilePath() =>
        Path.Combine(_logsDirectory, $"{DateTime.Now:yyyy-MM-dd}.json");

    public string GetJobsConfigFilePath() =>
        Path.Combine(_configDirectory, "jobs.json");

    public string GetStateFilePath() =>
        Path.Combine(_stateDirectory, "state.json");
}
