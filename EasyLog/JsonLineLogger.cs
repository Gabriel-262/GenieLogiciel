using System.Text.Json;

namespace EasyLog;

public class JsonLineLogger : ILogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly object _fileLock = new();
    private readonly Func<string> _logPathProvider;

    public JsonLineLogger(Func<string> logPathProvider)
    {
        _logPathProvider = logPathProvider;
    }

    public void Log(LogEntry entry)
    {
        string path = _logPathProvider();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string line = JsonSerializer.Serialize(entry, JsonOptions);
        lock (_fileLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
