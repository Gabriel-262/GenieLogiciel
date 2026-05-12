using System.Collections.Concurrent;
using System.Net.Sockets;
using EasySave.Protocol;

namespace EasySave.Client;

// Connexion TCP cliente vers EasySave.Server. Gère :
//  - le handshake (cmd.snapshot pour récupérer l'état initial)
//  - la corrélation commande ↔ réponse via CorrelationId
//  - le dispatch des events broadcast vers RemoteBackupEngine / RemoteJobRepository
//
// Stratégie de déconnexion (validée avec utilisateur) : pas de reconnexion auto.
// Si la socket tombe, on lève l'event Disconnected et le client (WPF/CLI) ferme.
public sealed class BackupServerConnection : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private NdjsonChannel? _channel;
    private CancellationTokenSource? _cts;
    private Task? _readLoop;

    // Corrélation correlationId -> TCS de la réponse attendue.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Envelope>> _pending = new();

    public event EventHandler? Disconnected;
    public event EventHandler<Envelope>? EventReceived;

    public bool IsConnected => _tcp.Connected;

    public async Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        await _tcp.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
        _tcp.NoDelay = true;
        _channel = new NdjsonChannel(_tcp.GetStream());
        _cts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Envelope? env;
                try { env = await _channel!.ReadAsync(ct).ConfigureAwait(false); }
                catch { break; }
                if (env is null) break;

                if (env.CorrelationId is { } corr && _pending.TryRemove(corr, out var tcs))
                {
                    tcs.TrySetResult(env);
                }
                else
                {
                    EventReceived?.Invoke(this, env);
                }
            }
        }
        finally
        {
            // Réveille toutes les commandes encore en attente : elles vont
            // observer une OperationCanceledException → l'appli peut décider
            // de fermer.
            foreach (var kv in _pending)
                kv.Value.TrySetCanceled();
            _pending.Clear();

            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<Envelope> SendCommandAsync(string type, object? payload, CancellationToken ct = default)
    {
        if (_channel is null) throw new InvalidOperationException("Non connecté.");
        var env = Envelope.Command(type, payload);
        var corr = env.CorrelationId!;

        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[corr] = tcs;

        try
        {
            await _channel.SendAsync(env, ct).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(corr, out _);
            throw;
        }

        using (ct.Register(() => { if (_pending.TryRemove(corr, out var t)) t.TrySetCanceled(); }))
        {
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        try { _tcp.Close(); } catch { }
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); } catch { }
        }
        _channel?.Dispose();
        _cts?.Dispose();
    }
}
