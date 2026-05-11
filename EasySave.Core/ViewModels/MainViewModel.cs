using System.Collections.Specialized;
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

        // Bind each JobItem to its live progress, so per-job cards show
        // a progress bar + pause/resume/stop without a separate tab.
        Execution.Jobs.CollectionChanged += OnExecutionJobsChanged;
        JobList.Jobs.CollectionChanged += (_, __) => SyncProgressToList();
    }

    public void MarkExecutionViewed()
    {
        UnseenExecutionCount = 0;
    }

    private void OnExecutionJobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (JobProgressItemViewModel p in e.NewItems)
                AttachProgress(p);

        if (e.OldItems is not null)
            foreach (JobProgressItemViewModel p in e.OldItems)
                DetachProgress(p);
    }

    private void AttachProgress(JobProgressItemViewModel p)
    {
        var item = JobList.FindById(p.JobId);
        if (item is not null) item.Progress = p;
    }

    private void DetachProgress(JobProgressItemViewModel p)
    {
        var item = JobList.FindById(p.JobId);
        if (item is not null && ReferenceEquals(item.Progress, p))
            item.Progress = null;
    }

    private void SyncProgressToList()
    {
        // Called after JobList.Refresh: re-attach progress objects to newly
        // recreated JobItemViewModels.
        foreach (var item in JobList.Jobs)
            item.Progress = Execution.Jobs.FirstOrDefault(p => p.JobId == item.Id);
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
