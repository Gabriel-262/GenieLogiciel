using EasySave.Models;

namespace EasySave.Services;

// Surface du dépôt d'état runtime utilisée par le moteur d'exécution.
// Découplée de IJobRepository (CRUD) pour ne dépendre côté JobRunner /
// JobOrchestrator que des opérations strictement nécessaires et faciliter
// les tests unitaires (mock dédié).
public interface IJobStateStore
{
    void UpdateState(int jobId, Action<JobEntry> mutate);
    void ClearState(int jobId);
}
