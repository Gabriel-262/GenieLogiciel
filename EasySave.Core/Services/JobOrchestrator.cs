using System.Collections.Concurrent;
using EasySave.Models;

namespace EasySave.Services;

// Orchestre l'exécution multi-jobs : limite globale (MaxParallelJobs),
// table des controllers actifs (clé = jobId), policies cross-job (mise en
// pause de tous les jobs quand un logiciel métier démarre, stop global, etc).
//
// Annulation : le CancellationToken passé à ExecuteJob[s]Async est lié au
// JobController du job (RequestStop dès que le token est annulé). Du coup
// le même bouton "Stop" et un `cts.Cancel()` ont strictement le même effet.
public sealed class JobOrchestrator
{
    private readonly JobRunner _runner;
    private readonly JobTelemetryPublisher _telemetry;
    private readonly IJobRepository _repo;
    private readonly ISettingsProvider? _settings;
    private readonly IBusinessSoftwareMonitor? _businessMonitor;

    private readonly ConcurrentDictionary<int, JobController> _controllers = new();

    public JobOrchestrator(
        JobRunner runner,
        JobTelemetryPublisher telemetry,
        IJobRepository repo,
        ISettingsProvider? settings,
        IBusinessSoftwareMonitor? businessMonitor)
    {
        _runner = runner;
        _telemetry = telemetry;
        _repo = repo;
        _settings = settings;
        _businessMonitor = businessMonitor;
    }

    public bool IsJobPaused(int jobId)
        => _controllers.TryGetValue(jobId, out var c) && c.IsPaused;

    public IReadOnlyCollection<int> ActiveJobIds => _controllers.Keys.ToArray();

    // === Contrôle (par job + global) ===

    public void Pause(int jobId)  => AddReason(jobId, PauseReason.User);
    public void Resume(int jobId) => RemoveReason(jobId, PauseReason.User);
    public void Stop(int jobId)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.RequestStop();
    }

    public void PauseAll()  { foreach (var id in _controllers.Keys) AddReason(id, PauseReason.User); }
    public void ResumeAll() { foreach (var id in _controllers.Keys) RemoveReason(id, PauseReason.User); }
    public void StopAll()   { foreach (var c in _controllers.Values) c.RequestStop(); }

    public void PauseAllForBusinessSoftware()
    { foreach (var id in _controllers.Keys) AddReason(id, PauseReason.Business); }
    public void ResumeAllAfterBusinessSoftware()
    { foreach (var id in _controllers.Keys) RemoveReason(id, PauseReason.Business); }

    private void AddReason(int jobId, PauseReason reason)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.AddReason(reason);
    }
    private void RemoveReason(int jobId, PauseReason reason)
    {
        if (_controllers.TryGetValue(jobId, out var c)) c.RemoveReason(reason);
    }

    // === Exécution ===

    public Task<JobExecutionResult> ExecuteJobAsync(BackupJob job, CancellationToken ct = default)
        => Task.Run(() => ExecuteOne(job, ct), ct);

    public Task ExecuteJobsAsync(IEnumerable<int> jobIds, CancellationToken ct = default)
    {
        var ids = jobIds.ToList();
        int max = Math.Max(1, _settings?.Current.MaxParallelJobs ?? 4);
        var sem = new SemaphoreSlim(max);

        var tasks = ids.Select(async id =>
        {
            await sem.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (ct.IsCancellationRequested) return;
                var job = _repo.GetJobById(id);
                if (job is null) return;
                await Task.Run(() => ExecuteOne(job, ct), ct).ConfigureAwait(false);
            }
            finally { sem.Release(); }
        });

        return Task.WhenAll(tasks);
    }

    private JobExecutionResult ExecuteOne(BackupJob job, CancellationToken ct)
    {
        var controller = new JobController();
        if (!_controllers.TryAdd(job.Id, controller))
        {
            controller.Dispose();
            return JobExecutionResult.Rejected;
        }

        // Annulation unifiée : un seul Cancel sur le ct externe = un seul
        // RequestStop côté controller. Le runner ne voit que le controller.
        using var ctReg = ct.Register(controller.RequestStop);

        controller.Paused  += r => _telemetry.RaisePaused(job, r);
        controller.Resumed += r => _telemetry.RaiseResumed(job, r);

        if (_businessMonitor is not null && _businessMonitor.IsRunning())
        {
            controller.AddReason(PauseReason.Business);
            _telemetry.LogBusinessSoftwareDetectedAtStart(job);
        }

        JobExecutionResult result;
        try
        {
            result = _runner.Run(job, controller);
        }
        finally
        {
            _telemetry.ClearState(job.Id);
            if (_controllers.TryRemove(job.Id, out var c)) c.Dispose();
        }

        // Pas d'évènement Finished sur Rejected : le runner n'a pas non plus
        // levé Started (rejet avant tout travail), on garde la symétrie.
        if (result.Status != JobExecutionStatus.Rejected)
            _telemetry.RaiseFinished(job, result);
        return result;
    }
}
