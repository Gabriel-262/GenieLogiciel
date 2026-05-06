namespace EasySave.Services;

public class JobLifecycleEventArgs : EventArgs
{
    public int JobId { get; init; }
    public string JobName { get; init; } = string.Empty;
}
