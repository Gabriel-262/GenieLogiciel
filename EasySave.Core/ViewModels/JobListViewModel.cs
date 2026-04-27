using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class JobListViewModel : ObservableObject
{
    private readonly JobRepository _repo;
    private readonly BackupEngine _engine;

    [ObservableProperty] private ObservableCollection<JobItemViewModel> jobs = new();

    public JobListViewModel(JobRepository repo, BackupEngine engine)
    {
        _repo = repo;
        _engine = engine;
        Refresh();
    }

    public int Count => Jobs.Count;
    public bool IsEmpty => Jobs.Count == 0;

    public void Refresh()
    {
        Jobs.Clear();
        foreach (var job in _repo.GetAllJobs())
            Jobs.Add(new JobItemViewModel(job));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public JobItemViewModel? FindById(int id) =>
        Jobs.FirstOrDefault(j => j.Id == id);

    public int GetNextAvailableId()
    {
        int i = 1;
        while (_repo.IdExists(i)) i++;
        return i;
    }

    public bool IdExists(int id) => _repo.IdExists(id);

    [RelayCommand]
    public void DeleteJob(int id)
    {
        if (_repo.DeleteJob(id)) Refresh();
    }

    [RelayCommand]
    public Task ExecuteJobAsync(int id)
    {
        var job = _repo.GetJobById(id);
        if (job is null) return Task.CompletedTask;
        return _engine.ExecuteJobAsync(job);
    }

    [RelayCommand]
    public Task ExecuteAllAsync()
    {
        return _engine.ExecuteJobsAsync(_repo.GetAllJobs().Select(j => j.Id));
    }

    [RelayCommand]
    public Task ExecuteManyAsync(IEnumerable<int> ids)
    {
        return _engine.ExecuteJobsAsync(ids);
    }
}
