using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isRunning = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    private bool isPaused;

    // Cause courante de la pause ; null = pas en pause.
    [ObservableProperty] private PauseReason? pauseReason;

    // Vrai uniquement pour les pauses "subies" (logiciel métier ou fichier
    // verrouillé). Pour ces pauses on affiche le bandeau orange (erreur).
    public bool IsPausedForError =>
        IsPaused && PauseReason is Services.PauseReason.Business or Services.PauseReason.FileLocked;

    // Vrai pour la pause manuelle utilisateur. On affiche un indicateur
    // discret (pas un bandeau d'erreur) pour donner un retour visuel clair.
    public bool IsPausedByUser =>
        IsPaused && PauseReason == Services.PauseReason.User;

    public string ErrorMessage => PauseReason switch
    {
        Services.PauseReason.Business   => "Logiciel métier détecté — la sauvegarde reprendra automatiquement à sa fermeture.",
        Services.PauseReason.FileLocked => "Fichier verrouillé par une autre application — reprise automatique dès libération.",
        _ => string.Empty
    };

    partial void OnPauseReasonChanged(PauseReason? value)
    {
        OnPropertyChanged(nameof(IsPausedForError));
        OnPropertyChanged(nameof(IsPausedByUser));
        OnPropertyChanged(nameof(ErrorMessage));
    }
    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPausedForError));
        OnPropertyChanged(nameof(IsPausedByUser));
    }

    private readonly Action<int> _pauseAction;
    private readonly Action<int> _resumeAction;
    private readonly Action<int> _stopAction;

    public JobProgressItemViewModel(int jobId, string jobName,
        Action<int> pauseAction, Action<int> resumeAction, Action<int> stopAction)
    {
        JobId = jobId;
        this.jobName = jobName;
        _pauseAction = pauseAction;
        _resumeAction = resumeAction;
        _stopAction = stopAction;
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => _pauseAction(JobId);
    private bool CanPause() => IsRunning && !IsPaused;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _resumeAction(JobId);
    private bool CanResume() => IsPaused;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _stopAction(JobId);
    private bool CanStop() => IsRunning;
}
