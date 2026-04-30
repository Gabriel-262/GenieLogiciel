using CommunityToolkit.Mvvm.ComponentModel;
using EasySave.Models;

namespace EasySave.ViewModels;

public partial class JobItemViewModel : ObservableObject
{
    [ObservableProperty] private int id;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string sourcePath = string.Empty;
    [ObservableProperty] private string targetPath = string.Empty;
    [ObservableProperty] private BackupType type;
    [ObservableProperty] private bool isSelected;

    public JobItemViewModel() { }

    public JobItemViewModel(BackupJob job)
    {
        id = job.Id;
        name = job.Name;
        sourcePath = job.SourcePath;
        targetPath = job.TargetPath;
        type = job.Type;
    }

    public BackupJob ToModel() => new()
    {
        Id = Id,
        Name = Name,
        SourcePath = SourcePath,
        TargetPath = TargetPath,
        Type = Type
    };
}
