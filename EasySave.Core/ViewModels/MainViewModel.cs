using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SynchronizationContext? _ui;

    public JobListViewModel JobList { get; }
    public JobFormViewModel JobForm { get; }
    public SettingsViewModel Settings { get; }
    public JobExecutionViewModel Execution { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnyJobRunning))]
    private int runningCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExecutionBadgeVisible))]
    private int unseenExecutionCount;

    public bool IsAnyJobRunning => RunningCount > 0;
    public bool IsExecutionBadgeVisible => UnseenExecutionCount > 0;

    public MainViewModel(
        IJobRepository repo,
        IBackupEngine engine,
        SettingsService settings)
    {
        _ui = SynchronizationContext.Current;

        JobList = new JobListViewModel(repo, engine);
        JobForm = new JobFormViewModel(repo);
        Settings = new SettingsViewModel(settings);
        Execution = new JobExecutionViewModel(engine);

        engine.JobStarted += OnJobStarted;
        engine.JobCompleted += OnJobCompleted;
    }

    public void MarkExecutionViewed()
    {
        UnseenExecutionCount = 0;
    }

    private void OnJobStarted(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        RunningCount++;
        UnseenExecutionCount++;
    });

    private void OnJobCompleted(object? sender, JobLifecycleEventArgs e) => RunOnUi(() =>
    {
        if (RunningCount > 0) RunningCount--;
    });

    private void RunOnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }
}
