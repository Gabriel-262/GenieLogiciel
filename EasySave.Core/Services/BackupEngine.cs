using System.Collections.Concurrent;
using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Orchestre l'exécution des jobs : pause/stop, parallélisme, priorité,
// progression, logs. Les sous-responsabilités sont déléguées à :
//   - FileScanner       : énumération + partition prioritaires/normaux.
//   - PriorityGate      : verrou global des fichiers non prioritaires.
//   - CopyService       : copie cancellable + gate des gros fichiers.
//   - JobController     : compteur de causes de pause + Stop par job.
public class BackupEngine : IBackupEngine
{
    private readonly JobRepository _repo;
    private readonly ILogger _logger;
    private readonly IBusinessSoftwareMonitor? _businessMonitor;
    private readonly ICryptoSoft? _crypto;
    private readonly SettingsService? _settings;
    private readonly FileScanner _scanner = new();
    private readonly PriorityGate _priorityGate = new();
    private readonly CopyService _copier = new();

    // Un controller PAR job actif.
    private readonly ConcurrentDictionary<int, JobController> _controllers = new();

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
    public event EventHandler<JobLifecycleEventArgs>? JobStopped;
    public event EventHandler<JobLifecycleEventArgs>? JobPaused;
    public event EventHandler<JobLifecycleEventArgs>? JobResumed;

    public bool IsJobPaused(int jobId)
        => _controllers.TryGetValue(jobId, out var c) && c.IsPaused;

    public IReadOnlyCollection<int> ActiveJobIds => _controllers.Keys.ToArray();

    // ===== API publique de contrôle =====

    public void Pause(int jobId)  => AddReason(jobId, PauseReason.User);

