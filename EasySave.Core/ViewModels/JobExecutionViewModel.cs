using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class JobExecutionViewModel : ObservableObject, IDisposable
{
    private readonly BackupEngine _engine;

    // SynchronizationContext capturé à la construction = thread UI. Utilisé
    // pour marshaler tous les handlers d'events vers l'UI : sans ça, modifier
    // une ObservableCollection ou une ObservableProperty depuis un thread de
    // background lèverait InvalidOperationException.
    private readonly SynchronizationContext? _ui;

    // Une ligne par job en cours d'exécution (ajoutée à JobStarted, retirée à
    // JobCompleted). Permet d'afficher la progression de N jobs en parallèle.
    public ObservableCollection<JobProgressItemViewModel> Jobs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyRunning))]
    [NotifyPropertyChangedFor(nameof(IsAnyPaused))]
    private int dummyCounter; // usé seulement pour forcer la propagation aux propriétés calculées

    public bool IsAnyRunning => Jobs.Any(j => j.IsRunning);
    public bool IsAnyPaused => Jobs.Any(j => j.IsPaused);

    public JobExecutionViewModel(BackupEngine engine)
    {
        _engine = engine;
        _ui = SynchronizationContext.Current;

        _engine.JobStarted += OnJobStarted;
        _engine.JobCompleted += OnJobCompleted;
        _engine.ProgressChanged += OnProgressChanged;
        _engine.JobPaused += OnJobPaused;
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
            item = new JobProgressItemViewModel(jobId, jobName, _engine.Resume);
            Jobs.Add(item);
        }
        return item;
    }

    private void NotifyAggregates()
    {
        OnPropertyChanged(nameof(IsAnyRunning));
        OnPropertyChanged(nameof(IsAnyPaused));
    }

    private void OnJobStarted(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        var item = FindOrAdd(e.JobId, e.JobName, addIfMissing: true)!;
        item.JobName = e.JobName;
        item.IsRunning = true;
        item.IsPaused = false;
        item.ProgressPercent = 0;
        item.ProcessedFiles = 0;
        item.BytesDone = 0;
        NotifyAggregates();
    });

    private void OnJobCompleted(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        var item = FindOrAdd(e.JobId, e.JobName, addIfMissing: false);
        if (item is not null)
        {
            item.IsRunning = false;
            item.IsPaused = false;
            item.ProgressPercent = 100;
            Jobs.Remove(item);
        }
        NotifyAggregates();
    });

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
        NotifyAggregates();
    });

    private void OnJobResumed(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        var item = FindOrAdd(e.JobId, e.JobName, addIfMissing: false);
        if (item is not null) item.IsPaused = false;
        NotifyAggregates();
    });

    public void Dispose()
    {
        _engine.JobStarted -= OnJobStarted;
        _engine.JobCompleted -= OnJobCompleted;
        _engine.ProgressChanged -= OnProgressChanged;
        _engine.JobPaused -= OnJobPaused;
        _engine.JobResumed -= OnJobResumed;
    }
}
