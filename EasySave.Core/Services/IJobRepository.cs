using EasySave.Models;

namespace EasySave.Services;

// Surface CRUD du dépôt de jobs vue par les clients (UI WPF, CLI).
// Implémentations :
//   - JobRepository           : lecture/écriture locale du jobs.json (côté serveur).
//   - RemoteJobRepository     : forwarde les commandes CRUD via TCP (côté client).
//
// Les lectures sont servies depuis un cache local (instantanées) -> sync OK.
// Les écritures côté Remote font un aller-retour réseau -> versions async pour
// ne pas bloquer le thread UI. Les versions sync sont conservées pour la CLI
// et le serveur (où elles n'ont pas de coût réseau).
public interface IJobRepository
{
    int Count { get; }

    List<BackupJob> GetAllJobs();
    BackupJob? GetJobById(int id);
    BackupJob? GetJobByIndex(int index1Based);

    BackupJob AddJob(BackupJob job);
    bool UpdateJob(int id, BackupJob updated);
    bool DeleteJob(int id);

    Task<BackupJob> AddJobAsync(BackupJob job, CancellationToken ct = default);
    Task<bool> UpdateJobAsync(int id, BackupJob updated, CancellationToken ct = default);
    Task<bool> DeleteJobAsync(int id, CancellationToken ct = default);
}
