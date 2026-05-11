using EasySave.Protocol;

namespace EasySave.Client;

// Façade qui regroupe la connexion + RemoteBackupEngine + RemoteJobRepository
// et fait l'amorçage : connecte, demande cmd.snapshot, alimente les caches.
//
// Stratégie de découverte (validée avec utilisateur) :
//   1. tente 127.0.0.1 avec timeout court
//   2. si échec : laisse l'appelant réessayer avec une IP fournie par l'utilisateur
public sealed class RemoteBackupSession : IAsyncDisposable
{
    public BackupServerConnection Connection { get; }
    public RemoteBackupEngine Engine { get; }
    public RemoteJobRepository Repository { get; }
    public SnapshotPayload Snapshot { get; private set; } = new();

    private RemoteBackupSession(BackupServerConnection conn)
    {
        Connection = conn;
        Engine = new RemoteBackupEngine(conn);
        Repository = new RemoteJobRepository(conn);
    }

    public static async Task<RemoteBackupSession> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        var conn = new BackupServerConnection();
        await conn.ConnectAsync(host, port, timeout, ct).ConfigureAwait(false);

        var session = new RemoteBackupSession(conn);
        await session.RefreshSnapshotAsync(ct).ConfigureAwait(false);
        return session;
    }

    public static async Task<RemoteBackupSession?> TryConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        try { return await ConnectAsync(host, port, timeout, ct).ConfigureAwait(false); }
        catch { return null; }
    }

    public async Task RefreshSnapshotAsync(CancellationToken ct = default)
    {
        var rsp = await Connection.SendCommandAsync(MessageTypes.CmdGetSnapshot, null, ct).ConfigureAwait(false);
        var payload = rsp.TryDecode<SnapshotPayload>()
            ?? throw new InvalidOperationException("Snapshot invalide.");
        Snapshot = payload;
        Repository.ApplySnapshot(payload);
        await Engine.ApplySnapshotAsync(payload).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
