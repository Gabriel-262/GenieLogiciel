using System.Text.Json;

namespace EasyLog;

public class Logger
{
    private readonly string _logDirectory;
    private static readonly object Lock = new();

    public Logger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public void Log(LogEntry entry)
    {
        entry.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string date = DateTime.Now.ToString("yyyy-MM-dd");
        string filePath = Path.Combine(_logDirectory, $"{date}.json");

        lock (Lock)
        {
            List<LogEntry> entries = new();
            if (File.Exists(filePath))
            {
                try
                {
                    entries = JsonSerializer.Deserialize<List<LogEntry>>(
                        File.ReadAllText(filePath)) ?? new();
                }
                catch
                {
                    entries = new();
                }
            }

            entries.Add(entry);
            File.WriteAllText(filePath, JsonSerializer.Serialize(
                entries, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
