using System.Collections.Concurrent;
using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

public class BackupEngine
{
    private readonly JobRepository _repo;
    private readonly ILogger _logger;
    private readonly IBusinessSoftwareMonitor? _businessMonitor;
    private readonly ICryptoSoft? _crypto;
    private readonly SettingsService? _settings;

    // Un signal de reprise PAR job (clé = job.Id).
    // Set = pas de pause. Reset = futur Wait() bloquera jusqu'à Resume(jobId).
    // Avec l'exécution parallèle, partager un signal global ferait qu'un job en
    // pause bloquerait tous les autres au prochain check métier/fichier.
    private readonly ConcurrentDictionary<int, ManualResetEventSlim> _jobSignals = new();

    // Sérialise la copie des fichiers "gros" (>= LargeFileThresholdKb) à travers
    // tous les jobs et toutes les unités de parallélisme. Un seul gros fichier
    // peut occuper la bande passante à un instant T ; les fichiers plus petits
    // continuent à se copier en parallèle.
    private readonly SemaphoreSlim _largeFileGate = new(1, 1);

    public BackupEngine(JobRepository repo, ILogger logger,
        IBusinessSoftwareMonitor? businessMonitor = null,
        ICryptoSoft? crypto = null,
        SettingsService? settings = null)
    {
        _repo = repo;
        _logger = logger;
        _businessMonitor = businessMonitor;
        _crypto = crypto;
        _settings = settings;
    }

    public event EventHandler<BackupProgressEventArgs>? ProgressChanged;
    public event EventHandler<JobLifecycleEventArgs>? JobStarted;
    public event EventHandler<JobLifecycleEventArgs>? JobCompleted;
    public event EventHandler<JobLifecycleEventArgs>? JobPaused;
    public event EventHandler<JobLifecycleEventArgs>? JobResumed;

    // True dès qu'au moins un job est en pause (un de ses signaux est Reset).
    public bool IsPaused => _jobSignals.Values.Any(s => !s.IsSet);

    public bool IsJobPaused(int jobId)
        => _jobSignals.TryGetValue(jobId, out var s) && !s.IsSet;

    private ManualResetEventSlim GetOrCreateSignal(int jobId)
        => _jobSignals.GetOrAdd(jobId, _ => new ManualResetEventSlim(initialState: true));

    // Réveille le thread du job ciblé. Appelé par le bouton "Reprendre" du ViewModel.
    public void Resume(int jobId)
    {
        if (_jobSignals.TryGetValue(jobId, out var s)) s.Set();
    }

    public void ExecuteJobs(IEnumerable<int> jobIds)
    {
        // Variante synchrone (CLI) : on attend la fin du run parallèle.
        ExecuteJobsAsync(jobIds).GetAwaiter().GetResult();
    }

