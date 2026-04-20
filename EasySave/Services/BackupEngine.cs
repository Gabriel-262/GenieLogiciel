using System.Diagnostics;
using EasyLog;
using EasySave.Interfaces;
using EasySave.Models;

namespace EasySave.Services;

public class BackupEngine
{
    private readonly BackupJobService _jobService;
    private readonly IStateManager _stateService;
    private readonly ILogger _logger;

    public BackupEngine(BackupJobService jobService, IStateManager stateService, PathService paths)
    {
        _jobService = jobService;
        _stateService = stateService;
        _logger = new EasyLogger(paths.GetDailyLogFilePath);
    }

    public void ExecuteJobs(IEnumerable<int> jobIds)
    {
        foreach (int id in jobIds)
        {
            var job = _jobService.GetById(id);
            if (job is null) continue;
            ExecuteJob(job);
        }
    }

    public void ExecuteJob(BackupJob job)
    {
        if (!Directory.Exists(job.SourcePath)) return;
        Directory.CreateDirectory(job.TargetPath);

        var files = ScanDirectory(job.SourcePath);
        long totalSize = files.Sum(f => f.Length);
        int totalFiles = files.Count;

        var state = new StateEntry
        {
            JobName = job.Name,
            Status = JobStatus.Active,
            TotalFiles = totalFiles,
            TotalSizeBytes = totalSize,
            RemainingFiles = totalFiles,
            RemainingSizeBytes = totalSize,
            ProgressPercent = 0,
            LastActionTime = DateTime.Now
        };
        _stateService.UpdateState(state);

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
                UpdateProgress(state, sourceFile.FullName, destination, processed, totalFiles, bytesDone, totalSize);
                continue;
            }

            string? destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            long elapsedMs = CopyFile(sourceFile.FullName, destination);

            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                BackupName = job.Name,
                SourceFilePath = sourceFile.FullName,
                DestinationFilePath = destination,
                FileSizeBytes = sourceFile.Length,
                TransferTimeMs = elapsedMs
            });

            processed++;
            bytesDone += sourceFile.Length;
            UpdateProgress(state, sourceFile.FullName, destination, processed, totalFiles, bytesDone, totalSize);
        }

        _stateService.ClearState(job.Name);
    }

    private void UpdateProgress(StateEntry state, string sourceFile, string destinationFile,
        int processed, int totalFiles, long bytesDone, long totalBytes)
    {
        state.LastActionTime = DateTime.Now;
        state.CurrentSourceFile = sourceFile;
        state.CurrentDestinationFile = destinationFile;
        state.RemainingFiles = totalFiles - processed;
        state.RemainingSizeBytes = totalBytes - bytesDone;
        state.ProgressPercent = totalFiles == 0 ? 100 : (double)processed * 100 / totalFiles;
        _stateService.UpdateState(state);
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
        return source.Length != dest.Length || source.LastWriteTimeUtc > dest.LastWriteTimeUtc;
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
