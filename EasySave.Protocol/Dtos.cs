namespace EasySave.Protocol;

// DTOs neutres (pas de dépendance à EasySave.Core / EasySave.Models). Le serveur
// et le client convertissent depuis/vers leurs types internes. Ça évite que la
// couche réseau se balade avec WPF/MVVM/ObservableObject sur le câble.

public enum BackupTypeDto
{
    Full,
    Differential
}

public enum JobStatusDto
{
    Inactive,
    Active,
    Paused
}

public enum PauseReasonDto
{
    User,
    Business,
    FileLocked
}

public sealed class BackupJobDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public BackupTypeDto Type { get; set; } = BackupTypeDto.Full;
}

public sealed class JobEntryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public BackupTypeDto Type { get; set; } = BackupTypeDto.Full;

    public JobStatusDto Status { get; set; } = JobStatusDto.Inactive;
    public DateTime LastActionTime { get; set; }
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public double ProgressPercent { get; set; }
    public int RemainingFiles { get; set; }
    public long RemainingSizeBytes { get; set; }
    public string CurrentSourceFile { get; set; } = string.Empty;
    public string CurrentDestinationFile { get; set; } = string.Empty;
}

public sealed class BackupProgressDto
{
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string CurrentSourceFile { get; set; } = string.Empty;
    public string CurrentDestinationFile { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public long BytesDone { get; set; }
    public double ProgressPercent { get; set; }
}

public sealed class JobLifecycleDto
{
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public PauseReasonDto? Reason { get; set; }
    public string? Detail { get; set; }
}

// Settings exposés au client. Ne contient QUE les champs pertinents pour le
// pilotage à distance (pas le thème WPF ni la langue UI, qui restent locaux).
public sealed class ServerSettingsDto
{
    public string? LogFormat { get; set; }
    public int? MaxJobs { get; set; }
    public string BusinessSoftwareName { get; set; } = string.Empty;
    public string BusinessSoftwareCheckMode { get; set; } = "StartOnly";
    public List<string> EncryptedExtensions { get; set; } = new();
    public string CryptoMode { get; set; } = "Rapide";
    public string? CryptoKey { get; set; }
    public int MaxParallelJobs { get; set; } = 4;
    public int MaxParallelFilesPerJob { get; set; } = 4;
    public int LargeFileThresholdKb { get; set; } = 1024;
}
