using System.Collections.Concurrent;
using System.Diagnostics;
using EasySave.Models;

namespace EasySave.Services;

// Exécute UN job de bout en bout (scan -> copie parallélisée -> finalisation).
// Volontairement sans connaissance du multi-job ni des autres controllers :
// reçoit le JobController de l'extérieur (créé par l'orchestrateur).
//
// Renvoie un JobExecutionResult unique qui détermine ensuite le statut
// loggé et l'évènement levé. Aucune exception n'est propagée hors de Run :
// elles sont capturées et converties en JobExecutionResult.Failed.
public sealed class JobRunner
{
    private readonly JobTelemetryPublisher _telemetry;
    private readonly FileScanner _scanner;
    private readonly PriorityGate _priorityGate;
    private readonly CopyService _copier;
    private readonly ICryptoSoft? _crypto;
    private readonly ISettingsProvider? _settings;

    public JobRunner(
        JobTelemetryPublisher telemetry,
        FileScanner scanner,
        PriorityGate priorityGate,
        CopyService copier,
        ICryptoSoft? crypto,
        ISettingsProvider? settings)
    {
        _telemetry = telemetry;
        _scanner = scanner;
        _priorityGate = priorityGate;
        _copier = copier;
        _crypto = crypto;
        _settings = settings;
    }

