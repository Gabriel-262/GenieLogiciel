using EasySave.Models;
using EasySave.Protocol;
using EasySave.Services;

namespace EasySave.Client;

// Conversions DTO -> types EasySave.Core côté client. Symétrique de DtoMapper
// côté serveur, mais on n'expose que ce dont VMs/CLI ont besoin.
internal static class RemoteDtoMapper
{
    public static BackupType FromDto(this BackupTypeDto t) => t switch
    {
        BackupTypeDto.Differential => BackupType.Differential,
        _ => BackupType.Full
    };

    public static BackupTypeDto ToDto(this BackupType t) => t switch
    {
        BackupType.Differential => BackupTypeDto.Differential,
        _ => BackupTypeDto.Full
    };

    public static PauseReason FromDto(this PauseReasonDto r) => r switch
    {
        PauseReasonDto.Business   => PauseReason.Business,
        PauseReasonDto.FileLocked => PauseReason.FileLocked,
        _ => PauseReason.User
    };

    public static BackupJob FromDto(this BackupJobDto d) => new()
    {
        Id = d.Id,
        Name = d.Name ?? string.Empty,
        SourcePath = d.SourcePath ?? string.Empty,
        TargetPath = d.TargetPath ?? string.Empty,
        Type = d.Type.FromDto()
    };

    public static BackupJobDto ToDto(this BackupJob j) => new()
    {
        Id = j.Id,
        Name = j.Name,
        SourcePath = j.SourcePath,
        TargetPath = j.TargetPath,
        Type = j.Type.ToDto()
    };

    public static BackupProgressEventArgs ToEventArgs(this BackupProgressDto d) => new()
    {
        JobId = d.JobId,
        JobName = d.JobName ?? string.Empty,
        CurrentSourceFile = d.CurrentSourceFile ?? string.Empty,
        CurrentDestinationFile = d.CurrentDestinationFile ?? string.Empty,
        TotalFiles = d.TotalFiles,
        ProcessedFiles = d.ProcessedFiles,
        TotalSizeBytes = d.TotalSizeBytes,
        BytesDone = d.BytesDone,
        ProgressPercent = d.ProgressPercent
    };

    public static JobLifecycleEventArgs ToEventArgs(this JobLifecycleDto d) => new()
    {
        JobId = d.JobId,
        JobName = d.JobName ?? string.Empty,
        Reason = d.Reason?.FromDto()
    };
}
