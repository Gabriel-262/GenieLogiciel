using System.Net.Sockets;
using EasySave.Models;
using EasySave.Protocol;
using EasySave.Services;

namespace EasySave.Server;

// Gère UNE connexion TCP cliente. Lit les enveloppes en boucle, dispatche les
// commandes vers IBackupEngine / IJobRepository, renvoie une réponse corrélée.
//
// Tolérante aux exceptions par commande : une commande qui throw renvoie
// rsp.error mais ne tue pas la session (sauf si la socket elle-même tombe).
internal sealed class ClientSession : IAsyncDisposable
{
    public Guid Id { get; } = Guid.NewGuid();

    private readonly TcpClient _client;
    private readonly IBackupEngine _engine;
    private readonly IJobRepository _repo;
    private readonly SettingsService _settings;
    private readonly Action<ClientSession> _onClosed;
    private readonly Action<Envelope> _broadcast;
    private readonly NdjsonChannel _channel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readLoop;

    public ClientSession(
        TcpClient client,
        IBackupEngine engine,
        IJobRepository repo,
        SettingsService settings,
        Action<ClientSession> onClosed,
        Action<Envelope> broadcast)
    {
        _client = client;
        _engine = engine;
        _repo = repo;
        _settings = settings;
        _onClosed = onClosed;
        _broadcast = broadcast;
        _channel = new NdjsonChannel(client.GetStream());
    }

    public void Start()
    {
        _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public Task SendAsync(Envelope envelope)
    {
        return _channel.SendAsync(envelope, _cts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Envelope? env;
                try { env = await _channel.ReadAsync(ct).ConfigureAwait(false); }
                catch { break; }
                if (env is null) break; // EOF / déconnexion

                Envelope response;
                try
                {
                    response = HandleCommand(env);
                }
                catch (Exception ex)
                {
                    response = Envelope.Response(MessageTypes.RspError, env.CorrelationId ?? string.Empty,
                        new ErrorPayload { Code = ErrorCodes.Internal, Message = ex.Message });
                }

                try { await _channel.SendAsync(response, ct).ConfigureAwait(false); }
                catch { break; }
            }
        }
        finally
        {
            _onClosed(this);
            try { _client.Close(); } catch { }
        }
    }

    private Envelope HandleCommand(Envelope env)
    {
        var corr = env.CorrelationId ?? string.Empty;

        switch (env.Type)
        {
            case MessageTypes.CmdGetSnapshot:
            {
                var jobs = _repo.GetAllJobs().Select(j => j.ToDto()).ToList();
                var payload = new SnapshotPayload
                {
                    Jobs = jobs,
                    Settings = _settings.Current.ToServerDto(),
                    ActiveJobIds = _engine.ActiveJobIds.ToList()
                };
                return Envelope.Response(MessageTypes.RspSnapshot, corr, payload);
            }

            case MessageTypes.CmdGetJobs:
            {
                var jobs = _repo.GetAllJobs().Select(j => j.ToDto()).ToList();
                return Envelope.Response(MessageTypes.RspJobs, corr, new JobsPayload { Jobs = jobs });
            }

            case MessageTypes.CmdAddJob:
            {
                var p = env.TryDecode<AddJobPayload>() ?? throw InvalidPayload();
                var added = _repo.AddJob(p.Job.FromDto());
                BroadcastJobsChanged();
                return Envelope.Response(MessageTypes.RspJob, corr, new JobPayload { Job = added.ToDto() });
            }

            case MessageTypes.CmdUpdateJob:
            {
                var p = env.TryDecode<UpdateJobPayload>() ?? throw InvalidPayload();
                if (!_repo.UpdateJob(p.Id, p.Job.FromDto()))
                    return Error(corr, ErrorCodes.JobNotFound, $"Job {p.Id} introuvable");
                BroadcastJobsChanged();
                return Ok(corr);
            }

            case MessageTypes.CmdDeleteJob:
            {
                var p = env.TryDecode<JobIdPayload>() ?? throw InvalidPayload();
                if (!_repo.DeleteJob(p.JobId))
                    return Error(corr, ErrorCodes.JobNotFound, $"Job {p.JobId} introuvable");
                BroadcastJobsChanged();
                return Ok(corr);
            }


            case MessageTypes.CmdRunJobs:
            {
                var p = env.TryDecode<RunJobsPayload>() ?? throw InvalidPayload();
                // Fire-and-forget : on n'attend pas la fin (sinon on bloque la
                // boucle de lecture pendant toute la sauvegarde). Les events
                // de progression suffiront au client à suivre.
                _ = _engine.ExecuteJobsAsync(p.JobIds);
                return Ok(corr);
            }

            case MessageTypes.CmdPauseJob:
                return SimpleJobAction(env, corr, _engine.Pause);
            case MessageTypes.CmdResumeJob:
                return SimpleJobAction(env, corr, _engine.Resume);
            case MessageTypes.CmdStopJob:
                return SimpleJobAction(env, corr, _engine.Stop);

            case MessageTypes.CmdPauseAll:  _engine.PauseAll();  return Ok(corr);
            case MessageTypes.CmdResumeAll: _engine.ResumeAll(); return Ok(corr);
            case MessageTypes.CmdStopAll:   _engine.StopAll();   return Ok(corr);

            case MessageTypes.CmdGetSettings:
                return Envelope.Response(MessageTypes.RspSettings, corr,
                    new SettingsPayload { Settings = _settings.Current.ToServerDto() });

            case MessageTypes.CmdUpdateSettings:
            {
                var p = env.TryDecode<UpdateSettingsPayload>() ?? throw InvalidPayload();
                _settings.Current.ApplyServerDto(p.Settings);
                _settings.Save();
                _broadcast(Envelope.Event(MessageTypes.EvtSettingsChanged,
                    new SettingsPayload { Settings = _settings.Current.ToServerDto() }));
                return Ok(corr);
            }

            default:
                return Error(corr, ErrorCodes.UnknownCommand, $"Type de message inconnu : {env.Type}");
        }
    }

    private Envelope SimpleJobAction(Envelope env, string corr, Action<int> action)
    {
        var p = env.TryDecode<JobIdPayload>() ?? throw InvalidPayload();
        action(p.JobId);
        return Ok(corr);
    }

    private void BroadcastJobsChanged()
    {
        var jobs = _repo.GetAllJobs().Select(j => j.ToDto()).ToList();
        _broadcast(Envelope.Event(MessageTypes.EvtJobsChanged, new JobsPayload { Jobs = jobs }));
    }

    private static Envelope Ok(string corr)
        => Envelope.Response(MessageTypes.RspOk, corr, null);

    private static Envelope Error(string corr, string code, string message)
        => Envelope.Response(MessageTypes.RspError, corr, new ErrorPayload { Code = code, Message = message });

    private static InvalidOperationException InvalidPayload()
        => new("Payload invalide pour la commande reçue.");

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _client.Close(); } catch { }
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); } catch { }
        }
        _channel.Dispose();
        _cts.Dispose();
    }
}