    public Task ExecuteJobsAsync(IEnumerable<int> jobIds, CancellationToken ct = default)
    {
        var ids = jobIds.ToList();
        int max = Math.Max(1, _settings?.Current.MaxParallelJobs ?? 4);
        var sem = new SemaphoreSlim(max);

        var tasks = ids.Select(async id =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (ct.IsCancellationRequested) return;
                var job = _repo.GetJobById(id);
                if (job is null) return;
                await Task.Run(() =>
                {
                    try { ExecuteJob(job); }
                    catch { /* skip failed job, continue with the others */ }
                }, ct).ConfigureAwait(false);
            }
            finally { sem.Release(); }
        });

        return Task.WhenAll(tasks);
    }

    public Task ExecuteJobAsync(BackupJob job, CancellationToken ct = default)
        => Task.Run(() => ExecuteJob(job), ct);

    public void ExecuteJob(BackupJob job)
    {
        if (!Directory.Exists(job.SourcePath)) return;

        // Initialise le signal de pause de ce job (set = libre par défaut).
        GetOrCreateSignal(job.Id);

        // Check pré-job : si le logiciel métier tourne déjà, on pause d'entrée.
        WaitWhileBusinessSoftwareRunning(job);

        Directory.CreateDirectory(job.TargetPath);

        JobStarted?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });
        try
        {
            var files = ScanDirectory(job.SourcePath);
            long totalSize = files.Sum(f => f.Length);
            int totalFiles = files.Count;

            _repo.UpdateState(job.Id, e =>
            {
                e.Status = JobStatus.Active;
                e.TotalFiles = totalFiles;
                e.TotalSizeBytes = totalSize;
                e.RemainingFiles = totalFiles;
                e.RemainingSizeBytes = totalSize;
                e.ProgressPercent = 0;
                e.LastActionTime = DateTime.Now;
            });

            int processed = 0;
            long bytesDone = 0;

            bool continuousBusinessCheck = !string.Equals(
                _settings?.Current.BusinessSoftwareCheckMode, "StartOnly",
                StringComparison.OrdinalIgnoreCase);

            int maxParallelFiles = Math.Max(1, _settings?.Current.MaxParallelFilesPerJob ?? 4);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxParallelFiles };

            // Trace des thread IDs réellement utilisés pour la copie (set thread-safe).
            var threadsUsed = new ConcurrentDictionary<int, byte>();
            var jobStopwatch = Stopwatch.StartNew();

            // Log de synthèse au démarrage : montre la configuration multithread.
            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = job.Id,
                BackupName = job.Name,
                Action = LogAction.JobStarted,
                SourceFilePath = job.SourcePath,
                DestinationFilePath = job.TargetPath,
                MaxDegreeOfParallelism = maxParallelFiles,
                ChunkCount = totalFiles,
                ThreadId = Environment.CurrentManagedThreadId
            });

            // Parallélisation au niveau fichier. Les compteurs partagés (processed,
            // bytesDone) sont mis à jour avec Interlocked pour rester atomiques.
            Parallel.ForEach(files, parallelOptions, sourceFile =>
            {
                int tid = Environment.CurrentManagedThreadId;
                threadsUsed.TryAdd(tid, 0);

                if (continuousBusinessCheck)
                    WaitWhileBusinessSoftwareRunning(job);

                WaitWhileFileLocked(sourceFile, job);

                sourceFile.Refresh();
                if (!sourceFile.Exists)
                {
                    int doneCount = Interlocked.Increment(ref processed);
                    long doneBytes = Interlocked.Read(ref bytesDone);
                    PushProgress(job, sourceFile.FullName, string.Empty,
                        doneCount, totalFiles, doneBytes, totalSize);
                    return;
                }

                string relative = Path.GetRelativePath(job.SourcePath, sourceFile.FullName);
                string destination = Path.Combine(job.TargetPath, relative);

                if (job.Type == BackupType.Differential && !NeedsCopy(sourceFile, destination))
                {
                    int doneCount = Interlocked.Increment(ref processed);
                    long doneBytes = Interlocked.Add(ref bytesDone, sourceFile.Length);
                    PushProgress(job, sourceFile.FullName, destination,
                        doneCount, totalFiles, doneBytes, totalSize);
                    return;
                }

                string? destDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                bool existedBefore = File.Exists(destination);

                // Bande passante : sérialise globalement les copies de gros fichiers.
                long thresholdBytes = (long)Math.Max(0, _settings?.Current.LargeFileThresholdKb ?? 0) * 1024;
                bool isLarge = thresholdBytes > 0 && sourceFile.Length >= thresholdBytes;
                if (isLarge) _largeFileGate.Wait();
                long elapsedMs;
                try { elapsedMs = CopyFile(sourceFile.FullName, destination); }
                finally { if (isLarge) _largeFileGate.Release(); }

                long cryptoMs = 0;
                if (_crypto is not null && elapsedMs >= 0 && ShouldEncrypt(sourceFile.FullName))
                {
                    cryptoMs = _crypto.Encrypt(destination);
                }

                if (elapsedMs >= 0)
                {
                    try { File.SetLastWriteTimeUtc(destination, sourceFile.LastWriteTimeUtc); }
                    catch { /* horodatage best-effort */ }
                }

                _logger.Log(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    JobId = job.Id,
                    BackupName = job.Name,
                    Action = existedBefore ? LogAction.Update : LogAction.Create,
                    SourceFilePath = sourceFile.FullName,
                    DestinationFilePath = destination,
                    FileSizeBytes = sourceFile.Length,
                    TransferTimeMs = elapsedMs,
                    CryptoTimeMs = cryptoMs,
                    ThreadId = tid
                });

                int processedSnap = Interlocked.Increment(ref processed);
                long bytesSnap = Interlocked.Add(ref bytesDone, sourceFile.Length);
                PushProgress(job, sourceFile.FullName, destination,
                    processedSnap, totalFiles, bytesSnap, totalSize);
            });

            jobStopwatch.Stop();

            // Log de synthèse à la fin : nombre de threads réellement utilisés.
            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = job.Id,
                BackupName = job.Name,
                Action = LogAction.JobCompleted,
                SourceFilePath = job.SourcePath,
                DestinationFilePath = job.TargetPath,
                FileSizeBytes = totalSize,
                TransferTimeMs = jobStopwatch.ElapsedMilliseconds,
                MaxDegreeOfParallelism = maxParallelFiles,
                ThreadsUsed = threadsUsed.Count,
                ChunkCount = totalFiles,
                ThreadId = Environment.CurrentManagedThreadId
            });
        }
        finally
        {
            _repo.ClearState(job.Id);
            // Libère le signal de pause associé à ce job.
            if (_jobSignals.TryRemove(job.Id, out var sig)) sig.Dispose();
            JobCompleted?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });
        }
    }

    // ------------------------------------------------------------------------
    // WaitWhileBusinessSoftwareRunning : si le logiciel métier tourne, marque
    // ce job comme Paused et bloque le thread courant sur le signal du job
    // jusqu'à Resume(job.Id) ET disparition du logiciel métier.
    // ------------------------------------------------------------------------
    private void WaitWhileBusinessSoftwareRunning(BackupJob job)
    {
        if (_businessMonitor is null) return;

        var signal = GetOrCreateSignal(job.Id);
        bool wasPaused = false;

        while (_businessMonitor.IsRunning())
        {
            if (!wasPaused)
            {
                wasPaused = true;
                signal.Reset();

                _repo.UpdateState(job.Id, e =>
                {
                    e.Status = JobStatus.Paused;
                    e.LastActionTime = DateTime.Now;
                });

                LogBusinessSoftwareDetected(job);
                JobPaused?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });
            }

            signal.Wait();
        }

        if (wasPaused)
        {
            _repo.UpdateState(job.Id, e =>
            {
                e.Status = JobStatus.Active;
                e.LastActionTime = DateTime.Now;
            });
            JobResumed?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });
        }
    }

    // ------------------------------------------------------------------------
    // WaitWhileFileLocked : si le fichier est verrouillé par une autre app,
    // bloque le thread courant sur le signal de ce job jusqu'à libération.
    // ------------------------------------------------------------------------
    private void WaitWhileFileLocked(FileInfo sourceFile, BackupJob job)
    {
        var signal = GetOrCreateSignal(job.Id);
        bool wasPaused = false;

        while (IsFileLocked(sourceFile))
        {
            if (!wasPaused)
            {
                wasPaused = true;
                signal.Reset();

                _repo.UpdateState(job.Id, e =>
                {
                    e.Status = JobStatus.Paused;
                    e.LastActionTime = DateTime.Now;
                    e.CurrentSourceFile = sourceFile.FullName;
                });

                _logger.Log(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    JobId = job.Id,
                    BackupName = job.Name,
                    Action = LogAction.BusinessSoftwareDetected,
                    SourceFilePath = sourceFile.FullName,
                    DestinationFilePath = string.Empty,
                    FileSizeBytes = sourceFile.Exists ? sourceFile.Length : 0,
                    TransferTimeMs = 0
                });

                JobPaused?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });
            }

            signal.Wait();
            sourceFile.Refresh();
        }

        if (wasPaused)
        {
            _repo.UpdateState(job.Id, e =>
            {
                e.Status = JobStatus.Active;
                e.LastActionTime = DateTime.Now;
            });
            JobResumed?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });
        }
    }

    private static bool IsFileLocked(FileInfo file)
    {
        if (!file.Exists) return false;

        try
        {
            using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void LogBusinessSoftwareDetected(BackupJob job)
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

    private void PushProgress(BackupJob job, string sourceFile, string destinationFile,
        int processed, int totalFiles, long bytesDone, long totalBytes)
    {
        double percent = totalFiles == 0 ? 100 : (double)processed * 100 / totalFiles;

        _repo.UpdateState(job.Id, e =>
        {
            e.LastActionTime = DateTime.Now;
            e.CurrentSourceFile = sourceFile;
            e.CurrentDestinationFile = destinationFile;
            e.RemainingFiles = totalFiles - processed;
            e.RemainingSizeBytes = totalBytes - bytesDone;
            e.ProgressPercent = percent;
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

    private bool ShouldEncrypt(string sourceFilePath)
    {
        var extensions = _settings?.Current.EncryptedExtensions;
        if (extensions is null || extensions.Count == 0) return false;

        string ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrEmpty(ext)) return false;

        return extensions.Any(e => string.Equals(
            NormalizeExtension(e), ext, StringComparison.OrdinalIgnoreCase));
    }

    internal static string NormalizeExtension(string ext)
    {
        ext = ext.Trim().ToLowerInvariant();
        if (ext.Length == 0) return ext;
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    private static List<FileInfo> ScanDirectory(string root)
    {
        return new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("~$"))
            .ToList();
    }

    private static bool NeedsCopy(FileInfo source, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return true;

        DateTime sourceTime = source.LastWriteTimeUtc;
        DateTime destTime   = File.GetLastWriteTimeUtc(destinationPath);
        return Math.Abs((sourceTime - destTime).TotalSeconds) > 2;
    }

    private static long CopyFile(string source, string destination)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            File.Copy(source, destination, true);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            sw.Stop();
            return -sw.ElapsedMilliseconds;
        }
    }
}
