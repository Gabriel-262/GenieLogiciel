using System.Text.Json;
using EasySave.Interfaces;
using EasySave.Models;

namespace EasySave.Services
{
    // Writes LogEntry objects to the daily JSON log file in real time
    public class EasyLog : ILogger
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static readonly object FileLock = new object();

        public void Log(LogEntry entry)
        {
            //Resolve daily log file path via PathManager
            string path = PathManager.GetDailyLogFilePath();

            lock (FileLock)
            {
                List<LogEntry> entries = ReadExistingEntries(path);

                entries.Add(entry);

                string json = JsonSerializer.Serialize(entries, JsonOptions);
                File.WriteAllText(path, json);
            }
        }

        private static List<LogEntry> ReadExistingEntries(string path)
        {
            if (!File.Exists(path))
                return new List<LogEntry>();

            try
            {
                string existing = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<LogEntry>>(existing) ?? new List<LogEntry>();
            }
            catch (JsonException)
            {
                return new List<LogEntry>();
            }
        }
    }
}