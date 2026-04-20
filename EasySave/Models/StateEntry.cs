namespace EasySave.Models;

public class StateEntry
{
    public string JobName { get; set; } = string.Empty;
    public DateTime LastActionTime { get; set; } = DateTime.Now;
    public JobStatus Status { get; set; } = JobStatus.Inactive;
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public double ProgressPercent { get; set; }
    public int RemainingFiles { get; set; }
    public long RemainingSizeBytes { get; set; }
    public string CurrentSourceFile { get; set; } = string.Empty;
    public string CurrentDestinationFile { get; set; } = string.Empty;
}
