namespace EasySave.Services;

// Surveille le logiciel métier en tâche de fond, indépendamment de
// l'exécution des sauvegardes. Émet Started/Stopped sur transition.
//
// Différence avec ProcessMonitorService :
//   - ProcessMonitorService = check synchrone, à la demande (IsRunning()).
//   - BusinessSoftwareWatcher = boucle de polling avec état + events.
//
// L'engine s'abonne aux events pour ajouter/retirer la cause de pause
// "Business" sur tous les jobs actifs (cf. App.xaml.cs).
public sealed class BusinessSoftwareWatcher : IDisposable
{
    private readonly IBusinessSoftwareMonitor _monitor;
    private readonly Timer _timer;
    private readonly object _lock = new();
    private bool _isRunning;
    private bool _disposed;

    public bool IsRunning { get { lock (_lock) return _isRunning; } }

    public event EventHandler? Started;
    public event EventHandler? Stopped;

    public BusinessSoftwareWatcher(IBusinessSoftwareMonitor monitor, int intervalMs = 1000)
    {
        _monitor = monitor;
        _timer = new Timer(Tick, null, 0, intervalMs);
    }

    private void Tick(object? state)
    {
        if (_disposed) return;
        bool running;
        try { running = _monitor.IsRunning(); }
        catch { return; }

        bool transitionStarted = false;
        bool transitionStopped = false;
        lock (_lock)
        {
            if (running && !_isRunning) { _isRunning = true;  transitionStarted = true; }
            else if (!running && _isRunning) { _isRunning = false; transitionStopped = true; }
        }

        if (transitionStarted) Started?.Invoke(this, EventArgs.Empty);
        if (transitionStopped) Stopped?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
