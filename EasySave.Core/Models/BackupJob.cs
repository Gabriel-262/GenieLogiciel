namespace EasySave.Models;

public class BackupJob
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public BackupType Type { get; set; } = BackupType.Full;
}
