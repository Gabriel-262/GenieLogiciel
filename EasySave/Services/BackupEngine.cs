using EasySave.Models;
using EasyLog;

namespace EasySave.Services;

public class BackupEngine
{
    private readonly BackupJobService _jobService;
    private readonly StateService _stateService;
    private readonly Logger _logger;

    public BackupEngine(BackupJobService jobService, StateService stateService, PathService pathService)
    {
        _jobService = jobService;
        _stateService = stateService;
        _logger = new Logger(pathService.GetLogsDirectory());
    }

    public void ExecuteJobs(IEnumerable<int> jobIds)
    {
        foreach (int id in jobIds)
        {
            var job = _jobService.GetAll().FirstOrDefault(j => j.Id == id);
            if (job != null)
                ExecuteJob(job);
        }
    }

    public void ExecuteJob(BackupJob job)
    {
        if (!Directory.Exists(job.SourcePath))
        {
            Console.Error.WriteLine($"Source directory not found: {job.SourcePath}");
            return;
        }

        Directory.CreateDirectory(job.TargetPath);
        var (totalFiles, totalSize) = ScanDirectory(job.SourcePath);
        int filesRemaining = totalFiles;
        long sizeRemaining = totalSize;

        _stateService.UpdateState(new StateEntry
        {
            Name = job.Name,
            Status = "Active",
            TotalFiles = totalFiles,
            TotalSize = totalSize,
            FilesRemaining = filesRemaining,
            SizeRemaining = sizeRemaining,
            Progress = 0
        });

        CopyDirectory(job.SourcePath, job.TargetPath, job, job.Type,
            ref filesRemaining, ref sizeRemaining, totalFiles, totalSize);

        _stateService.SetInactive(job.Name);
    }

    private void CopyDirectory(string sourceDir, string targetDir,
        BackupJob job, BackupType type,
        ref int filesRemaining, ref long sizeRemaining,
        int totalFiles, long totalSize)
    {
        Directory.CreateDirectory(targetDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            bool shouldCopy = type == BackupType.Full || ShouldCopyDifferential(file, destFile);
            var fileInfo = new FileInfo(file);

            if (shouldCopy)
            {
                long transferTime;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    File.Copy(file, destFile, overwrite: true);
                    watch.Stop();
                    transferTime = watch.ElapsedMilliseconds;
                }
                catch
                {
                    watch.Stop();
                    transferTime = -watch.ElapsedMilliseconds;
                }

                _logger.Log(new LogEntry
                {
                    BackupName = job.Name,
                    SourcePath = file,
                    DestinationPath = destFile,
                    FileSize = fileInfo.Length,
                    TransferTimeMs = transferTime
                });
            }

            filesRemaining--;
            sizeRemaining = Math.Max(0, sizeRemaining - fileInfo.Length);
            double progress = totalFiles > 0
                ? Math.Round((1.0 - (double)filesRemaining / totalFiles) * 100, 2)
                : 100;

            _stateService.UpdateState(new StateEntry
            {
                Name = job.Name,
                Status = "Active",
                TotalFiles = totalFiles,
                TotalSize = totalSize,
                FilesRemaining = filesRemaining,
                SizeRemaining = sizeRemaining,
                Progress = progress,
                CurrentSourceFile = file,
                CurrentDestFile = destFile
            });
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir, job, type,
                ref filesRemaining, ref sizeRemaining, totalFiles, totalSize);
        }
    }

    private static bool ShouldCopyDifferential(string sourceFile, string destFile)
    {
        if (!File.Exists(destFile)) return true;
        var src = new FileInfo(sourceFile);
        var dst = new FileInfo(destFile);
        return src.LastWriteTimeUtc != dst.LastWriteTimeUtc || src.Length != dst.Length;
    }

    private static (int files, long size) ScanDirectory(string directory)
    {
        int count = 0;
        long size = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            count++;
            size += new FileInfo(file).Length;
        }
        return (count, size);
    }
}
