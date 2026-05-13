using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Centralise la télémétrie d'exécution : events de cycle de vie, logs,
// remontée de progression vers le state store. Les autres composants du
// moteur (JobRunner, JobOrchestrator) ne touchent jamais directement ni
// _logger ni _store ni les events => une seule classe à modifier pour
// faire évoluer le format des évènements ou des logs.
public sealed class JobTelemetryPublisher
{
    private readonly ILogger _logger;
    private readonly IJobStateStore _store;

    public JobTelemetryPublisher(ILogger logger, IJobStateStore store)
    {
        _logger = logger;
        _store = store;
    }

    public event EventHandler<BackupProgressEventArgs>? ProgressChanged;
    public event EventHandler<JobLifecycleEventArgs>? JobStarted;
    public event EventHandler<JobLifecycleEventArgs>? JobCompleted;
    public event EventHandler<JobLifecycleEventArgs>? JobStopped;
    public event EventHandler<JobLifecycleEventArgs>? JobPaused;
    public event EventHandler<JobLifecycleEventArgs>? JobResumed;

    // === Lifecycle events ===

    public void RaiseStarted(BackupJob job)
        => JobStarted?.Invoke(this, Args(job));

    public void RaisePaused(BackupJob job, PauseReason reason)
        => JobPaused?.Invoke(this, Args(job, reason));

    public void RaiseResumed(BackupJob job, PauseReason reason)
        => JobResumed?.Invoke(this, Args(job, reason));

    // Routage unique statut -> évènement. Le caller fournit un
    // JobExecutionResult ; il n'a pas à choisir lui-même quel event lever.
    public void RaiseFinished(BackupJob job, JobExecutionResult result)
    {
        var args = Args(job);
        if (result.Status == JobExecutionStatus.Completed) JobCompleted?.Invoke(this, args);
        else                                               JobStopped?.Invoke(this, args);
    }

    private static JobLifecycleEventArgs Args(BackupJob job, PauseReason? reason = null) => new()
    {
        JobId = job.Id,
        JobName = job.Name,
        Reason = reason
    };

    // === Logs ===

    public void LogJobStarted(BackupJob job, int parallelism, int totalFiles)
    {
        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            JobId = job.Id,
            BackupName = job.Name,
            Action = LogAction.JobStarted,
            SourceFilePath = job.SourcePath,
            DestinationFilePath = job.TargetPath,
            MaxDegreeOfParallelism = parallelism,
            ChunkCount = totalFiles,
            ThreadId = Environment.CurrentManagedThreadId
        });
    }

    public void LogJobFinished(BackupJob job, JobExecutionResult result, long sizeBytes,
        long elapsedMs, int parallelism, int threadsUsed, int totalFiles)
    {
        // Source de vérité unique : le statut final dicte l'action loggée.
        // - Completed -> JobCompleted
        // - Stopped / Rejected -> JobStopped (annulation explicite)
        // - Failed -> JobStopped + détail de l'erreur dans DestinationFilePath
        var action = result.Status == JobExecutionStatus.Completed
            ? LogAction.JobCompleted
            : LogAction.JobStopped;

        string destination = result.Status == JobExecutionStatus.Failed && result.Error is not null
            ? $"ERROR: {result.Error.GetType().Name}: {result.Error.Message}"
            : job.TargetPath;

        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            JobId = job.Id,
            BackupName = job.Name,
            Action = action,
            SourceFilePath = job.SourcePath,
            DestinationFilePath = destination,
            FileSizeBytes = sizeBytes,
            TransferTimeMs = elapsedMs,
            MaxDegreeOfParallelism = parallelism,
            ThreadsUsed = threadsUsed,
            ChunkCount = totalFiles,
            ThreadId = Environment.CurrentManagedThreadId
        });
    }

    public void LogFileCopied(BackupJob job, string source, string destination,
        bool existedBefore, long sizeBytes, long transferMs, long cryptoMs)
    {
        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            JobId = job.Id,
            BackupName = job.Name,
            Action = existedBefore ? LogAction.Update : LogAction.Create,
            SourceFilePath = source,
            DestinationFilePath = destination,
            FileSizeBytes = sizeBytes,
            TransferTimeMs = transferMs,
            CryptoTimeMs = cryptoMs,
            ThreadId = Environment.CurrentManagedThreadId
        });
    }

    public void LogBusinessSoftwareDetected(BackupJob job, string sourceFile, long sizeBytes)
    {
        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            JobId = job.Id,
            BackupName = job.Name,
            Action = LogAction.BusinessSoftwareDetected,
            SourceFilePath = sourceFile,
            DestinationFilePath = string.Empty,
            FileSizeBytes = sizeBytes,
            TransferTimeMs = 0
        });
    }

    public void LogBusinessSoftwareDetectedAtStart(BackupJob job)
    {
        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            JobId = job.Id,
            BackupName = job.Name,
            Action = LogAction.BusinessSoftwareDetected,
            SourceFilePath = job.SourcePath,
            DestinationFilePath = job.TargetPath,
            FileSizeBytes = 0,
            TransferTimeMs = 0
        });
    }

    // === State store ===

    public void InitState(int jobId, FileScanner.Result scan, int maxParallelFiles)
    {
        _store.UpdateState(jobId, e =>
        {
            e.Status = JobStatus.Active;
            e.TotalFiles = scan.TotalFiles;
            e.TotalSizeBytes = scan.TotalSizeBytes;
            e.RemainingFiles = scan.TotalFiles;
            e.RemainingSizeBytes = scan.TotalSizeBytes;
            e.ProgressPercent = 0;
            e.LastActionTime = DateTime.Now;
            e.MaxParallelFiles = maxParallelFiles;
            e.ThreadsUsed = 0;
            e.ActiveThreads = 0;
        });
    }

    public void MarkPausedOnLockedFile(int jobId, string sourceFile)
    {
        _store.UpdateState(jobId, e =>
        {
            e.Status = JobStatus.Paused;
            e.LastActionTime = DateTime.Now;
            e.CurrentSourceFile = sourceFile;
        });
    }

    public void ClearState(int jobId) => _store.ClearState(jobId);

    public void PushProgress(BackupJob job, string sourceFile, string destinationFile,
        int processed, int totalFiles, long bytesDone, long totalBytes,
        int threadsUsed, int activeThreads)
    {
        double percent = totalFiles == 0 ? 100 : (double)processed * 100 / totalFiles;

        _store.UpdateState(job.Id, e =>
        {
            e.LastActionTime = DateTime.Now;
            e.CurrentSourceFile = sourceFile;
            e.CurrentDestinationFile = destinationFile;
            e.RemainingFiles = totalFiles - processed;
            e.RemainingSizeBytes = totalBytes - bytesDone;
            e.ProgressPercent = percent;
            e.ThreadsUsed = threadsUsed;
            e.ActiveThreads = activeThreads;
        });

        ProgressChanged?.Invoke(this, new BackupProgressEventArgs
        {
            JobId = job.Id,
            JobName = job.Name,
            CurrentSourceFile = sourceFile,
            CurrentDestinationFile = destinationFile,
            TotalFiles = totalFiles,
            ProcessedFiles = processed,
            TotalSizeBytes = totalBytes,
            BytesDone = bytesDone,
            ProgressPercent = percent
        });
    }
}
