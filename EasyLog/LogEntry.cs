namespace EasyLog;

// TODO (Oscar): ajouter LogAction.BusinessSoftwareDetected pour tracer les pauses/blocages.
public enum LogAction { Create, Update, Delete, JobUpdated, JobDeleted }

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string BackupName { get; set; } = string.Empty;
    public LogAction Action { get; set; } = LogAction.Create;
    public string SourceFilePath { get; set; } = string.Empty;
    public string DestinationFilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public long TransferTimeMs { get; set; }

    // TODO (Bastien): ajouter
    //   public long CryptoTimeMs { get; set; }
    // et sérialiser ce champ dans JsonLineLogger + XmlAppendLogger.
}
