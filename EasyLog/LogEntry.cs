namespace EasyLog;

public enum LogAction
{
    Create, Update, Delete, JobUpdated, JobDeleted, BusinessSoftwareDetected,
    // Entrées de synthèse multithreading émises au début et à la fin d'un job.
    JobStarted, JobCompleted
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int JobId { get; set; }
    public string BackupName { get; set; } = string.Empty;
    public LogAction Action { get; set; } = LogAction.Create;
    public string SourceFilePath { get; set; } = string.Empty;
    public string DestinationFilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public long TransferTimeMs { get; set; }

    // 0 = pas de chiffrement, >0 = durée en ms, <0 = code erreur
    public long CryptoTimeMs { get; set; }

    // Multithreading -------------------------------------------------------
    // ID du thread managé qui a traité l'entrée (copie de fichier).
    // 0 sur les entrées de cycle de vie qui ne correspondent à aucun travail.
    public int ThreadId { get; set; }

    // Renseigné sur les entrées de synthèse (JobStarted/JobCompleted).
    // 0 sur les entrées par fichier.
    public int MaxDegreeOfParallelism { get; set; }
    public int ThreadsUsed { get; set; }
    public int ChunkCount { get; set; }
}
