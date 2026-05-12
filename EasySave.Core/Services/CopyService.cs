using System.Diagnostics;

namespace EasySave.Services;

// Service de copie de fichiers. Responsabilités :
//   - copie cancellable (check du token toutes les BufferSize ko)
//   - suppression du fichier partiel si annulation ou erreur
//   - sérialisation globale des "gros fichiers" (> seuil paramétrable) pour
//     ne pas saturer la bande passante.
//
// Retour de Copy :
//   >=0  durée en ms (succès)
//   0    annulé (fichier partiel supprimé) — distinguer via le CT du caller
//   <0   erreur (fichier partiel supprimé) ; |valeur| = temps écoulé
public sealed class CopyService
{
    private const int BufferSize = 64 * 1024;

    // Sérialise globalement la copie des fichiers >= largeThresholdBytes.
    // Un seul gros fichier peut occuper la bande passante à un instant T ;
    // les petits continuent en parallèle.
    private readonly SemaphoreSlim _largeFileGate = new(1, 1);

    public long Copy(string source, string destination, long largeThresholdBytes, CancellationToken ct)
    {
        bool isLarge = largeThresholdBytes > 0 && new FileInfo(source).Length >= largeThresholdBytes;
        if (isLarge) _largeFileGate.Wait();
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
        var sw = Stopwatch.StartNew();
        bool canceled = false;
        try
        {
            using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                                            BufferSize, FileOptions.SequentialScan))
            using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                                            BufferSize, FileOptions.SequentialScan))
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
