namespace EasySave.Services;

public class BackupProgressEventArgs : EventArgs
{
    public string JobName { get; init; } = string.Empty;
    public string CurrentSourceFile { get; init; } = string.Empty;
    public string CurrentDestinationFile { get; init; } = string.Empty;
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public long TotalSizeBytes { get; init; }
    public long BytesDone { get; init; }
    public double ProgressPercent { get; init; }
}
