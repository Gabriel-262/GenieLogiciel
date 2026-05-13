using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

// Facade publique du moteur de sauvegarde. Compose les trois classes
// extraites (séparation des responsabilités) :
//   - JobTelemetryPublisher : events de cycle de vie + logs + progression.
//   - JobRunner             : exécution d'un job (scan + copie parallèle).
//   - JobOrchestrator       : parallélisme multi-jobs + policies cross-job
//                             (business software, pause/resume/stop globaux).
//
// La signature publique (constructeur, events, méthodes Pause/Resume/Stop,
// ExecuteJob[s]Async) est inchangée pour ne rien casser côté call-sites
// (Server/Program.cs, ViewModels, RemoteBackupEngine miroir).
public class BackupEngine : IBackupEngine
{
    private readonly JobTelemetryPublisher _telemetry;
    private readonly JobOrchestrator _orchestrator;

    public BackupEngine(JobRepository repo, ILogger logger,
        IBusinessSoftwareMonitor? businessMonitor = null,
        ICryptoSoft? crypto = null,
        SettingsService? settings = null)
    {
        _telemetry = new JobTelemetryPublisher(logger, repo);

        var copier = new CopyService(settings?.Current.MaxParallelLargeFiles ?? 2);
        var runner = new JobRunner(
            _telemetry,
            new FileScanner(),
            new PriorityGate(),
            copier,
            crypto,
            settings);

        _orchestrator = new JobOrchestrator(runner, _telemetry, repo, settings, businessMonitor);
    }

    // === Events : forwarding pur vers le publisher ===

    public event EventHandler<BackupProgressEventArgs>? ProgressChanged
    { add => _telemetry.ProgressChanged += value; remove => _telemetry.ProgressChanged -= value; }
    public event EventHandler<JobLifecycleEventArgs>? JobStarted
    { add => _telemetry.JobStarted += value; remove => _telemetry.JobStarted -= value; }
    public event EventHandler<JobLifecycleEventArgs>? JobCompleted
    { add => _telemetry.JobCompleted += value; remove => _telemetry.JobCompleted -= value; }
    public event EventHandler<JobLifecycleEventArgs>? JobStopped
    { add => _telemetry.JobStopped += value; remove => _telemetry.JobStopped -= value; }
    public event EventHandler<JobLifecycleEventArgs>? JobPaused
    { add => _telemetry.JobPaused += value; remove => _telemetry.JobPaused -= value; }
    public event EventHandler<JobLifecycleEventArgs>? JobResumed
    { add => _telemetry.JobResumed += value; remove => _telemetry.JobResumed -= value; }

    // === Délégation à l'orchestrateur ===

    public bool IsJobPaused(int jobId) => _orchestrator.IsJobPaused(jobId);
    public IReadOnlyCollection<int> ActiveJobIds => _orchestrator.ActiveJobIds;

    public void Pause(int jobId)  => _orchestrator.Pause(jobId);
    public void Resume(int jobId) => _orchestrator.Resume(jobId);
    public void Stop(int jobId)   => _orchestrator.Stop(jobId);

    public void PauseAll()  => _orchestrator.PauseAll();
    public void ResumeAll() => _orchestrator.ResumeAll();
    public void StopAll()   => _orchestrator.StopAll();

    public void PauseAllForBusinessSoftware()  => _orchestrator.PauseAllForBusinessSoftware();
    public void ResumeAllAfterBusinessSoftware() => _orchestrator.ResumeAllAfterBusinessSoftware();

    public Task ExecuteJobAsync(BackupJob job, CancellationToken ct = default)
        => _orchestrator.ExecuteJobAsync(job, ct);

    public Task ExecuteJobsAsync(IEnumerable<int> jobIds, CancellationToken ct = default)
        => _orchestrator.ExecuteJobsAsync(jobIds, ct);
}
