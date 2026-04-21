using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public JobListViewModel JobList { get; }
    public JobFormViewModel JobForm { get; }
    public SettingsViewModel Settings { get; }
    public JobExecutionViewModel Execution { get; }

    public MainViewModel(
        JobRepository repo,
        BackupEngine engine,
        SettingsService settings)
    {
        JobList = new JobListViewModel(repo, engine);
        JobForm = new JobFormViewModel(repo);
        Settings = new SettingsViewModel(settings);
        Execution = new JobExecutionViewModel(engine);
    }
}
