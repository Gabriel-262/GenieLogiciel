using System.Text.Json;

namespace EasyLog;

public class EasyLogger : ILogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _fileLock = new();
    private readonly Func<string> _logPathProvider;

    public EasyLogger(Func<string> logPathProvider)
    {
        _logPathProvider = logPathProvider;
    }

    public void Log(LogEntry entry)
    {
        string path = _logPathProvider();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        lock (_fileLock)
        {
            List<LogEntry> entries = ReadExisting(path);
            entries.Add(entry);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
        }
    }

    private static List<LogEntry> ReadExisting(string path)
    {
        if (!File.Exists(path)) return new List<LogEntry>();
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<LogEntry>>(json) ?? new List<LogEntry>();
        }
        catch (JsonException)
        {
            return new List<LogEntry>();
        }
    }
}
