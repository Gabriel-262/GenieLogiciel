using System.Collections.Concurrent;
using System.Diagnostics;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

public class BackupEngine : IBackupEngine
{
    private readonly JobRepository _repo;
    private readonly ILogger _logger;
    private readonly IBusinessSoftwareMonitor? _businessMonitor;
    private readonly ICryptoSoft? _crypto;
    private readonly SettingsService? _settings;

    // Un controller PAR job actif. Encapsule le compteur de causes de pause
    // (User/Business/FileLocked) et le CancellationTokenSource pour le Stop.
    private readonly ConcurrentDictionary<int, JobController> _controllers = new();

    // Sérialise globalement la copie des fichiers >= LargeFileThresholdKb.
    private readonly SemaphoreSlim _largeFileGate = new(1, 1);

    // Gate global pour les extensions prioritaires : tant que _priorityPending
    // > 0 (au moins un fichier prioritaire reste à traiter sur n'importe quel
    // job actif), aucun fichier non prioritaire ne peut être copié.
    private int _priorityPending;
    private readonly ManualResetEventSlim _noPriorityPending = new(true);

    private const int CopyBufferSize = 64 * 1024;

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

    public void Pause(int jobId)        => AddReason(jobId, PauseReason.User);
    public void Resume(int jobId)       => RemoveReason(jobId, PauseReason.User);
    public void Stop(int jobId)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.RequestStop();
    }

    public void PauseAll()
    {
        foreach (var id in _controllers.Keys) AddReason(id, PauseReason.User);
    }
    public void ResumeAll()
    {
        foreach (var id in _controllers.Keys) RemoveReason(id, PauseReason.User);
    }
    public void StopAll()
    {
        foreach (var c in _controllers.Values) c.RequestStop();
    }

    // Appelés par BusinessSoftwareWatcher (cf. App.xaml.cs).
    public void PauseAllForBusinessSoftware()
    {
        foreach (var id in _controllers.Keys) AddReason(id, PauseReason.Business);
    }
    public void ResumeAllAfterBusinessSoftware()
    {
        foreach (var id in _controllers.Keys) RemoveReason(id, PauseReason.Business);
    }

    private void AddReason(int jobId, PauseReason reason)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.AddReason(reason);
    }
    private void RemoveReason(int jobId, PauseReason reason)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.RemoveReason(reason);
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
                    catch { /* skip job en erreur, continue les autres */ }
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
            return; // job déjà en cours
        }

        // Bridge controller -> events publics. Une seule souscription, durée
        // de vie limitée à l'exécution du job.
        controller.Paused  += r => JobPaused?.Invoke(this,
            new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name, Reason = r });
        controller.Resumed += r => JobResumed?.Invoke(this,
            new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name, Reason = r });

        // Si le logiciel métier tourne déjà : on entre directement en pause
        // (cause Business). Le watcher lèvera la cause à la fermeture.
        if (_businessMonitor is not null && _businessMonitor.IsRunning())
        {
            controller.AddReason(PauseReason.Business);
            LogBusinessSoftwareDetected(job);
        }

        Directory.CreateDirectory(job.TargetPath);

        JobStarted?.Invoke(this, new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name });
        bool stopped = false;
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

            int maxParallelFiles = Math.Max(1, _settings?.Current.MaxParallelFilesPerJob ?? 4);
            // On ne passe PAS le StopToken à ParallelOptions : on préfère gérer
            // l'arrêt à la main via IsStopRequested + return, pour éviter toute
            // OperationCanceledException remontant du code utilisateur.
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelFiles
            };

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
                ChunkCount = totalFiles,
                ThreadId = Environment.CurrentManagedThreadId
            });

            // Partition prioritaires / non-prioritaires. Les prioritaires sont
            // traités d'abord ; les non-prioritaires attendent que la file
            // GLOBALE (tous jobs confondus) de fichiers prioritaires soit vide.
            var priorityFiles = new List<FileInfo>();
            var normalFiles = new List<FileInfo>();
            foreach (var f in files)
            {
                if (IsPriority(f.FullName)) priorityFiles.Add(f);
                else normalFiles.Add(f);
            }

            int priorityTotal = priorityFiles.Count;
            if (priorityTotal > 0)
            {
                Interlocked.Add(ref _priorityPending, priorityTotal);
                _noPriorityPending.Reset();
            }
            int priorityDone = 0;

            void ProcessFile(FileInfo sourceFile, bool isPriority)
            {
                try
                {
                    if (controller.IsStopRequested) return;

                    // Gate global : un fichier non prioritaire ne démarre pas
                    // tant qu'il reste des prioritaires en attente.
                    if (!isPriority)
                    {
                        try { _noPriorityPending.Wait(controller.StopToken); }
                        catch (OperationCanceledException) { return; }
                        if (controller.IsStopRequested) return;
                    }

                    int tid = Environment.CurrentManagedThreadId;
                    threadsUsed.TryAdd(tid, 0);

                    // Pause = effective ICI, donc APRÈS le fichier précédent
                    // et AVANT le suivant (conformément au CDC).
                    controller.WaitIfPaused();
                    if (controller.IsStopRequested) return;

                    if (!WaitWhileFileLocked(sourceFile, job, controller)) return;

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

                    long thresholdBytes = (long)Math.Max(0, _settings?.Current.LargeFileThresholdKb ?? 0) * 1024;
                    bool isLarge = thresholdBytes > 0 && sourceFile.Length >= thresholdBytes;
                    if (isLarge) _largeFileGate.Wait();
                    long elapsedMs;
                    try
                    {
                        if (controller.IsStopRequested) return;
                        elapsedMs = CopyFile(sourceFile.FullName, destination, controller.StopToken);
                    }
                    finally { if (isLarge) _largeFileGate.Release(); }
                    if (controller.IsStopRequested) return;

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
                }
                finally
                {
                    if (isPriority)
                    {
                        Interlocked.Increment(ref priorityDone);
                        if (Interlocked.Decrement(ref _priorityPending) == 0)
                            _noPriorityPending.Set();
                    }
                }
            }

            try
            {
                if (priorityTotal > 0)
                    Parallel.ForEach(priorityFiles, parallelOptions, f => ProcessFile(f, true));

                // Filet de sécurité : si on est sorti tôt (stop), les compteurs
                // restants doivent être rendus au gate global pour ne pas bloquer
                // les autres jobs.
                int leftover = priorityTotal - Volatile.Read(ref priorityDone);
                if (leftover > 0)
                {
                    if (Interlocked.Add(ref _priorityPending, -leftover) == 0)
                        _noPriorityPending.Set();
                }

                if (!controller.IsStopRequested && normalFiles.Count > 0)
                    Parallel.ForEach(normalFiles, parallelOptions, f => ProcessFile(f, false));
            }
            finally
            {
                // En cas d'exception inattendue, on s'assure de ne pas avoir
                // laissé de crédit prioritaire fantôme.
                int residual = priorityTotal - Volatile.Read(ref priorityDone);
                if (residual > 0)
                {
                    if (Interlocked.Add(ref _priorityPending, -residual) <= 0)
                        _noPriorityPending.Set();
                }
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
            if (_controllers.TryRemove(job.Id, out var c)) c.Dispose();
            var args = new JobLifecycleEventArgs { JobId = job.Id, JobName = job.Name };
            if (stopped) JobStopped?.Invoke(this, args);
            else         JobCompleted?.Invoke(this, args);
        }
    }

    // ------------------------------------------------------------------------
    // Si le fichier source est verrouillé : on ajoute la cause FileLocked au
    // controller, et on attend (signal pause + check stop) jusqu'à libération.
    // ------------------------------------------------------------------------
    // Retourne true si le fichier est libre (ou l'est devenu), false si Stop
    // a été demandé pendant l'attente. Le caller doit alors abandonner ce
    // fichier et sortir.
    private bool WaitWhileFileLocked(FileInfo sourceFile, BackupJob job, JobController controller)
    {
        if (!IsFileLocked(sourceFile)) return true;

        controller.AddReason(PauseReason.FileLocked);
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

            while (IsFileLocked(sourceFile))
            {
                if (controller.IsStopRequested) return false;
                Thread.Sleep(500);
                sourceFile.Refresh();
            }
            return true;
        }
        finally
        {
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

    private static List<FileInfo> ScanDirectory(string root)
        => new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("~$"))
            .ToList();

    private static bool NeedsCopy(FileInfo source, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return true;
        DateTime sourceTime = source.LastWriteTimeUtc;
        DateTime destTime   = File.GetLastWriteTimeUtc(destinationPath);
        return Math.Abs((sourceTime - destTime).TotalSeconds) > 2;
    }

    // Copie cancellable : check du token toutes les 64 Ko. Si annulée, ferme
    // les streams, supprime la copie partielle, et retourne 0 (pas d'OCE
    // levée vers le code utilisateur). Le caller détecte l'annulation via
    // controller.IsStopRequested après l'appel.
    private static long CopyFile(string source, string destination, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        bool canceled = false;
        try
        {
            using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                                            CopyBufferSize, FileOptions.SequentialScan))
            using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                                            CopyBufferSize, FileOptions.SequentialScan))
            {
                var buffer = new byte[CopyBufferSize];
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (ct.IsCancellationRequested)
                    {
                        canceled = true;
                        break;
                    }
                    dst.Write(buffer, 0, read);
                }
            }
            sw.Stop();
            if (canceled)
            {
                TryDelete(destination);
                return 0;
            }
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            sw.Stop();
            TryDelete(destination);
            return -sw.ElapsedMilliseconds;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
