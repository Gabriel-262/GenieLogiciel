namespace EasySave.Models
{
    // Entry for a single file transfer log, to be serialized to JSON
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string BackupName { get; set; } = string.Empty;

        public string SourceFilePath { get; set; } = string.Empty;

        public string DestinationFilePath { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        // Transfer duration in milliseconds.
        public long TransferTimeMs { get; set; }
    }
}