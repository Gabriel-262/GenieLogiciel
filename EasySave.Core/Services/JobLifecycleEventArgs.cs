namespace EasySave.Services;

public class JobLifecycleEventArgs : EventArgs
{
    public int JobId { get; init; }
    public string JobName { get; init; } = string.Empty;

    // Renseigné pour les events Pause/Resume : indique la cause qui a déclenché
    // la transition (User, Business, FileLocked). Null pour Started/Completed/Stopped.
    public PauseReason? Reason { get; init; }

    // Contexte additionnel : pour FileLocked = chemin du fichier verrouillé,
    // pour Business = nom du process métier détecté, etc. Null sinon.
    public string? Detail { get; init; }
}
