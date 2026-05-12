using EasySave.Models;

namespace EasySave.Services;

// Surface du moteur de sauvegarde vue par les ViewModels.
// Implémentations :
//   - BackupEngine        : exécution locale (côté serveur).
//   - RemoteBackupEngine  : commandes envoyées au serveur, events repoussés
//                           depuis les messages broadcast.
//
// La signature des events est strictement identique à BackupEngine d'origine
// (EventHandler<BackupProgressEventArgs>, EventHandler<JobLifecycleEventArgs>)
// pour que les VMs ne distinguent pas le mode local du mode distant.
public interface IBackupEngine
{
    event EventHandler<BackupProgressEventArgs>? ProgressChanged;
    event EventHandler<JobLifecycleEventArgs>? JobStarted;
    event EventHandler<JobLifecycleEventArgs>? JobCompleted;
    event EventHandler<JobLifecycleEventArgs>? JobStopped;
    event EventHandler<JobLifecycleEventArgs>? JobPaused;
    event EventHandler<JobLifecycleEventArgs>? JobResumed;

    bool IsJobPaused(int jobId);
    IReadOnlyCollection<int> ActiveJobIds { get; }

    void Pause(int jobId);
    void Resume(int jobId);
    void Stop(int jobId);
    void PauseAll();
    void ResumeAll();
    void StopAll();

    Task ExecuteJobAsync(BackupJob job, CancellationToken ct = default);
    Task ExecuteJobsAsync(IEnumerable<int> jobIds, CancellationToken ct = default);
}
