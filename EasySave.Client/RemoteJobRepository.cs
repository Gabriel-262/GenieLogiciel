using EasySave.Models;
using EasySave.Protocol;
using EasySave.Services;

namespace EasySave.Client;

// IJobRepository côté client : lit la liste de jobs depuis un cache local
// alimenté par le snapshot initial + l'event evt.jobs.changed. Les écritures
// (Add/Update/Delete) sont synchrones du point de vue VM mais font un
// aller-retour réseau bloquant (les VMs appellent ces méthodes en réponse
// à un clic, donc une latence LAN est acceptable).
//
// Les VMs ne reçoivent PAS d'event "JobsChanged" : elles appellent Refresh()
// après chaque CRUD. Pour reflet inter-clients, l'appli abonne un handler à
// BackupServerConnection.EventReceived et déclenche Refresh().
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

    public BackupJob AddJob(BackupJob job)
    {
        var rsp = SendSync(MessageTypes.CmdAddJob, new AddJobPayload { Job = job.ToDto() });
        var payload = rsp.TryDecode<JobPayload>()
            ?? throw new InvalidOperationException("Réponse AddJob invalide.");
        var added = payload.Job.FromDto();
        // Mise à jour locale immédiate ; l'event evt.jobs.changed la confirmera.
        lock (_lock) _cache.Add(Clone(added));
        return added;
    }

    public bool UpdateJob(int id, BackupJob updated)
    {
        var rsp = SendSync(MessageTypes.CmdUpdateJob, new UpdateJobPayload { Id = id, Job = updated.ToDto() });
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

    public bool DeleteJob(int id)
    {
        var rsp = SendSync(MessageTypes.CmdDeleteJob, new JobIdPayload { JobId = id });
        if (rsp.Type == MessageTypes.RspError) return false;
        lock (_lock) _cache.RemoveAll(j => j.Id == id);
        return true;
    }

    private Envelope SendSync(string type, object? payload)
    {
        // Les VMs sont synchrones dans leurs commandes CRUD. On bloque le
        // thread courant le temps de l'aller-retour. Pour la WPF, ces appels
        // viennent du thread UI lors d'un clic : la latence LAN est faible
        // (~quelques ms) donc acceptable. Si on voulait un vrai async, il
        // faudrait async-iser IJobRepository — gros changement aux VMs.
        return _conn.SendCommandAsync(type, payload).GetAwaiter().GetResult();
    }

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
