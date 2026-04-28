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
    private int _nextId;
    private ILogger? _logger;

    private class StateFile
    {
        public int NextId { get; set; } = 1;
        public List<JobEntry> Entries { get; set; } = new();
    }

    public JobRepository(PathService paths)
    {
        _paths = paths;
        var state = LoadFromDisk();
        _entries = state.Entries;
        _nextId = state.NextId;
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

    public BackupJob? GetJobByIndex(int index1Based)
    {
        lock (_fileLock)
        {
            if (index1Based < 1 || index1Based > _entries.Count) return null;
            return _entries[index1Based - 1].ToJob();
        }
    }

    public int Count
    {
        get { lock (_fileLock) return _entries.Count; }
    }

    public BackupJob AddJob(BackupJob job)
    {
        lock (_fileLock)
        {
            if (_entries.Count >= AppConfig.MaxJobs)
                throw new InvalidOperationException($"Maximum number of backup jobs ({AppConfig.MaxJobs}) reached.");

            job.Id = _nextId++;
            _entries.Add(JobEntry.FromJob(job));
            SaveToDisk();
            return job;
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

    private StateFile LoadFromDisk()
    {
        string path = _paths.GetStateFilePath();
        if (!File.Exists(path)) return new StateFile();

        try
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var entries = JsonSerializer.Deserialize<List<JobEntry>>(json) ?? new List<JobEntry>();
                int next = entries.Count == 0 ? 1 : entries.Max(e => e.Id) + 1;
                return new StateFile { NextId = next, Entries = entries };
            }

            var state = JsonSerializer.Deserialize<StateFile>(json) ?? new StateFile();
            if (state.NextId < 1)
                state.NextId = state.Entries.Count == 0 ? 1 : state.Entries.Max(e => e.Id) + 1;
            return state;
        }
        catch (JsonException)
        {
            return new StateFile();
        }
    }

    private void SaveToDisk()
    {
        var state = new StateFile { NextId = _nextId, Entries = _entries };
        File.WriteAllText(
            _paths.GetStateFilePath(),
            JsonSerializer.Serialize(state, JsonOptions));
    }
}
