using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasySave.ViewModels;

// Représente la progression d'UN job en cours. Plusieurs instances coexistent
// dans JobExecutionViewModel.Jobs quand plusieurs jobs tournent en parallèle.
public partial class JobProgressItemViewModel : ObservableObject
{
    public int JobId { get; }

    [ObservableProperty] private string jobName = string.Empty;
    [ObservableProperty] private string currentSourceFile = string.Empty;
    [ObservableProperty] private string currentDestinationFile = string.Empty;
    [ObservableProperty] private int totalFiles;
    [ObservableProperty] private int processedFiles;
    [ObservableProperty] private long totalSizeBytes;
    [ObservableProperty] private long bytesDone;
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private bool isRunning = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool isPaused;

    private readonly Action<int> _resumeAction;

    public JobProgressItemViewModel(int jobId, string jobName, Action<int> resumeAction)
    {
        JobId = jobId;
        this.jobName = jobName;
        _resumeAction = resumeAction;
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _resumeAction(JobId);

    private bool CanResume() => IsPaused;
}
