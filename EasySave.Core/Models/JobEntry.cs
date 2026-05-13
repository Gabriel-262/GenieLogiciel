namespace EasySave.Models;

public class JobEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public BackupType Type { get; set; } = BackupType.Full;

    public JobStatus Status { get; set; } = JobStatus.Inactive;
    public DateTime LastActionTime { get; set; } = DateTime.Now;
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public double ProgressPercent { get; set; }
    public int RemainingFiles { get; set; }
    public long RemainingSizeBytes { get; set; }
    public string CurrentSourceFile { get; set; } = string.Empty;
    public string CurrentDestinationFile { get; set; } = string.Empty;

    // Observabilité du multithreading :
    //  - MaxParallelFiles : limite configurée (MaxParallelFilesPerJob).
    //  - ThreadsUsed      : nombre de threads distincts ayant traité un
    //                       fichier de ce job (cumul depuis le démarrage).
    //  - ActiveThreads    : nombre de threads actuellement dans ProcessFile.
    public int MaxParallelFiles { get; set; }
    public int ThreadsUsed { get; set; }
    public int ActiveThreads { get; set; }

    public BackupJob ToJob() => new()
    {
        Id = Id,
        Name = Name,
        SourcePath = SourcePath,
        TargetPath = TargetPath,
        Type = Type
    };

    public static JobEntry FromJob(BackupJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        SourcePath = job.SourcePath,
        TargetPath = job.TargetPath,
        Type = job.Type
    };
}
