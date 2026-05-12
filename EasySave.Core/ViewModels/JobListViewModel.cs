using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class JobListViewModel : ObservableObject
{
    private readonly IJobRepository _repo;
    private readonly IBackupEngine _engine;
    private readonly SettingsService _settings;

    [ObservableProperty] private ObservableCollection<JobItemViewModel> jobs = new();

    public JobListViewModel(IJobRepository repo, IBackupEngine engine, SettingsService settings)
    {
        _repo = repo;
        _engine = engine;
        _settings = settings;
        Refresh();
    }

    public int Count => Jobs.Count;
    public bool IsEmpty => Jobs.Count == 0;
    public bool HasJobs => Jobs.Count > 0;
    public int MaxJobs => _settings.MaxJobs;
    public bool IsFull => Jobs.Count >= _settings.MaxJobs;
    public bool HasAnyChecked => Jobs.Any(j => j.IsSelected);
    public int SelectedCount => Jobs.Count(j => j.IsSelected);

    public void Refresh()
    {
        var previouslyChecked = Jobs.Where(j => j.IsSelected).Select(j => j.Id).ToHashSet();

        // Détache les handlers IsSelected des anciennes VMs avant Clear,
        // sinon HasAnyChecked continuerait de les écouter.
        foreach (var oldVm in Jobs) oldVm.PropertyChanged -= OnJobItemPropertyChanged;

        Jobs.Clear();
        foreach (var job in _repo.GetAllJobs())
        {
            var vm = new JobItemViewModel(job);
            if (previouslyChecked.Contains(job.Id)) vm.IsSelected = true;
            vm.PropertyChanged += OnJobItemPropertyChanged;
            Jobs.Add(vm);
        }
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(IsFull));
        OnPropertyChanged(nameof(HasAnyChecked));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void OnJobItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(HasAnyChecked));
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    public JobItemViewModel? FindById(int id) =>
        Jobs.FirstOrDefault(j => j.Id == id);

    public JobItemViewModel? FindByIndex(int index1Based) =>
        index1Based >= 1 && index1Based <= Jobs.Count ? Jobs[index1Based - 1] : null;

    [RelayCommand]
    public async Task DeleteJobAsync(int id)
    {
        if (await _repo.DeleteJobAsync(id)) Refresh();
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
