using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using EasySave.Protocol;
using EasySave.Services;

namespace EasySave.Server;

// Listener TCP du moteur EasySave.
//
// - Accept loop : pour chaque connexion entrante, crée une ClientSession qui
//   tourne sur sa propre Task.
// - Bridge events : s'abonne aux events de IBackupEngine et les rediffuse en
//   broadcast à toutes les sessions actives.
// - Pas d'auth (LAN de confiance, choix archi). Quiconque atteint le port a
//   un accès complet.
public sealed class TcpBackupServer : IAsyncDisposable
{
    private readonly IBackupEngine _engine;
    private readonly IJobRepository _repo;
    private readonly SettingsService _settings;
    private readonly IPAddress _bind;
    private readonly int _port;

    private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();
    private readonly CancellationTokenSource _cts = new();

    private TcpListener? _listener;
    private Task? _acceptLoop;

    public TcpBackupServer(
        IBackupEngine engine,
        IJobRepository repo,
        SettingsService settings,
        IPAddress? bind = null,
        int port = ProtocolConstants.DefaultPort)
    {
        _engine = engine;
        _repo = repo;
        _settings = settings;
        _bind = bind ?? IPAddress.Any;
        _port = port;
    }

    public IPEndPoint Endpoint => (IPEndPoint)(_listener?.LocalEndpoint ?? new IPEndPoint(_bind, _port));

    public void Start()
    {
        if (_listener is not null) return;

        _listener = new TcpListener(_bind, _port);
        _listener.Start();

        AttachEngineEvents();

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }

            client.NoDelay = true; // events de progression doivent partir vite
            var session = new ClientSession(client, _engine, _repo, _settings, OnSessionClosed, Broadcast);
            _sessions[session.Id] = session;
            session.Start();
        }
    }

    private void OnSessionClosed(ClientSession session)
    {
        _sessions.TryRemove(session.Id, out _);
    }

    // === Bridge events engine -> broadcast ===

    private void AttachEngineEvents()
    {
        _engine.ProgressChanged += OnProgress;
        _engine.JobStarted += OnJobStarted;
        _engine.JobCompleted += OnJobCompleted;
        _engine.JobStopped += OnJobStopped;
        _engine.JobPaused += OnJobPaused;
        _engine.JobResumed += OnJobResumed;
    }

    private void DetachEngineEvents()
    {
        _engine.ProgressChanged -= OnProgress;
        _engine.JobStarted -= OnJobStarted;
        _engine.JobCompleted -= OnJobCompleted;
        _engine.JobStopped -= OnJobStopped;
        _engine.JobPaused -= OnJobPaused;
        _engine.JobResumed -= OnJobResumed;
    }

    private void OnProgress(object? s, BackupProgressEventArgs e)
        => Broadcast(Envelope.Event(MessageTypes.EvtProgress, e.ToDto()));
    private void OnJobStarted(object? s, JobLifecycleEventArgs e)
        => Broadcast(Envelope.Event(MessageTypes.EvtJobStarted, e.ToDto()));
    private void OnJobCompleted(object? s, JobLifecycleEventArgs e)
        => Broadcast(Envelope.Event(MessageTypes.EvtJobCompleted, e.ToDto()));
    private void OnJobStopped(object? s, JobLifecycleEventArgs e)
        => Broadcast(Envelope.Event(MessageTypes.EvtJobStopped, e.ToDto()));
    private void OnJobPaused(object? s, JobLifecycleEventArgs e)
        => Broadcast(Envelope.Event(MessageTypes.EvtJobPaused, e.ToDto()));
    private void OnJobResumed(object? s, JobLifecycleEventArgs e)
        => Broadcast(Envelope.Event(MessageTypes.EvtJobResumed, e.ToDto()));

    // Broadcast best-effort : si une session échoue à recevoir, on la marque
    // simplement morte. La session se nettoie de _sessions via OnSessionClosed.
    public void Broadcast(Envelope envelope)
    {
        foreach (var s in _sessions.Values)
        {
            _ = s.SendAsync(envelope);
        }
    }

    public async ValueTask DisposeAsync()
    {
        DetachEngineEvents();
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch { }
        }
        foreach (var s in _sessions.Values) await s.DisposeAsync().ConfigureAwait(false);
        _sessions.Clear();
        _cts.Dispose();
    }
}
