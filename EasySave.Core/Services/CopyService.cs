using System.Diagnostics;

namespace EasySave.Services;

// Service de copie de fichiers. Responsabilités :
//   - copie cancellable (check du token toutes les BufferSize ko)
//   - suppression du fichier partiel si annulation ou erreur
//   - gate global des "gros fichiers" (> seuil paramétrable) avec N slots
//     pour ne pas saturer la bande passante du SSD/HDD.
//
// Retour de Copy :
//   >=0  durée en ms (succès)
//   0    annulé (fichier partiel supprimé) — distinguer via le CT du caller
//   <0   erreur (fichier partiel supprimé) ; |valeur| = temps écoulé
public sealed class CopyService
{
    // 1 MB : sweet spot SSD/NVMe — réduit les syscalls vs 64 KB,
    // tient en cache L2/L3, n'impacte pas le working set.
    private const int BufferSize = 1024 * 1024;

    // Gate global des gros fichiers. N slots configurables : sur SSD, 2-3
    // parallèles saturent mieux le contrôleur (NCQ) qu'une sérialisation
    // stricte à 1.
    private readonly SemaphoreSlim _largeFileGate;

    public CopyService(int maxParallelLargeFiles = 2)
    {
        int slots = Math.Max(1, maxParallelLargeFiles);
        _largeFileGate = new SemaphoreSlim(slots, slots);
    }

    public long Copy(string source, string destination, long largeThresholdBytes, CancellationToken ct)
    {
        bool isLarge = largeThresholdBytes > 0 && new FileInfo(source).Length >= largeThresholdBytes;
        if (isLarge) _largeFileGate.Wait(ct);
        try
        {
            if (ct.IsCancellationRequested) return 0;
            return CopyInner(source, destination, ct);
        }
        finally
        {
            if (isLarge) _largeFileGate.Release();
        }
    }

    private static long CopyInner(string source, string destination, CancellationToken ct)
    {
        // FileOptions.Asynchronous + SequentialScan : permet au scheduler I/O
        // du noyau de chevaucher lecture/écriture et au contrôleur SSD de
        // tirer profit de la file de commandes (NCQ).
        const FileOptions opts = FileOptions.SequentialScan | FileOptions.Asynchronous;

        var sw = Stopwatch.StartNew();
        bool canceled = false;
        try
        {
            using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                                            BufferSize, opts))
            using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                                            BufferSize, opts))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (ct.IsCancellationRequested)
                    {
                        canceled = true;
                        break;
                    }
                    dst.Write(buffer, 0, read);
                }
            }
            sw.Stop();
            if (canceled)
            {
                TryDelete(destination);
                return 0;
            }
            return sw.ElapsedMilliseconds;
        }
        catch
        {
            sw.Stop();
            TryDelete(destination);
            return -sw.ElapsedMilliseconds;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