    // Resume utilisateur = FORCE-RESUME : enlève toutes les causes (User ET
    // Business). Choix produit : une fois en pause, seul l'utilisateur peut
    // décider de reprendre. Le logiciel métier qui se ferme ne réveille plus
    // automatiquement le job.
    public void Resume(int jobId) => ForceResume(jobId);
    public void Stop(int jobId)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.RequestStop();
    }

    public void PauseAll()  { foreach (var id in _controllers.Keys) AddReason(id, PauseReason.User); }
    public void ResumeAll() { foreach (var id in _controllers.Keys) ForceResume(id); }
    public void StopAll()   { foreach (var c in _controllers.Values) c.RequestStop(); }

    public void PauseAllForBusinessSoftware()
    {
        var name = _settings?.Current.BusinessSoftwareName;
        foreach (var id in _controllers.Keys)
            if (_controllers.TryGetValue(id, out var c)) c.AddReason(PauseReason.Business, name);
    }
    // Conservé pour compat API. Plus appelé par le watcher : la reprise est
    // désormais manuelle (cf. Server/Program.cs).
    public void ResumeAllAfterBusinessSoftware()
    { foreach (var id in _controllers.Keys) RemoveReason(id, PauseReason.Business); }

    private void AddReason(int jobId, PauseReason reason)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.AddReason(reason);
    }
    private void RemoveReason(int jobId, PauseReason reason)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.RemoveReason(reason);
    }

    // Enlève TOUTES les causes actives du job. Une fois en pause (quelle que
    // soit la cause : User, Business, FileLocked), seul le clic Reprendre
    // utilisateur fait sortir le job. Plus de reprise auto.
    private void ForceResume(int jobId)
    {
        if (!_controllers.TryGetValue(jobId, out var c)) return;
        foreach (var r in c.ActiveReasons)
            c.RemoveReason(r);
    }

    // ===== Exécution =====

    public void ExecuteJobs(IEnumerable<int> jobIds)
        => ExecuteJobsAsync(jobIds).GetAwaiter().GetResult();

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
                    catch (Exception ex)
                    {
                        // Avant : catch { } silencieux. Maintenant on log
                        // pour ne pas perdre la trace en cas de bug.
                        LogJobError(job, ex);
                    }
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

        var controller = new JobController();
        if (!_controllers.TryAdd(job.Id, controller))
        {
            controller.Dispose();
            return;
        }

        controller.Paused  += (r, detail) => JobPaused?.Invoke(this,
            new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name, Reason = r, Detail = detail });
        controller.Resumed += r => JobResumed?.Invoke(this,
            new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name, Reason = r });

        if (_businessMonitor is not null && _businessMonitor.IsRunning())
        {
            controller.AddReason(PauseReason.Business, _settings?.Current.BusinessSoftwareName);
            LogBusinessSoftwareDetected(job);
        }

        Directory.CreateDirectory(job.TargetPath);
        JobStarted?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });

        bool stopped = false;
        try
        {
            var scan = _scanner.Scan(job.SourcePath, IsPriority);

            _repo.UpdateState(job.Id, e =>
            {
                e.Status = JobStatus.Active;
                e.TotalFiles = scan.TotalFiles;
                e.TotalSizeBytes = scan.TotalSizeBytes;
                e.RemainingFiles = scan.TotalFiles;
                e.RemainingSizeBytes = scan.TotalSizeBytes;
                e.ProgressPercent = 0;
                e.LastActionTime = DateTime.Now;
            });

            int maxParallelFiles = Math.Max(1, _settings?.Current.MaxParallelFilesPerJob ?? 4);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxParallelFiles };

            int processed = 0;
            long bytesDone = 0;
            var threadsUsed = new ConcurrentDictionary<int, byte>();
            var jobStopwatch = Stopwatch.StartNew();

            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = job.Id,
                BackupName = job.Name,
                Action = LogAction.JobStarted,
                SourceFilePath = job.SourcePath,
                DestinationFilePath = job.TargetPath,
                MaxDegreeOfParallelism = maxParallelFiles,
                ChunkCount = scan.TotalFiles,
                ThreadId = Environment.CurrentManagedThreadId
            });

            // Réserve auprès du gate global les fichiers prioritaires de ce job.
            _priorityGate.Register(scan.PriorityFiles.Count);
            int priorityDone = 0;

            try
            {
                if (scan.PriorityFiles.Count > 0)
                {
                    // L'overload avec ParallelLoopState permet d'appeler state.Stop()
                    // dès qu'on détecte le Stop utilisateur : Parallel.ForEach
                    // cesse de dispatcher de nouvelles itérations, au lieu de
                    // boucler en return rapide sur N fichiers restants.
                    Parallel.ForEach(scan.PriorityFiles, parallelOptions, (sourceFile, state) =>
                    {
                        if (controller.IsStopRequested) { state.Stop(); return; }
                        try { ProcessFile(job, controller, sourceFile, isPriority: true,
                                          scan.TotalFiles, scan.TotalSizeBytes, threadsUsed,
                                          ref processed, ref bytesDone); }
                        finally
                        {
                            Interlocked.Increment(ref priorityDone);
                            _priorityGate.NotifyOneDone();
                        }
                    });
                }

                // Si on est sorti tôt (stop), rendre les crédits prioritaires
                // restants au gate pour ne pas bloquer les autres jobs.
                int leftover = scan.PriorityFiles.Count - Volatile.Read(ref priorityDone);
                if (leftover > 0) _priorityGate.Release(leftover);

                if (!controller.IsStopRequested && scan.NormalFiles.Count > 0)
                {
                    Parallel.ForEach(scan.NormalFiles, parallelOptions, (sourceFile, state) =>
                    {
                        if (controller.IsStopRequested) { state.Stop(); return; }
                        ProcessFile(job, controller, sourceFile, isPriority: false,
                                    scan.TotalFiles, scan.TotalSizeBytes, threadsUsed,
                                    ref processed, ref bytesDone);
                    });
                }
            }
            finally
            {
                // Filet : si quelque chose a explosé, on ne laisse pas de crédit
                // prioritaire fantôme dans le gate.
                int residual = scan.PriorityFiles.Count - Volatile.Read(ref priorityDone);
                if (residual > 0) _priorityGate.Release(residual);
            }

            stopped = controller.IsStopRequested;
            jobStopwatch.Stop();

            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = job.Id,
                BackupName = job.Name,
                Action = stopped ? LogAction.JobStopped : LogAction.JobCompleted,
                SourceFilePath = job.SourcePath,
                DestinationFilePath = job.TargetPath,
                FileSizeBytes = scan.TotalSizeBytes,
                TransferTimeMs = jobStopwatch.ElapsedMilliseconds,
                MaxDegreeOfParallelism = maxParallelFiles,
                ThreadsUsed = threadsUsed.Count,
                ChunkCount = scan.TotalFiles,
                ThreadId = Environment.CurrentManagedThreadId
            });
        }
        finally
        {
            _repo.ClearState(job.Id);
            if (_controllers.TryRemove(job.Id, out var c)) c.Dispose();
            var args = new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name };
            if (stopped) JobStopped?.Invoke(this, args);
            else         JobCompleted?.Invoke(this, args);
        }
    }

    // Corps de traitement d'un fichier (appelé en parallèle).
    private void ProcessFile(BackupJob job, JobController controller, FileInfo sourceFile, bool isPriority,
        int totalFiles, long totalSize, ConcurrentDictionary<int, byte> threadsUsed,
        ref int processed, ref long bytesDone)
    {
        if (controller.IsStopRequested) return;

        // Les non prioritaires attendent que le gate global soit ouvert.
        if (!isPriority)
        {
            _priorityGate.WaitForNoPriority();
            if (controller.IsStopRequested) return;
        }

        threadsUsed.TryAdd(Environment.CurrentManagedThreadId, 0);

        // Pause = effective ICI, donc APRÈS le fichier précédent et AVANT le
        // suivant (conformément au CDC).
        controller.WaitIfPaused();
        if (controller.IsStopRequested) return;

        if (!WaitWhileFileLocked(sourceFile, job, controller)) return;

        sourceFile.Refresh();
        if (!sourceFile.Exists)
        {
            int doneCount = Interlocked.Increment(ref processed);
            long doneBytes = Interlocked.Read(ref bytesDone);
            PushProgress(job, sourceFile.FullName, string.Empty, doneCount, totalFiles, doneBytes, totalSize);
            return;
        }

        string relative = Path.GetRelativePath(job.SourcePath, sourceFile.FullName);
        string destination = Path.Combine(job.TargetPath, relative);

        if (job.Type == BackupType.Differential && !NeedsCopy(sourceFile, destination))
        {
            int doneCount = Interlocked.Increment(ref processed);
            long doneBytes = Interlocked.Add(ref bytesDone, sourceFile.Length);
            PushProgress(job, sourceFile.FullName, destination, doneCount, totalFiles, doneBytes, totalSize);
            return;
        }

        string? destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        bool existedBefore = File.Exists(destination);
        long thresholdBytes = (long)Math.Max(0, _settings?.Current.LargeFileThresholdKb ?? 0) * 1024;

        long elapsedMs = _copier.Copy(sourceFile.FullName, destination, thresholdBytes, controller.StopToken);
        if (controller.IsStopRequested) return;

        long cryptoMs = 0;
        if (_crypto is not null && elapsedMs >= 0 && ShouldEncrypt(sourceFile.FullName))
            cryptoMs = _crypto.Encrypt(destination);

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
            ThreadId = Environment.CurrentManagedThreadId
        });

        int processedSnap = Interlocked.Increment(ref processed);
        long bytesSnap = Interlocked.Add(ref bytesDone, sourceFile.Length);
        PushProgress(job, sourceFile.FullName, destination, processedSnap, totalFiles, bytesSnap, totalSize);
    }

    private bool WaitWhileFileLocked(FileInfo sourceFile, BackupJob job, JobController controller)
    {
        if (!IsFileLocked(sourceFile)) return true;

        controller.AddReason(PauseReason.FileLocked, sourceFile.FullName);
        try
        {
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

            // 1) Boucle de polling : on attend que le fichier soit libéré.
            while (IsFileLocked(sourceFile))
            {
                if (controller.IsStopRequested) return false;
                Thread.Sleep(500);
                sourceFile.Refresh();
            }

            // 2) Fichier libéré, mais on NE retire PAS la cause FileLocked
            // automatiquement : seul le clic Reprendre utilisateur peut sortir
            // le job de pause. Le signal reste Reset → le thread bloque ici
            // jusqu'à ForceResume.
            controller.WaitIfPaused();
            return !controller.IsStopRequested;
        }
        finally
        {
            // Safety net : si Stop a été demandé, on enlève la cause pour
            // libérer les éventuels autres threads parqués. ForceResume aura
            // déjà fait le job dans le cas nominal.
            if (controller.IsStopRequested)
                controller.RemoveReason(PauseReason.FileLocked);
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
        catch (IOException)            { return true; }
        catch (UnauthorizedAccessException) { return true; }
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

    private void LogJobError(BackupJob job, Exception ex)
    {
        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            JobId = job.Id,
            BackupName = job.Name,
            Action = LogAction.JobStopped,
            SourceFilePath = job.SourcePath,
            DestinationFilePath = $"ERROR: {ex.GetType().Name}: {ex.Message}",
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

    private bool IsPriority(string sourceFilePath)
    {
        var extensions = _settings?.Current.PriorityExtensions;
        if (extensions is null || extensions.Count == 0) return false;
        string ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrEmpty(ext)) return false;
        return extensions.Any(e => string.Equals(
            NormalizeExtension(e), ext, StringComparison.OrdinalIgnoreCase));
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

    private static bool NeedsCopy(FileInfo source, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return true;
        DateTime sourceTime = source.LastWriteTimeUtc;
        DateTime destTime   = File.GetLastWriteTimeUtc(destinationPath);
        return Math.Abs((sourceTime - destTime).TotalSeconds) > 2;
    }
}
