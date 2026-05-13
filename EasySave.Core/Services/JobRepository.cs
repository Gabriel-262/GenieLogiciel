using System.Text.Json;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

public class JobRepository : IJobRepository, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Throttle des écritures provoquées par UpdateState (appelé par fichier
    // copié). Sans throttle, un job de 50k fichiers = 50k réécritures
    // complètes du state.json => I/O et JSON serialization énormes.
    // 200 ms = compromis réactivité UI / charge disque.
    private const int ThrottleMs = 200;

    private readonly object _fileLock = new();
    private readonly PathService _paths;
    private readonly SettingsService _settings;
    private readonly List<JobEntry> _entries;
    private int _nextId;
    private ILogger? _logger;

    // État du throttle. Protégé par _fileLock.
    private long _lastSaveTicks;
    private bool _savePending;
    private readonly Timer _saveTimer;

    private class StateFile
    {
        public int NextId { get; set; } = 1;
        public List<JobEntry> Entries { get; set; } = new();
    }

    public JobRepository(PathService paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
        var state = LoadFromDisk();
        _entries = state.Entries;
        _nextId = state.NextId;
        _saveTimer = new Timer(_ => FlushPending(), null, Timeout.Infinite, Timeout.Infinite);
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
            int max = _settings.MaxJobs;
            if (_entries.Count >= max)
                throw new InvalidOperationException($"Maximum number of backup jobs ({max}) reached.");

            job.Id = _nextId++;
            _entries.Add(JobEntry.FromJob(job));
            SaveNow();
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
            SaveNow();

            _logger?.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = id,
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
            SaveNow();

            _logger?.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = entry.Id,
                BackupName = entry.Name,
                Action = LogAction.JobDeleted,
                SourceFilePath = entry.SourcePath,
                DestinationFilePath = entry.TargetPath
            });
            return true;
        }
    }

    // Versions async : in-process, on n'a pas de coût réseau donc on retourne
    // immédiatement le résultat sync. Présentes pour respecter l'interface
    // et permettre aux call sites de toujours utiliser la forme async.
    public Task<BackupJob> AddJobAsync(BackupJob job, CancellationToken ct = default)
        => Task.FromResult(AddJob(job));
    public Task<bool> UpdateJobAsync(int id, BackupJob updated, CancellationToken ct = default)
        => Task.FromResult(UpdateJob(id, updated));
    public Task<bool> DeleteJobAsync(int id, CancellationToken ct = default)
        => Task.FromResult(DeleteJob(id));

    public void UpdateState(int jobId, Action<JobEntry> mutate)
    {
        lock (_fileLock)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == jobId);
            if (entry is null) return;
            mutate(entry);
            ScheduleSave();
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
            entry.ActiveThreads = 0;
            // Fin de job = flush immédiat pour ne pas laisser un état partiel
            // dans le state.json si le process meurt juste après.
            SaveNow();
        }
    }

    // === Throttle / persistence ===

    // À appeler sous _fileLock. Schedule une écriture dans <= ThrottleMs.
    private void ScheduleSave()
    {
        long now = Environment.TickCount64;
        long elapsed = now - _lastSaveTicks;
        if (elapsed >= ThrottleMs)
        {
            SaveNow();
            return;
        }
        _savePending = true;
        int delay = (int)(ThrottleMs - elapsed);
        _saveTimer.Change(delay, Timeout.Infinite);
    }

    // Callback du timer : prend le lock, écrit si pending. Si le timer arrive
    // entre-temps après un SaveNow, _savePending sera false donc no-op.
    private void FlushPending()
    {
        lock (_fileLock)
        {
            if (!_savePending) return;
            SaveNow();
        }
    }

    // À appeler sous _fileLock. Écriture atomique : .tmp puis Replace.
    private void SaveNow()
    {
        var state = new StateFile { NextId = _nextId, Entries = _entries };
        string targetPath = _paths.GetStateFilePath();
        string tmpPath = targetPath + ".tmp";

        File.WriteAllText(tmpPath, JsonSerializer.Serialize(state, JsonOptions));

        if (File.Exists(targetPath))
        {
            // File.Replace = rename atomique côté NTFS, conserve les ACL.
            File.Replace(tmpPath, targetPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tmpPath, targetPath);
        }

        _lastSaveTicks = Environment.TickCount64;
        _savePending = false;
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        FlushPending();
        _saveTimer.Dispose();
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
}
