namespace EasySave.Protocol;

// Catalogue centralisé des "type" qui apparaissent dans le champ Envelope.Type.
// Convention :
//  - "cmd.<nom>"   : commande client → serveur (attend une réponse)
//  - "evt.<nom>"   : événement broadcast serveur → tous clients
//  - "rsp.<nom>"   : réponse à une commande (corrélée via CorrelationId)
public static class MessageTypes
{
    // === Commandes (client → serveur) ===
    public const string CmdGetSnapshot = "cmd.snapshot";
    public const string CmdGetJobs = "cmd.jobs.list";
    public const string CmdAddJob = "cmd.jobs.add";
    public const string CmdUpdateJob = "cmd.jobs.update";
    public const string CmdDeleteJob = "cmd.jobs.delete";
    public const string CmdRunJobs = "cmd.jobs.run";
    public const string CmdPauseJob = "cmd.jobs.pause";
    public const string CmdResumeJob = "cmd.jobs.resume";
    public const string CmdStopJob = "cmd.jobs.stop";
    public const string CmdPauseAll = "cmd.jobs.pauseAll";
    public const string CmdResumeAll = "cmd.jobs.resumeAll";
    public const string CmdStopAll = "cmd.jobs.stopAll";
    public const string CmdGetSettings = "cmd.settings.get";
    public const string CmdUpdateSettings = "cmd.settings.update";

    // === Réponses (serveur → client, corrélées) ===
    public const string RspOk = "rsp.ok";
    public const string RspError = "rsp.error";
    public const string RspSnapshot = "rsp.snapshot";
    public const string RspJobs = "rsp.jobs";
    public const string RspJob = "rsp.job";
    public const string RspSettings = "rsp.settings";

    // === Événements (serveur → tous clients) ===
    public const string EvtProgress = "evt.progress";
    public const string EvtJobStarted = "evt.job.started";
    public const string EvtJobCompleted = "evt.job.completed";
    public const string EvtJobStopped = "evt.job.stopped";
    public const string EvtJobPaused = "evt.job.paused";
    public const string EvtJobResumed = "evt.job.resumed";
    public const string EvtJobsChanged = "evt.jobs.changed";
    public const string EvtSettingsChanged = "evt.settings.changed";
}

// === Payloads de commandes ===

public sealed class RunJobsPayload
{
    public List<int> JobIds { get; set; } = new();
}

public sealed class JobIdPayload
{
    public int JobId { get; set; }
}

public sealed class AddJobPayload
{
    public BackupJobDto Job { get; set; } = new();
}

public sealed class UpdateJobPayload
{
    public int Id { get; set; }
    public BackupJobDto Job { get; set; } = new();
}

public sealed class UpdateSettingsPayload
{
    public ServerSettingsDto Settings { get; set; } = new();
}

// === Payloads de réponses ===

public sealed class ErrorPayload
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class SnapshotPayload
{
    public int ProtocolVersion { get; set; } = ProtocolConstants.Version;
    public List<BackupJobDto> Jobs { get; set; } = new();
    public ServerSettingsDto Settings { get; set; } = new();
    public List<int> ActiveJobIds { get; set; } = new();
}

public sealed class JobsPayload
{
    public List<BackupJobDto> Jobs { get; set; } = new();
}

public sealed class JobPayload
{
    public BackupJobDto Job { get; set; } = new();
}

public sealed class SettingsPayload
{
    public ServerSettingsDto Settings { get; set; } = new();
}

// Codes d'erreur standard côté serveur. Le client peut switcher dessus.
public static class ErrorCodes
{
    public const string JobNotFound = "job.notFound";
    public const string JobAlreadyRunning = "job.alreadyRunning";
    public const string MaxJobsReached = "jobs.maxReached";
    public const string InvalidPayload = "msg.invalidPayload";
    public const string UnknownCommand = "msg.unknownCommand";
    public const string Internal = "internal";
}
