using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class JobExecutionViewModel : ObservableObject, IDisposable
{
    private readonly BackupEngine _engine;

    // SynchronizationContext capturé à la construction = celui du thread UI
    // (puisque le ViewModel est créé sur ce thread). On l'utilise pour
    // marshaler tous les handlers d'événements vers le thread UI : sinon,
    // les modifications d'ObservableProperty depuis un thread de background
    // lèvent une InvalidOperationException ("le thread appelant ne peut pas
    // accéder à cet objet parce qu'un autre thread en est propriétaire").
    private readonly SynchronizationContext? _ui;

    [ObservableProperty] private string currentJobName = string.Empty;
    [ObservableProperty] private string currentSourceFile = string.Empty;
    [ObservableProperty] private string currentDestinationFile = string.Empty;
    [ObservableProperty] private int totalFiles;
    [ObservableProperty] private int processedFiles;
    [ObservableProperty] private long totalSizeBytes;
    [ObservableProperty] private long bytesDone;
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private bool isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool isPaused;

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

    // ------------------------------------------------------------------------
    // RunOnUi : exécute l'action sur le thread UI (capture si nécessaire).
    // Si on est déjà sur le thread UI ou pas de context capturé → exécution directe.
    // ------------------------------------------------------------------------
    private void RunOnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }

    private void OnJobStarted(object? sender, string jobName) => RunOnUi(() =>
    {
        CurrentJobName = jobName;
        IsRunning = true;
        IsPaused = false;
        ProgressPercent = 0;
        ProcessedFiles = 0;
        BytesDone = 0;
    });

    private void OnJobCompleted(object? sender, string jobName) => RunOnUi(() =>
    {
        IsRunning = false;
        IsPaused = false;
        ProgressPercent = 100;
    });

    private void OnProgressChanged(object? sender, BackupProgressEventArgs e) => RunOnUi(() =>
    {
        CurrentJobName = e.JobName;
        CurrentSourceFile = e.CurrentSourceFile;
        CurrentDestinationFile = e.CurrentDestinationFile;
        TotalFiles = e.TotalFiles;
        ProcessedFiles = e.ProcessedFiles;
        TotalSizeBytes = e.TotalSizeBytes;
        BytesDone = e.BytesDone;
        ProgressPercent = e.ProgressPercent;
    });

    private void OnJobPaused(object? sender, string jobName) => RunOnUi(() =>
    {
        CurrentJobName = jobName;
        IsPaused = true;
    });

    private void OnJobResumed(object? sender, string jobName) => RunOnUi(() =>
    {
        CurrentJobName = jobName;
        IsPaused = false;
    });

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        _engine.Resume();
    }

    private bool CanResume() => IsPaused;

    public void Dispose()
    {
        _engine.JobStarted -= OnJobStarted;
        _engine.JobCompleted -= OnJobCompleted;
        _engine.ProgressChanged -= OnProgressChanged;
        _engine.JobPaused -= OnJobPaused;
        _engine.JobResumed -= OnJobResumed;
    }
}
