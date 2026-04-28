using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class JobExecutionViewModel : ObservableObject, IDisposable
{
    private readonly BackupEngine _engine;

    [ObservableProperty] private string currentJobName = string.Empty;
    [ObservableProperty] private string currentSourceFile = string.Empty;
    [ObservableProperty] private string currentDestinationFile = string.Empty;
    [ObservableProperty] private int totalFiles;
    [ObservableProperty] private int processedFiles;
    [ObservableProperty] private long totalSizeBytes;
    [ObservableProperty] private long bytesDone;
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private bool isRunning;

    public JobExecutionViewModel(BackupEngine engine)
    {
        _engine = engine;
        _engine.JobStarted += OnJobStarted;
        _engine.JobCompleted += OnJobCompleted;
        _engine.ProgressChanged += OnProgressChanged;
    }

    private void OnJobStarted(object? sender, string jobName)
    {
        CurrentJobName = jobName;
        IsRunning = true;
        ProgressPercent = 0;
        ProcessedFiles = 0;
        BytesDone = 0;
    }

    private void OnJobCompleted(object? sender, string jobName)
    {
        IsRunning = false;
        ProgressPercent = 100;
    }

    private void OnProgressChanged(object? sender, BackupProgressEventArgs e)
    {
        CurrentJobName = e.JobName;
        CurrentSourceFile = e.CurrentSourceFile;
        CurrentDestinationFile = e.CurrentDestinationFile;
        TotalFiles = e.TotalFiles;
        ProcessedFiles = e.ProcessedFiles;
        TotalSizeBytes = e.TotalSizeBytes;
        BytesDone = e.BytesDone;
        ProgressPercent = e.ProgressPercent;
    }

    public void Dispose()
    {
        _engine.JobStarted -= OnJobStarted;
        _engine.JobCompleted -= OnJobCompleted;
        _engine.ProgressChanged -= OnProgressChanged;
    }
}
