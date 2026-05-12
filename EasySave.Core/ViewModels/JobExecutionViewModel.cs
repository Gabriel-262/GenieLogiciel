using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class JobExecutionViewModel : ObservableObject, IDisposable
{
    private readonly IBackupEngine _engine;
    private readonly SynchronizationContext? _ui;

    // Une ligne par job en cours. Mise à jour via les events de l'engine
    // (toujours marshalés vers le thread UI).
    public ObservableCollection<JobProgressItemViewModel> Jobs { get; } = new();

    public bool IsAnyRunning => Jobs.Any(j => j.IsRunning);
    public bool IsAnyPaused  => Jobs.Any(j => j.IsPaused);

    // Bandeau d'alerte affiché quand le logiciel métier est détecté pendant
    // qu'au moins un job tourne. La vue peut binder cette string et masquer
    // le bandeau quand vide.
    [ObservableProperty] private string businessSoftwareAlert = string.Empty;
    public bool HasBusinessSoftwareAlert => !string.IsNullOrEmpty(BusinessSoftwareAlert);

    partial void OnBusinessSoftwareAlertChanged(string value)
        => OnPropertyChanged(nameof(HasBusinessSoftwareAlert));

    public JobExecutionViewModel(IBackupEngine engine)
    {
        _engine = engine;
        _ui = SynchronizationContext.Current;

        _engine.JobStarted   += OnJobStarted;
        _engine.JobCompleted += OnJobCompleted;
        _engine.JobStopped   += OnJobStopped;
        _engine.ProgressChanged += OnProgressChanged;
        _engine.JobPaused  += OnJobPaused;
        _engine.JobResumed += OnJobResumed;
    }

    private void RunOnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }

    private JobProgressItemViewModel? FindOrAdd(int jobId, string jobName, bool addIfMissing)
    {
        var item = Jobs.FirstOrDefault(j => j.JobId == jobId);
        if (item is null && addIfMissing)
        {
            item = new JobProgressItemViewModel(jobId, jobName,
                pauseAction:  _engine.Pause,
                resumeAction: _engine.Resume,
                stopAction:   _engine.Stop);
            Jobs.Add(item);
        }
        return item;
    }

    private void NotifyAggregates()
    {
        OnPropertyChanged(nameof(IsAnyRunning));
        OnPropertyChanged(nameof(IsAnyPaused));
        PauseAllCommand.NotifyCanExecuteChanged();
        ResumeAllCommand.NotifyCanExecuteChanged();
        StopAllCommand.NotifyCanExecuteChanged();
    }

    // ===== Commandes globales =====

    [RelayCommand(CanExecute = nameof(CanPauseAll))]
    private void PauseAll() => _engine.PauseAll();
    private bool CanPauseAll() => Jobs.Any(j => j.IsRunning && !j.IsPaused);

    [RelayCommand(CanExecute = nameof(CanResumeAll))]
    private void ResumeAll() => _engine.ResumeAll();
    private bool CanResumeAll() => Jobs.Any(j => j.IsPaused);

    [RelayCommand(CanExecute = nameof(CanStopAll))]
    private void StopAll() => _engine.StopAll();
    private bool CanStopAll() => Jobs.Any(j => j.IsRunning);

    // ===== Handlers d'events engine =====

    private void OnJobStarted(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        var item = FindOrAdd(e.JobId, e.JobName, addIfMissing: true)!;
        item.JobName = e.JobName;
        item.IsRunning = true;
        item.IsPaused = false;
        item.PauseReason = null;
        item.ProgressPercent = 0;
        item.ProcessedFiles = 0;
        item.BytesDone = 0;
        NotifyAggregates();
    });

    private void OnJobCompleted(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
        RemoveJob(e.JobId, finalPercent: 100));

    private void OnJobStopped(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
        RemoveJob(e.JobId, finalPercent: null));

    private void RemoveJob(int jobId, double? finalPercent)
    {
        var item = Jobs.FirstOrDefault(j => j.JobId == jobId);
        if (item is null) return;
        item.IsRunning = false;
        item.IsPaused  = false;
        if (finalPercent.HasValue) item.ProgressPercent = finalPercent.Value;
        Jobs.Remove(item);
        NotifyAggregates();
    }

    private void OnProgressChanged(object? sender, BackupProgressEventArgs e) => RunOnUi(() =>
    {
        var item = FindOrAdd(e.JobId, e.JobName, addIfMissing: true)!;
        item.JobName = e.JobName;
        item.CurrentSourceFile = e.CurrentSourceFile;
        item.CurrentDestinationFile = e.CurrentDestinationFile;
        item.TotalFiles = e.TotalFiles;
        item.ProcessedFiles = e.ProcessedFiles;
        item.TotalSizeBytes = e.TotalSizeBytes;
        item.BytesDone = e.BytesDone;
        item.ProgressPercent = e.ProgressPercent;
    });

    private void OnJobPaused(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        var item = FindOrAdd(e.JobId, e.JobName, addIfMissing: true)!;
        item.IsPaused = true;
        item.PauseReason = e.Reason;

        if (e.Reason == PauseReason.Business)
            BusinessSoftwareAlert = "Logiciel métier détecté — toutes les sauvegardes sont en pause.";

        NotifyAggregates();
    });

    private void OnJobResumed(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        var item = FindOrAdd(e.JobId, e.JobName, addIfMissing: false);
        if (item is not null)
        {
            item.IsPaused = false;
            item.PauseReason = null;
        }

        if (e.Reason == PauseReason.Business && !Jobs.Any(j => j.PauseReason == PauseReason.Business))
            BusinessSoftwareAlert = string.Empty;

        NotifyAggregates();
    });

    public void Dispose()
    {
        _engine.JobStarted   -= OnJobStarted;
        _engine.JobCompleted -= OnJobCompleted;
        _engine.JobStopped   -= OnJobStopped;
        _engine.ProgressChanged -= OnProgressChanged;
        _engine.JobPaused  -= OnJobPaused;
        _engine.JobResumed -= OnJobResumed;
    }
}
