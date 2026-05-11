namespace EasySave.Services;

// Barrière GLOBALE (tous jobs confondus) qui bloque le traitement des
// fichiers non prioritaires tant qu'il reste des fichiers prioritaires en
// attente sur n'importe quel job.
//
// Cycle de vie typique d'un job :
//   Register(N)                  : N fichiers prioritaires à traiter
//   NotifyOneDone() × M          : M traités au fur et à mesure
//   Release(N - M) si stop avant
//
// Quand le compteur global atteint 0, les threads non prioritaires en
// WaitForNoPriority() sont réveillés.
public sealed class PriorityGate
{
    private int _pending;
    private readonly ManualResetEventSlim _open = new(true);

    public bool IsOpen => _open.IsSet;
    public int Pending => Volatile.Read(ref _pending);

    // Réserve N crédits prioritaires. Si on était à 0, ferme la barrière.
    public void Register(int count)
    {
        if (count <= 0) return;
        int newVal = Interlocked.Add(ref _pending, count);
        if (newVal > 0) _open.Reset();
    }

    // Un fichier prioritaire vient d'être traité.
    public void NotifyOneDone()
    {
        if (Interlocked.Decrement(ref _pending) == 0) _open.Set();
    }

    // Filet de sécurité : un job a été stoppé avant d'avoir traité tous ses
    // prioritaires. Libère les crédits restants pour ne pas bloquer les
    // autres jobs.
    public void Release(int count)
    {
        if (count <= 0) return;
        if (Interlocked.Add(ref _pending, -count) <= 0) _open.Set();
    }

    // Bloque jusqu'à ce que la barrière soit ouverte (pending == 0). Ne lève
    // PAS d'exception : le caller vérifie son propre stop-token après.
    public void WaitForNoPriority() => _open.Wait();
}
