using EasySave.Models;

namespace EasySave.Services;

// Surface CRUD du dépôt de jobs vue par les clients (UI WPF, CLI).
// Implémentations :
//   - JobRepository           : lecture/écriture locale du jobs.json (côté serveur).
//   - RemoteJobRepository     : forwarde les commandes CRUD via TCP (côté client).
//
// L'API "état runtime" (UpdateState / ClearState) reste interne à JobRepository :
// seul le moteur côté serveur en a besoin, donc elle ne traverse pas le réseau.
public interface IJobRepository
{
    int Count { get; }

    List<BackupJob> GetAllJobs();
    BackupJob? GetJobById(int id);
    BackupJob? GetJobByIndex(int index1Based);

    BackupJob AddJob(BackupJob job);
    bool UpdateJob(int id, BackupJob updated);
    bool DeleteJob(int id);
}
