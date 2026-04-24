using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Models;
using EasySave.Services;

namespace EasySave.ViewModels;

public enum JobFormMode { Create, Edit }

public partial class JobFormViewModel : ObservableObject
{
    private readonly JobRepository _repo;

    [ObservableProperty] private JobFormMode mode;
    [ObservableProperty] private int id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValidName))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValidSource))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string sourcePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValidTarget))]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string targetPath = string.Empty;

    [ObservableProperty] private BackupType type = BackupType.Full;

    public JobFormViewModel(JobRepository repo)
    {
        _repo = repo;
    }

    public bool IsValidName => InputValidator.IsValidJobName(Name);
    public bool IsValidSource => InputValidator.IsExistingDirectory(SourcePath);
    public bool IsValidTarget => InputValidator.IsValidPath(TargetPath);
    public bool CanSave => IsValidName && IsValidSource && IsValidTarget;

    public void LoadForCreate(int assignedId)
    {
        Mode = JobFormMode.Create;
        Id = assignedId;
        Name = string.Empty;
        SourcePath = string.Empty;
        TargetPath = string.Empty;
        Type = BackupType.Full;
    }

    public void LoadForEdit(BackupJob job)
    {
        Mode = JobFormMode.Edit;
        Id = job.Id;
        Name = job.Name;
        SourcePath = job.SourcePath;
        TargetPath = job.TargetPath;
        Type = job.Type;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var job = new BackupJob
        {
            Id = Id,
            Name = Name,
            SourcePath = SourcePath,
            TargetPath = TargetPath,
            Type = Type
        };

        if (Mode == JobFormMode.Create) _repo.AddJob(job);
        else _repo.UpdateJob(Id, job);
    }
}
