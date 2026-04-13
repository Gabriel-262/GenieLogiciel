namespace EasySave.Models;

public class StateEntry
{
    public string Name { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Status { get; set; } = "Inactive";
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public int FilesRemaining { get; set; }
    public long SizeRemaining { get; set; }
    public double Progress { get; set; }
    public string CurrentSourceFile { get; set; } = string.Empty;
    public string CurrentDestFile { get; set; } = string.Empty;
}
