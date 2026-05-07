namespace EasySave.Services;

// État runtime d'un job actif. Géré par BackupEngine, un par job en cours.
//
// - Pause "multi-cause" : plusieurs raisons (User, Business, FileLocked) peuvent
//   coexister. Le job reste bloqué tant qu'au moins une cause est active.
//   Conséquence : un Resume utilisateur ne sort PAS le job de la pause si le
//   logiciel métier tourne encore (et inversement).
//
// - Stop : annulation immédiate via CancellationToken. Le copieur de fichier
//   le check à chaque buffer => abandon en plein milieu d'un fichier possible.
public sealed class JobController : IDisposable
{
    private readonly object _lock = new();
    private readonly HashSet<PauseReason> _reasons = new();
    private readonly ManualResetEventSlim _signal = new(initialState: true);
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken StopToken => _cts.Token;
    public bool IsStopRequested => _cts.IsCancellationRequested;
    public bool IsPaused => !_signal.IsSet;

    public IReadOnlyCollection<PauseReason> ActiveReasons
    {
        get { lock (_lock) return _reasons.ToArray(); }
    }

    // Levé quand la 1re cause apparaît (transition non-pausé -> pausé).
    public event Action<PauseReason>? Paused;
    // Levé quand la dernière cause disparaît (transition pausé -> non-pausé).
    public event Action<PauseReason>? Resumed;

    public void AddReason(PauseReason reason)
    {
        lock (_lock)
        {
            bool wasEmpty = _reasons.Count == 0;
            if (!_reasons.Add(reason)) return;
            _signal.Reset();
            if (wasEmpty) Paused?.Invoke(reason);
        }
    }

    public void RemoveReason(PauseReason reason)
    {
        lock (_lock)
        {
            if (!_reasons.Remove(reason)) return;
            if (_reasons.Count == 0)
            {
                _signal.Set();
                Resumed?.Invoke(reason);
            }
        }
    }

    // Bloque le thread tant qu'une cause de pause est active. NE LANCE PAS
    // d'exception : RequestStop appelle Set() pour débloquer ; le caller
    // doit vérifier IsStopRequested après l'appel pour sortir proprement.
    // Choix volontaire : éviter de propager OperationCanceledException depuis
    // le code utilisateur (le débogueur s'arrêterait dessus en mode Just My Code).
    public void WaitIfPaused()
    {
        if (_signal.IsSet) return;
        _signal.Wait();
    }

    public void RequestStop()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        // On débloque les threads en attente sur le signal pour qu'ils voient le cancel.
        _signal.Set();
    }

    public void Dispose()
    {
        _signal.Dispose();
        _cts.Dispose();
    }
}
