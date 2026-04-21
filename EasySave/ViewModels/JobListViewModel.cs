using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class JobListViewModel : ObservableObject
{
    private readonly BackupJobService _jobService;
    private readonly BackupEngine _engine;

    [ObservableProperty] private ObservableCollection<JobItemViewModel> jobs = new();

    public JobListViewModel(BackupJobService jobService, BackupEngine engine)
    {
        _jobService = jobService;
        _engine = engine;
        Refresh();
    }

    public int Count => Jobs.Count;
    public bool IsEmpty => Jobs.Count == 0;
    public bool IsFull => Jobs.Count >= AppConfig.MaxJobs;

    public void Refresh()
    {
        Jobs.Clear();
        foreach (var job in _jobService.GetAll())
            Jobs.Add(new JobItemViewModel(job));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFull));
    }

    public JobItemViewModel? FindById(int id) =>
        Jobs.FirstOrDefault(j => j.Id == id);

    public int? GetNextAvailableId()
    {
        for (int i = 1; i <= AppConfig.MaxJobs; i++)
            if (!_jobService.IdExists(i)) return i;
        return null;
    }

    public bool IdExists(int id) => _jobService.IdExists(id);

    [RelayCommand]
    public void DeleteJob(int id)
    {
        if (_jobService.Delete(id)) Refresh();
    }

    [RelayCommand]
    public void ExecuteJob(int id)
    {
        var job = _jobService.GetById(id);
        if (job is null) return;
        _engine.ExecuteJob(job);
    }

    [RelayCommand]
    public void ExecuteAll()
    {
        _engine.ExecuteJobs(_jobService.GetAll().Select(j => j.Id));
    }

    [RelayCommand]
    public void ExecuteMany(IEnumerable<int> ids)
    {
        _engine.ExecuteJobs(ids);
    }
}
