namespace EasySave.Models;

// Statut terminal d'une exécution de job. Source de vérité unique partagée
// par les events de cycle de vie et les logs (un seul mapping
// statut -> évènement -> action de log => pas de divergence possible).
public enum JobExecutionStatus
{
    Completed, // tous les fichiers traités sans annulation ni exception
    Stopped,   // annulation utilisateur (Stop / CancellationToken)
    Failed,    // exception remontée pendant l'exécution
    Rejected   // refus avant démarrage (source absente, déjà actif, etc.)
}

public sealed record JobExecutionResult(JobExecutionStatus Status, Exception? Error = null)
{
    public static JobExecutionResult Completed { get; } = new(JobExecutionStatus.Completed);
    public static JobExecutionResult Stopped   { get; } = new(JobExecutionStatus.Stopped);
    public static JobExecutionResult Rejected  { get; } = new(JobExecutionStatus.Rejected);
    public static JobExecutionResult Failed(Exception ex) => new(JobExecutionStatus.Failed, ex);
}
