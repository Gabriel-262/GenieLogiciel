using System.Text.Json;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

public class JobRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _fileLock = new();
    private readonly PathService _paths;
    private readonly List<JobEntry> _entries;
    private ILogger? _logger;

    public JobRepository(PathService paths)
    {
        _paths = paths;
        _entries = LoadFromDisk();
    }

    public void SetLogger(ILogger logger) => _logger = logger;

    public List<BackupJob> GetAllJobs()
    {
        lock (_fileLock) return _entries.Select(e => e.ToJob()).ToList();
    }

    public BackupJob? GetJobById(int id)
    {
        lock (_fileLock) return _entries.FirstOrDefault(e => e.Id == id)?.ToJob();
    }

    public bool IdExists(int id)
    {
        lock (_fileLock) return _entries.Any(e => e.Id == id);
    }

    public int Count
    {
        get { lock (_fileLock) return _entries.Count; }
    }

    public void AddJob(BackupJob job)
    {
        lock (_fileLock)
        {
            if (_entries.Any(e => e.Id == job.Id))
                throw new InvalidOperationException($"Job ID {job.Id} already exists.");

            _entries.Add(JobEntry.FromJob(job));
            SaveToDisk();
        }
    }

    public bool UpdateJob(int id, BackupJob updated)
    {
        lock (_fileLock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return false;

            entry.Name = updated.Name;
            entry.SourcePath = updated.SourcePath;
            entry.TargetPath = updated.TargetPath;
            entry.Type = updated.Type;
            SaveToDisk();

            _logger?.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                BackupName = updated.Name,
                Action = LogAction.JobUpdated,
                SourceFilePath = updated.SourcePath,
                DestinationFilePath = updated.TargetPath
            });
            return true;
        }
    }

    public bool DeleteJob(int id)
    {
        lock (_fileLock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return false;
            _entries.Remove(entry);
            SaveToDisk();

            _logger?.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                BackupName = entry.Name,
                Action = LogAction.JobDeleted,
                SourceFilePath = entry.SourcePath,
                DestinationFilePath = entry.TargetPath
            });
            return true;
        }
    }

    public void UpdateState(int jobId, Action<JobEntry> mutate)
    {
        lock (_fileLock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == jobId);
            if (entry is null) return;
            mutate(entry);
            SaveToDisk();
        }
    }

    public void ClearState(int jobId)
    {
        lock (_fileLock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == jobId);
            if (entry is null) return;
            entry.Status = JobStatus.Inactive;
            entry.LastActionTime = DateTime.Now;
            entry.ProgressPercent = 0;
            entry.RemainingFiles = 0;
            entry.RemainingSizeBytes = 0;
            entry.CurrentSourceFile = string.Empty;
            entry.CurrentDestinationFile = string.Empty;
            SaveToDisk();
        }
    }

    private List<JobEntry> LoadFromDisk()
    {
        string path = _paths.GetStateFilePath();
        if (!File.Exists(path)) return new List<JobEntry>();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<JobEntry>>(json) ?? new List<JobEntry>();
        }
        catch (JsonException)
        {
            return new List<JobEntry>();
        }
    }

    private void SaveToDisk()
    {
        File.WriteAllText(
            _paths.GetStateFilePath(),
            JsonSerializer.Serialize(_entries, JsonOptions));
    }
}
