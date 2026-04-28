namespace EasyLog;

public enum LogAction { Create, Update, Delete, JobUpdated, JobDeleted, BusinessSoftwareDetected }

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string BackupName { get; set; } = string.Empty;
    public LogAction Action { get; set; } = LogAction.Create;
    public string SourceFilePath { get; set; } = string.Empty;
    public string DestinationFilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public long TransferTimeMs { get; set; }

    // 0 = pas de chiffrement, >0 = durée en ms, <0 = code erreur
    public long CryptoTimeMs { get; set; }
}
