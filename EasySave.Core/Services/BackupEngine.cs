using System.Diagnostics;
using System.Security.Cryptography;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

public class BackupEngine
{
    private readonly JobRepository _repo;
    private readonly ILogger _logger;
    private readonly IBusinessSoftwareMonitor? _businessMonitor;
    private readonly ICryptoSoft? _crypto;
    private readonly SettingsService? _settings;

    // Signal de reprise. Initial: set (true) = pas de pause.
    // Quand le logiciel métier est détecté → Reset() (signal éteint) → le
    // thread d'exécution se bloque sur Wait() jusqu'à appel à Resume().
    private readonly ManualResetEventSlim _resumeSignal = new(initialState: true);

    public BackupEngine(JobRepository repo, ILogger logger,
        IBusinessSoftwareMonitor? businessMonitor = null,
        ICryptoSoft? crypto = null,
        SettingsService? settings = null)
    {
        _repo = repo;
        _logger = logger;
        _businessMonitor = businessMonitor;
        _crypto = crypto;
        _settings = settings;
    }

    public event EventHandler<BackupProgressEventArgs>? ProgressChanged;
    public event EventHandler<string>? JobStarted;
    public event EventHandler<string>? JobCompleted;
    // Levé chaque fois qu'un job entre en pause à cause du logiciel métier.
    public event EventHandler<string>? JobPaused;
    // Levé quand l'utilisateur clique sur Reprendre.
    public event EventHandler<string>? JobResumed;

    // True si au moins un job est actuellement en pause (signal Reset).
    public bool IsPaused => !_resumeSignal.IsSet;

    // Réveille le thread d'exécution bloqué dans WaitForResume().
    // Appelé par le bouton "Reprendre" du ViewModel.
    public void Resume()
    {
        _resumeSignal.Set();
    }

    public void ExecuteJobs(IEnumerable<int> jobIds)
    {
        foreach (int id in jobIds)
        {
            var job = _repo.GetJobById(id);
            if (job is null) continue;
            ExecuteJob(job);
        }
    }

    public Task ExecuteJobsAsync(IEnumerable<int> jobIds, CancellationToken ct = default)
    {
        var ids = jobIds.ToList();
        return Task.Run(() =>
        {
            foreach (int id in ids)
            {
                if (ct.IsCancellationRequested) break;
                var job = _repo.GetJobById(id);
                if (job is null) continue;
                ExecuteJob(job);
            }
        }, ct);
    }

    public Task ExecuteJobAsync(BackupJob job, CancellationToken ct = default)
        => Task.Run(() => ExecuteJob(job), ct);

    public void ExecuteJob(BackupJob job)
    {
        if (!Directory.Exists(job.SourcePath)) return;

        // Check pré-job : si le logiciel métier tourne déjà au moment du clic
        // sur "Exécuter", on pause immédiatement (avant même JobStarted).
        WaitWhileBusinessSoftwareRunning(job);

        Directory.CreateDirectory(job.TargetPath);

        JobStarted?.Invoke(this, job.Name);

        var files = ScanDirectory(job.SourcePath);
        long totalSize = files.Sum(f => f.Length);
        int totalFiles = files.Count;

        _repo.UpdateState(job.Id, e =>
        {
            e.Status = JobStatus.Active;
            e.TotalFiles = totalFiles;
            e.TotalSizeBytes = totalSize;
            e.RemainingFiles = totalFiles;
            e.RemainingSizeBytes = totalSize;
            e.ProgressPercent = 0;
            e.LastActionTime = DateTime.Now;
        });

        int processed = 0;
        long bytesDone = 0;

        foreach (var sourceFile in files)
        {
            // CHECK 1 : logiciel métier global (configuré dans Paramètres).
            WaitWhileBusinessSoftwareRunning(job);

            // CHECK 2 : fichier source verrouillé par une autre application
            // (ex. .docx ouvert dans Word). On bloque AVANT la copie : si on
            // attendait la copie, File.Copy passerait silencieusement sur les
            // fichiers en partage-lecture. Ici on force un test exclusif.
            WaitWhileFileLocked(sourceFile, job);

            // Le fichier peut avoir disparu pendant la pause (cas typique :
            // Word a supprimé son lock file ~$xxx.docx en se fermant).
            sourceFile.Refresh();
            if (!sourceFile.Exists)
            {
                processed++;
                PushProgress(job, sourceFile.FullName, string.Empty, processed, totalFiles, bytesDone, totalSize);
                continue;
            }

            string relative = Path.GetRelativePath(job.SourcePath, sourceFile.FullName);
            string destination = Path.Combine(job.TargetPath, relative);

            if (job.Type == BackupType.Differential && !NeedsCopy(sourceFile, destination))
            {
                processed++;
                bytesDone += sourceFile.Length;
                PushProgress(job, sourceFile.FullName, destination, processed, totalFiles, bytesDone, totalSize);
                continue;
            }

            string? destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            bool existedBefore = File.Exists(destination);
            long elapsedMs = CopyFile(sourceFile.FullName, destination);

            long cryptoMs = 0;
            if (_crypto is not null && elapsedMs >= 0 && ShouldEncrypt(sourceFile.FullName))
            {
                cryptoMs = _crypto.Encrypt(destination);
            }

            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = job.Id,
                BackupName = job.Name,
                Action = existedBefore ? LogAction.Update : LogAction.Create,
                SourceFilePath = sourceFile.FullName,
                DestinationFilePath = destination,
                FileSizeBytes = sourceFile.Length,
                TransferTimeMs = elapsedMs,
                CryptoTimeMs = cryptoMs
            });