    public JobExecutionResult Run(BackupJob job, JobController controller)
    {
        if (!Directory.Exists(job.SourcePath)) return JobExecutionResult.Rejected;

        Directory.CreateDirectory(job.TargetPath);
        _telemetry.RaiseStarted(job);

        try
        {
            var scan = _scanner.Scan(job.SourcePath, IsPriority);

            int maxParallelFiles = Math.Max(1, _settings?.Current.MaxParallelFilesPerJob ?? 4);
            _telemetry.InitState(job.Id, scan, maxParallelFiles);

            int processed = 0;
            long bytesDone = 0;
            int activeThreads = 0;
            var threadsUsed = new ConcurrentDictionary<int, byte>();
            var jobStopwatch = Stopwatch.StartNew();

            _telemetry.LogJobStarted(job, maxParallelFiles, scan.TotalFiles);

            _priorityGate.Register(scan.PriorityFiles.Count);
            int priorityDone = 0;

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxParallelFiles };

            try
            {
                if (scan.PriorityFiles.Count > 0)
                {
                    var partitioner = Partitioner.Create(scan.PriorityFiles,
                        EnumerablePartitionerOptions.NoBuffering);
                    Parallel.ForEach(partitioner, parallelOptions, (sourceFile, state) =>
                    {
                        if (controller.IsStopRequested) { state.Stop(); return; }
                        try
                        {
                            ProcessFile(job, controller, sourceFile, isPriority: true,
                                scan.TotalFiles, scan.TotalSizeBytes, threadsUsed,
                                ref processed, ref bytesDone, ref activeThreads);
                        }
                        finally
                        {
                            Interlocked.Increment(ref priorityDone);
                            _priorityGate.NotifyOneDone();
                        }
                    });
                }

                int leftover = scan.PriorityFiles.Count - Volatile.Read(ref priorityDone);
                if (leftover > 0) _priorityGate.Release(leftover);

                if (!controller.IsStopRequested && scan.NormalFiles.Count > 0)
                {
                    var partitioner = Partitioner.Create(scan.NormalFiles,
                        EnumerablePartitionerOptions.NoBuffering);
                    Parallel.ForEach(partitioner, parallelOptions, (sourceFile, state) =>
                    {
                        if (controller.IsStopRequested) { state.Stop(); return; }
                        ProcessFile(job, controller, sourceFile, isPriority: false,
                            scan.TotalFiles, scan.TotalSizeBytes, threadsUsed,
                            ref processed, ref bytesDone, ref activeThreads);
                    });
                }
            }
            finally
            {
                int residual = scan.PriorityFiles.Count - Volatile.Read(ref priorityDone);
                if (residual > 0) _priorityGate.Release(residual);
            }

            jobStopwatch.Stop();
            var result = controller.IsStopRequested
                ? JobExecutionResult.Stopped
                : JobExecutionResult.Completed;

            _telemetry.LogJobFinished(job, result, scan.TotalSizeBytes,
                jobStopwatch.ElapsedMilliseconds, maxParallelFiles, threadsUsed.Count, scan.TotalFiles);
            return result;
        }
        catch (Exception ex)
        {
            var failed = JobExecutionResult.Failed(ex);
            _telemetry.LogJobFinished(job, failed, 0, 0, 0, 0, 0);
            return failed;
        }
    }

    private void ProcessFile(BackupJob job, JobController controller, FileInfo sourceFile, bool isPriority,
        int totalFiles, long totalSize, ConcurrentDictionary<int, byte> threadsUsed,
        ref int processed, ref long bytesDone, ref int activeThreads)
    {
        if (controller.IsStopRequested) return;

        if (!isPriority)
        {
            _priorityGate.WaitForNoPriority();
            if (controller.IsStopRequested) return;
        }

        threadsUsed.TryAdd(Environment.CurrentManagedThreadId, 0);
        Interlocked.Increment(ref activeThreads);
        try
        {
            ProcessFileCore(job, controller, sourceFile, totalFiles, totalSize,
                threadsUsed, ref processed, ref bytesDone, ref activeThreads);
        }
        finally
        {
            Interlocked.Decrement(ref activeThreads);
        }
    }

    private void ProcessFileCore(BackupJob job, JobController controller, FileInfo sourceFile,
        int totalFiles, long totalSize, ConcurrentDictionary<int, byte> threadsUsed,
        ref int processed, ref long bytesDone, ref int activeThreads)
    {
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
            _telemetry.PushProgress(job, sourceFile.FullName, string.Empty, doneCount, totalFiles,
                doneBytes, totalSize, threadsUsed.Count, Volatile.Read(ref activeThreads));
            return;
        }

        string relative = Path.GetRelativePath(job.SourcePath, sourceFile.FullName);
        string destination = Path.Combine(job.TargetPath, relative);

        if (job.Type == BackupType.Differential && !NeedsCopy(sourceFile, destination))
        {
            int doneCount = Interlocked.Increment(ref processed);
            long doneBytes = Interlocked.Add(ref bytesDone, sourceFile.Length);
            _telemetry.PushProgress(job, sourceFile.FullName, destination, doneCount, totalFiles,
                doneBytes, totalSize, threadsUsed.Count, Volatile.Read(ref activeThreads));
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

        _telemetry.LogFileCopied(job, sourceFile.FullName, destination,
            existedBefore, sourceFile.Length, elapsedMs, cryptoMs);

        int processedSnap = Interlocked.Increment(ref processed);
        long bytesSnap = Interlocked.Add(ref bytesDone, sourceFile.Length);
        _telemetry.PushProgress(job, sourceFile.FullName, destination, processedSnap, totalFiles,
            bytesSnap, totalSize, threadsUsed.Count, Volatile.Read(ref activeThreads));
    }

    private bool WaitWhileFileLocked(FileInfo sourceFile, BackupJob job, JobController controller)
    {
        if (!IsFileLocked(sourceFile)) return true;

        controller.AddReason(PauseReason.FileLocked);
        try
        {
            _telemetry.MarkPausedOnLockedFile(job.Id, sourceFile.FullName);
            _telemetry.LogBusinessSoftwareDetected(job, sourceFile.FullName,
                sourceFile.Exists ? sourceFile.Length : 0);

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

    private static bool NeedsCopy(FileInfo source, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return true;
        DateTime sourceTime = source.LastWriteTimeUtc;
        DateTime destTime   = File.GetLastWriteTimeUtc(destinationPath);
        return Math.Abs((sourceTime - destTime).TotalSeconds) > 2;
    }

    private bool IsPriority(string sourceFilePath)
        => MatchesExtension(sourceFilePath, _settings?.Current.PriorityExtensions);

    private bool ShouldEncrypt(string sourceFilePath)
        => MatchesExtension(sourceFilePath, _settings?.Current.EncryptedExtensions);

    private static bool MatchesExtension(string sourceFilePath, IReadOnlyCollection<string>? extensions)
    {
        if (extensions is null || extensions.Count == 0) return false;
        string ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrEmpty(ext)) return false;
        return extensions.Any(e => string.Equals(NormalizeExtension(e), ext, StringComparison.OrdinalIgnoreCase));
    }

    internal static string NormalizeExtension(string ext)
    {
        ext = ext.Trim().ToLowerInvariant();
        if (ext.Length == 0) return ext;
        return ext.StartsWith('.') ? ext : "." + ext;
    }
}
