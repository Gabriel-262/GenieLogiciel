using EasySave.Models;
using EasySave.Protocol;
using EasySave.Services;

namespace EasySave.Client;

// IJobRepository côté client : lit la liste de jobs depuis un cache local
// alimenté par le snapshot initial + l'event evt.jobs.changed. Les écritures
// (Add/Update/Delete) font un aller-retour réseau ; les VMs DOIVENT utiliser
// les versions async pour ne pas bloquer le thread UI.
//
// Les versions sync sont conservées pour la CLI (qui tourne hors thread UI),
// mais elles utilisent quand même le bridge sync-over-async — à éviter dans
// du code GUI.
public sealed class RemoteJobRepository : IJobRepository
{
    private readonly BackupServerConnection _conn;
    private readonly object _lock = new();
    private List<BackupJob> _cache = new();

    public RemoteJobRepository(BackupServerConnection conn)
    {
        _conn = conn;
        _conn.EventReceived += OnEventReceived;
    }

    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }

    public List<BackupJob> GetAllJobs()
    {
        lock (_lock) return _cache.Select(Clone).ToList();
    }

    public BackupJob? GetJobById(int id)
    {
        lock (_lock) return _cache.FirstOrDefault(j => j.Id == id) is { } j ? Clone(j) : null;
    }

    public BackupJob? GetJobByIndex(int index1Based)
    {
        lock (_lock)
        {
            if (index1Based < 1 || index1Based > _cache.Count) return null;
            return Clone(_cache[index1Based - 1]);
        }
    }

    // ====== Async (à privilégier depuis le code UI) ======

    public async Task<BackupJob> AddJobAsync(BackupJob job, CancellationToken ct = default)
    {
        var rsp = await _conn.SendCommandAsync(
            MessageTypes.CmdAddJob, new AddJobPayload { Job = job.ToDto() }, ct).ConfigureAwait(false);
        var payload = rsp.TryDecode<JobPayload>()
            ?? throw new InvalidOperationException("Réponse AddJob invalide.");
        var added = payload.Job.FromDto();
        // L'event evt.jobs.changed peut déjà avoir rafraîchi le cache (course
        // entre la réponse et le broadcast serveur) : on évite le doublon.
        lock (_lock)
        {
            if (!_cache.Any(j => j.Id == added.Id)) _cache.Add(Clone(added));
        }
        return added;
    }

    public async Task<bool> UpdateJobAsync(int id, BackupJob updated, CancellationToken ct = default)
    {
        var rsp = await _conn.SendCommandAsync(
            MessageTypes.CmdUpdateJob, new UpdateJobPayload { Id = id, Job = updated.ToDto() }, ct).ConfigureAwait(false);
        if (rsp.Type == MessageTypes.RspError) return false;
        lock (_lock)
        {
            var existing = _cache.FirstOrDefault(j => j.Id == id);
            if (existing is not null)
            {
                existing.Name = updated.Name;
                existing.SourcePath = updated.SourcePath;
                existing.TargetPath = updated.TargetPath;
                existing.Type = updated.Type;
            }
        }
        return true;
    }

    public async Task<bool> DeleteJobAsync(int id, CancellationToken ct = default)
    {
        var rsp = await _conn.SendCommandAsync(
            MessageTypes.CmdDeleteJob, new JobIdPayload { JobId = id }, ct).ConfigureAwait(false);
        if (rsp.Type == MessageTypes.RspError) return false;
        lock (_lock) _cache.RemoveAll(j => j.Id == id);
        return true;
    }

    // ====== Sync (CLI uniquement — bloque le thread courant) ======

    public BackupJob AddJob(BackupJob job)        => AddJobAsync(job).GetAwaiter().GetResult();
    public bool UpdateJob(int id, BackupJob job)  => UpdateJobAsync(id, job).GetAwaiter().GetResult();
    public bool DeleteJob(int id)                 => DeleteJobAsync(id).GetAwaiter().GetResult();

    private void OnEventReceived(object? sender, Envelope env)
    {
        if (env.Type != MessageTypes.EvtJobsChanged) return;
        if (env.TryDecode<JobsPayload>() is not { } payload) return;
        lock (_lock)
            _cache = payload.Jobs.Select(d => d.FromDto()).ToList();
    }

    public void ApplySnapshot(SnapshotPayload snapshot)
    {
        lock (_lock)
            _cache = snapshot.Jobs.Select(d => d.FromDto()).ToList();
    }

    private static BackupJob Clone(BackupJob j) => new()
    {
        Id = j.Id, Name = j.Name, SourcePath = j.SourcePath, TargetPath = j.TargetPath, Type = j.Type
    };
}
