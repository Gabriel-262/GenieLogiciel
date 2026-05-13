using EasySave.Models;
using EasySave.Protocol;
using EasySave.Services;

namespace EasySave.Server;

// Conversions DTO ↔ types internes. Centralisé ici pour qu'aucun handler ou
// session n'ait à connaître les détails (et qu'on évite les drifts de mapping
// entre commande et événement).
internal static class DtoMapper
{
    public static BackupTypeDto ToDto(this BackupType t) => t switch
    {
        BackupType.Full => BackupTypeDto.Full,
        BackupType.Differential => BackupTypeDto.Differential,
        _ => BackupTypeDto.Full
    };

    public static BackupType FromDto(this BackupTypeDto t) => t switch
    {
        BackupTypeDto.Full => BackupType.Full,
        BackupTypeDto.Differential => BackupType.Differential,
        _ => BackupType.Full
    };

    public static JobStatusDto ToDto(this JobStatus s) => s switch
    {
        JobStatus.Inactive => JobStatusDto.Inactive,
        JobStatus.Active => JobStatusDto.Active,
        JobStatus.Paused => JobStatusDto.Paused,
        _ => JobStatusDto.Inactive
    };

    public static PauseReasonDto ToDto(this PauseReason r) => r switch
    {
        PauseReason.User => PauseReasonDto.User,
        PauseReason.Business => PauseReasonDto.Business,
        PauseReason.FileLocked => PauseReasonDto.FileLocked,
        _ => PauseReasonDto.User
    };

    public static BackupJobDto ToDto(this BackupJob j) => new()
    {
        Id = j.Id,
        Name = j.Name,
        SourcePath = j.SourcePath,
        TargetPath = j.TargetPath,
        Type = j.Type.ToDto()
    };

    public static BackupJob FromDto(this BackupJobDto d) => new()
    {
        Id = d.Id,
        Name = d.Name ?? string.Empty,
        SourcePath = d.SourcePath ?? string.Empty,
        TargetPath = d.TargetPath ?? string.Empty,
        Type = d.Type.FromDto()
    };

    public static JobEntryDto ToDto(this JobEntry e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        SourcePath = e.SourcePath,
        TargetPath = e.TargetPath,
        Type = e.Type.ToDto(),
        Status = e.Status.ToDto(),
        LastActionTime = e.LastActionTime,
        TotalFiles = e.TotalFiles,
        TotalSizeBytes = e.TotalSizeBytes,
        ProgressPercent = e.ProgressPercent,
        RemainingFiles = e.RemainingFiles,
        RemainingSizeBytes = e.RemainingSizeBytes,
        CurrentSourceFile = e.CurrentSourceFile,
        CurrentDestinationFile = e.CurrentDestinationFile
    };

    public static BackupProgressDto ToDto(this BackupProgressEventArgs e) => new()
    {
        JobId = e.JobId,
        JobName = e.JobName,
        CurrentSourceFile = e.CurrentSourceFile,
        CurrentDestinationFile = e.CurrentDestinationFile,
        TotalFiles = e.TotalFiles,
        ProcessedFiles = e.ProcessedFiles,
        TotalSizeBytes = e.TotalSizeBytes,
        BytesDone = e.BytesDone,
        ProgressPercent = e.ProgressPercent
    };

    public static JobLifecycleDto ToDto(this JobLifecycleEventArgs e) => new()
    {
        JobId = e.JobId,
        JobName = e.JobName,
        Reason = e.Reason?.ToDto()
    };

    public static ServerSettingsDto ToServerDto(this AppSettings s) => new()
    {
        LogFormat = s.LogFormat,
        MaxJobs = s.MaxJobs,
        BusinessSoftwareName = s.BusinessSoftwareName,
        BusinessSoftwareCheckMode = s.BusinessSoftwareCheckMode,
        EncryptedExtensions = new List<string>(s.EncryptedExtensions),
        CryptoMode = s.CryptoMode,
        CryptoKey = s.CryptoKey,
        MaxParallelJobs = s.MaxParallelJobs,
        MaxParallelFilesPerJob = s.MaxParallelFilesPerJob,
        LargeFileThresholdKb = s.LargeFileThresholdKb
    };

    // Applique les champs pilotables à distance sur l'AppSettings local. Les
    // champs non-réseau (Theme, Language, BackKey, paths) restent intacts.
    public static void ApplyServerDto(this AppSettings target, ServerSettingsDto src)
    {
        target.LogFormat = src.LogFormat;
        target.MaxJobs = src.MaxJobs;
        target.BusinessSoftwareName = src.BusinessSoftwareName ?? string.Empty;
        target.BusinessSoftwareCheckMode = src.BusinessSoftwareCheckMode ?? "StartOnly";
        target.EncryptedExtensions = new List<string>(src.EncryptedExtensions ?? new List<string>());
        target.CryptoMode = src.CryptoMode ?? "Rapide";
        target.CryptoKey = src.CryptoKey;
        target.MaxParallelJobs = Math.Max(1, src.MaxParallelJobs);
        target.MaxParallelFilesPerJob = Math.Max(1, src.MaxParallelFilesPerJob);
        target.LargeFileThresholdKb = Math.Max(0, src.LargeFileThresholdKb);
    }
}
