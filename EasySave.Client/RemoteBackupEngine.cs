using EasySave.Models;
using EasySave.Protocol;
using EasySave.Services;

namespace EasySave.Client;

// IBackupEngine côté client : forwarde les commandes via TCP, lève les events
// quand le serveur broadcast les siens.
//
// Les events sont levés sur le thread du read loop de la connexion. Les VMs
// font déjà leur propre marshalling vers le thread UI (RunOnUi → SynchronizationContext),
// donc rien à faire de spécial ici.
public sealed class RemoteBackupEngine : IBackupEngine
{
    private readonly BackupServerConnection _conn;
    private readonly HashSet<int> _activeJobIds = new();
    private readonly object _activeLock = new();

    public RemoteBackupEngine(BackupServerConnection conn)
    {
        _conn = conn;
        _conn.EventReceived += OnEventReceived;
    }

    public event EventHandler<BackupProgressEventArgs>? ProgressChanged;
    public event EventHandler<JobLifecycleEventArgs>? JobStarted;
    public event EventHandler<JobLifecycleEventArgs>? JobCompleted;
    public event EventHandler<JobLifecycleEventArgs>? JobStopped;
    public event EventHandler<JobLifecycleEventArgs>? JobPaused;
    public event EventHandler<JobLifecycleEventArgs>? JobResumed;

    public bool IsJobPaused(int jobId)
    {
        // Approximation : on ne suit pas l'état Paused localement (besoin
        // détaillé pas encore exigé par les VMs). À enrichir si nécessaire
        // en cachant la dernière transition Paused/Resumed par jobId.
        return false;
    }

    public IReadOnlyCollection<int> ActiveJobIds
    {
        get { lock (_activeLock) return _activeJobIds.ToArray(); }
    }

    // === Commandes (fire-and-forget côté UI : on ne bloque pas le thread VM
    // sur l'aller-retour réseau pour des actions de contrôle) ===

    public void Pause(int jobId)  => Fire(MessageTypes.CmdPauseJob,  new JobIdPayload { JobId = jobId });
    public void Resume(int jobId) => Fire(MessageTypes.CmdResumeJob, new JobIdPayload { JobId = jobId });
    public void Stop(int jobId)   => Fire(MessageTypes.CmdStopJob,   new JobIdPayload { JobId = jobId });

    public void PauseAll()  => Fire(MessageTypes.CmdPauseAll, null);
    public void ResumeAll() => Fire(MessageTypes.CmdResumeAll, null);
    public void StopAll()   => Fire(MessageTypes.CmdStopAll, null);

    public Task ExecuteJobAsync(BackupJob job, CancellationToken ct = default)
        => ExecuteJobsAsync(new[] { job.Id }, ct);

    public async Task ExecuteJobsAsync(IEnumerable<int> jobIds, CancellationToken ct = default)
    {
        await _conn.SendCommandAsync(MessageTypes.CmdRunJobs,
            new RunJobsPayload { JobIds = jobIds.ToList() }, ct).ConfigureAwait(false);
        // On ne suit PAS la fin du run ici : le serveur a renvoyé un OK qui
        // signifie "commande acceptée". Les events JobStarted/Completed/Stopped
        // permettent au client d'observer le cycle de vie réel.
    }

    private void Fire(string type, object? payload)
    {
        // Best-effort. Si la connexion tombe, l'erreur sera observée via
        // BackupServerConnection.Disconnected ; pas besoin de la propager
        // depuis un handler de bouton.
        _ = _conn.SendCommandAsync(type, payload);
    }

    // === Réception des events broadcast ===

    private void OnEventReceived(object? sender, Envelope env)
    {
        switch (env.Type)
        {
            case MessageTypes.EvtProgress:
                if (env.TryDecode<BackupProgressDto>() is { } pd)
                    ProgressChanged?.Invoke(this, pd.ToEventArgs());
                break;

            case MessageTypes.EvtJobStarted:
                if (env.TryDecode<JobLifecycleDto>() is { } js)
                {
                    lock (_activeLock) _activeJobIds.Add(js.JobId);
                    JobStarted?.Invoke(this, js.ToEventArgs());
                }
                break;

            case MessageTypes.EvtJobCompleted:
                if (env.TryDecode<JobLifecycleDto>() is { } jc)
                {
                    lock (_activeLock) _activeJobIds.Remove(jc.JobId);
                    JobCompleted?.Invoke(this, jc.ToEventArgs());
                }
                break;

            case MessageTypes.EvtJobStopped:
                if (env.TryDecode<JobLifecycleDto>() is { } jx)
                {
                    lock (_activeLock) _activeJobIds.Remove(jx.JobId);
                    JobStopped?.Invoke(this, jx.ToEventArgs());
                }
                break;

            case MessageTypes.EvtJobPaused:
                if (env.TryDecode<JobLifecycleDto>() is { } jp)
                    JobPaused?.Invoke(this, jp.ToEventArgs());
                break;

            case MessageTypes.EvtJobResumed:
                if (env.TryDecode<JobLifecycleDto>() is { } jr)
                    JobResumed?.Invoke(this, jr.ToEventArgs());
                break;
        }
    }

    // À appeler après Connect : récupère l'état initial et amorce ActiveJobIds.
    public async Task ApplySnapshotAsync(SnapshotPayload snapshot)
    {
        lock (_activeLock)
        {
            _activeJobIds.Clear();
            foreach (var id in snapshot.ActiveJobIds) _activeJobIds.Add(id);
        }
        await Task.CompletedTask;
    }
}