            processed++;
            bytesDone += sourceFile.Length;
            PushProgress(job, sourceFile.FullName, destination, processed, totalFiles, bytesDone, totalSize);
        }

        RemoveOrphans(job, files);

        _repo.ClearState(job.Id);
        JobCompleted?.Invoke(this, job.Name);
    }

    // ------------------------------------------------------------------------
    // WaitWhileBusinessSoftwareRunning : si le logiciel métier tourne,
    // marque le job comme Paused, log l'événement, fire JobPaused, puis
    // BLOQUE le thread sur _resumeSignal jusqu'à ce que :
    //   - l'utilisateur clique sur Reprendre (Resume() set le signal),
    //   - ET que le logiciel métier soit redevenu inactif.
    // Au réveil, on revérifie. Si le logiciel tourne toujours → on repause.
    // ------------------------------------------------------------------------
    private void WaitWhileBusinessSoftwareRunning(BackupJob job)
    {
        if (_businessMonitor is null) return;

        bool wasPaused = false;

        while (_businessMonitor.IsRunning())
        {
            if (!wasPaused)
            {
                // Première détection → on bascule en pause.
                wasPaused = true;
                _resumeSignal.Reset();   // signal éteint → futur Wait() bloquant

                _repo.UpdateState(job.Id, e =>
                {
                    e.Status = JobStatus.Paused;
                    e.LastActionTime = DateTime.Now;
                });

                LogBusinessSoftwareDetected(job);
                JobPaused?.Invoke(this, job.Name);
            }

            // Bloque le thread jusqu'à appel à Resume(). Pas de busy-loop CPU.
            _resumeSignal.Wait();

            // Au réveil, on retombe en haut du while pour re-vérifier IsRunning().
            // → si le logiciel tourne toujours, on repause (boucle).
            // → sinon, on sort du while et on continue le job.
        }

        if (wasPaused)
        {
            // Sortie de pause confirmée → on remet l'état Active.
            _repo.UpdateState(job.Id, e =>
            {
                e.Status = JobStatus.Active;
                e.LastActionTime = DateTime.Now;
            });
            JobResumed?.Invoke(this, job.Name);
        }
    }

    // ------------------------------------------------------------------------
    // WaitWhileFileLocked : si un autre process tient le fichier source
    // (ex. Word ouvre le .docx → verrou de partage), on pause le job ici
    // jusqu'à ce que l'utilisateur ferme le fichier ET clique Reprendre.
    // ------------------------------------------------------------------------
    private void WaitWhileFileLocked(FileInfo sourceFile, BackupJob job)
    {
        bool wasPaused = false;

        while (IsFileLocked(sourceFile))
        {
            if (!wasPaused)
            {
                wasPaused = true;
                _resumeSignal.Reset();

                _repo.UpdateState(job.Id, e =>
                {
                    e.Status = JobStatus.Paused;
                    e.LastActionTime = DateTime.Now;
                    e.CurrentSourceFile = sourceFile.FullName;
                });

                _logger.Log(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    JobId = job.Id,
                    BackupName = job.Name,
                    Action = LogAction.BusinessSoftwareDetected,
                    SourceFilePath = sourceFile.FullName,
                    DestinationFilePath = string.Empty,
                    FileSizeBytes = sourceFile.Exists ? sourceFile.Length : 0,
                    TransferTimeMs = 0
                });

                JobPaused?.Invoke(this, job.Name);
            }

            _resumeSignal.Wait();

            // FileInfo cache la taille/existence → on rafraîchit avant le re-test.
            sourceFile.Refresh();
        }

        if (wasPaused)
        {
            _repo.UpdateState(job.Id, e =>
            {
                e.Status = JobStatus.Active;
                e.LastActionTime = DateTime.Now;
            });
            JobResumed?.Invoke(this, job.Name);
        }
    }

    // ------------------------------------------------------------------------
    // IsFileLocked : tente d'ouvrir le fichier en mode EXCLUSIF (FileShare.None).
    // Si une autre app le tient (Word, Excel, etc.) → IOException → true.
    // Si le fichier n'existe plus → on considère qu'il n'est pas verrouillé.
    // ------------------------------------------------------------------------
    private static bool IsFileLocked(FileInfo file)
    {
        if (!file.Exists) return false;

        try
        {
            // FileShare.None = exclusif : aucun autre handle autorisé.
            // Si Word a ouvert le .docx en partage-lecture, cet appel échoue.
            using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;          // verrouillé par un autre process
        }
        catch (UnauthorizedAccessException)
        {
            return true;          // pas le droit en exclusif → considère verrouillé
        }
    }

    private void LogBusinessSoftwareDetected(BackupJob job)
    {
        _logger.Log(new LogEntry
        {
            Timestamp = DateTime.Now,
            JobId = job.Id,
            BackupName = job.Name,
            Action = LogAction.BusinessSoftwareDetected,
            SourceFilePath = job.SourcePath,
            DestinationFilePath = job.TargetPath,
            FileSizeBytes = 0,
            TransferTimeMs = 0
        });
    }

    private void RemoveOrphans(BackupJob job, List<FileInfo> sourceFiles)
    {
        if (!Directory.Exists(job.TargetPath)) return;

        var expected = new HashSet<string>(
            sourceFiles.Select(f => Path.Combine(job.TargetPath, Path.GetRelativePath(job.SourcePath, f.FullName))),
            StringComparer.OrdinalIgnoreCase);

        foreach (var destFile in new DirectoryInfo(job.TargetPath).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (expected.Contains(destFile.FullName)) continue;

            long size = destFile.Length;
            string fullPath = destFile.FullName;
            try { destFile.Delete(); }
            catch { continue; }

            _logger.Log(new LogEntry
            {
                Timestamp = DateTime.Now,
                JobId = job.Id,
                BackupName = job.Name,
                Action = LogAction.Delete,
                SourceFilePath = string.Empty,
                DestinationFilePath = fullPath,
                FileSizeBytes = size,
                TransferTimeMs = 0
            });
        }
    }

    private void PushProgress(BackupJob job, string sourceFile, string destinationFile,
        int processed, int totalFiles, long bytesDone, long totalBytes)
    {
        double percent = totalFiles == 0 ? 100 : (double)processed * 100 / totalFiles;

        _repo.UpdateState(job.Id, e =>
        {
            e.LastActionTime = DateTime.Now;
            e.CurrentSourceFile = sourceFile;
            e.CurrentDestinationFile = destinationFile;
            e.RemainingFiles = totalFiles - processed;
            e.RemainingSizeBytes = totalBytes - bytesDone;
            e.ProgressPercent = percent;
        });

        ProgressChanged?.Invoke(this, new BackupProgressEventArgs
        {
            JobName = job.Name,
            CurrentSourceFile = sourceFile,
            CurrentDestinationFile = destinationFile,
            TotalFiles = totalFiles,
            ProcessedFiles = processed,
            TotalSizeBytes = totalBytes,
            BytesDone = bytesDone,
            ProgressPercent = percent
        });
    }

    private bool ShouldEncrypt(string sourceFilePath)
    {
        var extensions = _settings?.Current.EncryptedExtensions;
        if (extensions is null || extensions.Count == 0) return false;

        string ext = Path.GetExtension(sourceFilePath);
        if (string.IsNullOrEmpty(ext)) return false;

        return extensions.Any(e => string.Equals(
            NormalizeExtension(e), ext, StringComparison.OrdinalIgnoreCase));
    }

    internal static string NormalizeExtension(string ext)
    {
        ext = ext.Trim().ToLowerInvariant();
        if (ext.Length == 0) return ext;
        return ext.StartsWith('.') ? ext : "." + ext;
    }

    private static List<FileInfo> ScanDirectory(string root)
    {
        return new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            // Filtre les fichiers de verrouillage Office (~$xxx.docx, ~$xxx.xlsx, ...).
            // Ce sont des fichiers cachés temporaires créés par Word/Excel/PPT qui
            // disparaissent à la fermeture de l'application → on ne les sauvegarde pas.
            .Where(f => !f.Name.StartsWith("~$"))
            .ToList();
    }

    private static bool NeedsCopy(FileInfo source, string destinationPath)
    {
        if (!File.Exists(destinationPath)) return true;
        var dest = new FileInfo(destinationPath);
        if (source.Length != dest.Length) return true;
        return !FilesHaveSameHash(source.FullName, destinationPath);
    }

    private static bool FilesHaveSameHash(string path1, string path2)
    {
        try
        {
            using var md5 = MD5.Create();
            using var s1 = File.OpenRead(path1);
            using var s2 = File.OpenRead(path2);
            byte[] h1 = md5.ComputeHash(s1);
            byte[] h2 = md5.ComputeHash(s2);
            return h1.SequenceEqual(h2);
        }
        catch
        {
            return false;
        }
    }

    private static long CopyFile(string source, string destination)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            File.Copy(source, destination, true);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            sw.Stop();
            return -sw.ElapsedMilliseconds;
        }
    }
}
