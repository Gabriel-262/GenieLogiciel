using System.Diagnostics;
using System.Security.Cryptography;
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
    }

    public event EventHandler<BackupProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? JobStarted;
    public event EventHandler<string>? JobCompleted;

    public void ExecuteJobs(IEnumerable<int> jobIds)
    {
        foreach (int id in jobIds)
        {
            var job = _repo.GetJobById(id);
            if (job is null) continue;
            ExecuteJob(job);
        }
    }

    public void ExecuteJob(BackupJob job)
    {
        if (!Directory.Exists(job.SourcePath)) return;

        if (_businessMonitor?.IsRunning() == true)
        {
            LogBusinessSoftwareDetected(job);
            return;
        }

        Directory.CreateDirectory(job.TargetPath);

        JobStarted?.Invoke(this, job.Name);

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

        foreach (var sourceFile in files)
        {
            string relative = Path.GetRelativePath(job.SourcePath, sourceFile.FullName);
            string destination = Path.Combine(job.TargetPath, relative);

            if (job.Type == BackupType.Differential && !NeedsCopy(sourceFile, destination))
            {
                processed++;
                bytesDone += sourceFile.Length;
                PushProgress(job, sourceFile.FullName, destination, processed, totalFiles, bytesDone, totalSize);
                continue;
            }

            string? destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            bool existedBefore = File.Exists(destination);
            long elapsedMs = CopyFile(sourceFile.FullName, destination);

            long cryptoMs = 0;
            if (_crypto is not null && elapsedMs >= 0 && ShouldEncrypt(sourceFile.FullName))
            {
                cryptoMs = _crypto.Encrypt(destination);
            }

            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                BackupName = job.Name,
                Action = existedBefore ? LogAction.Update : LogAction.Create,
                SourceFilePath = sourceFile.FullName,
                DestinationFilePath = destination,
                FileSizeBytes = sourceFile.Length,
                TransferTimeMs = elapsedMs,
                CryptoTimeMs = cryptoMs
            });

            processed++;
            bytesDone += sourceFile.Length;
            PushProgress(job, sourceFile.FullName, destination, processed, totalFiles, bytesDone, totalSize);

            if (_businessMonitor?.IsRunning() == true)
            {
                LogBusinessSoftwareDetected(job);
                _repo.ClearState(job.Id);
                JobCompleted?.Invoke(this, job.Name);
                return;
            }
        }

        RemoveOrphans(job, files);

        _repo.ClearState(job.Id);
        JobCompleted?.Invoke(this, job.Name);
    }

    private void LogBusinessSoftwareDetected(BackupJob job)
    {
        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            BackupName = job.Name,
            Action = LogAction.BusinessSoftwareDetected,
            SourceFilePath = job.SourcePath,
            DestinationFilePath = job.TargetPath,
            FileSizeBytes = 0,
            TransferTimeMs = 0
        });
    }

    private void RemoveOrphans(BackupJob job, List<FileInfo> sourceFiles)
    {
        if (!Directory.Exists(job.TargetPath)) return;

        var expected = new HashSet<string>(
            sourceFiles.Select(f => Path.Combine(job.TargetPath, Path.GetRelativePath(job.SourcePath, f.FullName))),
            StringComparer.OrdinalIgnoreCase);

        foreach (var destFile in new DirectoryInfo(job.TargetPath).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (expected.Contains(destFile.FullName)) continue;

            long size = destFile.Length;
            string fullPath = destFile.FullName;
            try { destFile.Delete(); }
            catch { continue; }

            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                BackupName = job.Name,
                Action = LogAction.Delete,
                SourceFilePath = string.Empty,
                DestinationFilePath = fullPath,
                FileSizeBytes = size,
                TransferTimeMs = 0
            });
        }
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
            .ToList();
    }

    private static bool NeedsCopy(FileInfo source, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return true;
        var dest = new FileInfo(destinationPath);
        if (source.Length != dest.Length) return true;
        return !FilesHaveSameHash(source.FullName, destinationPath);
    }

    private static bool FilesHaveSameHash(string path1, string path2)
    {
        try
        {
            using var md5 = MD5.Create();
            using var s1 = File.OpenRead(path1);
            using var s2 = File.OpenRead(path2);
            byte[] h1 = md5.ComputeHash(s1);
            byte[] h2 = md5.ComputeHash(s2);
            return h1.SequenceEqual(h2);
        }
        catch
        {
            return false;
        }
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
