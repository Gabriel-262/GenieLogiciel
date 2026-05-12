using System.Net;
using EasyLog;
using EasySave;
using EasySave.Protocol;
using EasySave.Server;
using EasySave.Services;

// =====================================================================
// Composition root du serveur EasySave (équivalent headless de App.xaml.cs).
// Aucune UI : tout ce qui était dans App.xaml.cs (Settings/Path/Repo/Engine/
// CryptoDispatcher/BusinessSoftwareWatcher) vit ici.
// =====================================================================

int port = ProtocolConstants.DefaultPort;
IPAddress bind = IPAddress.Any;

// Args : --port <n>  --bind <ip>
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) { port = p; i++; }
    else if (args[i] == "--bind" && i + 1 < args.Length && IPAddress.TryParse(args[i + 1], out var ip)) { bind = ip; i++; }
}

var pathService     = new PathService();
var settingsService = new SettingsService(pathService);
pathService.Bind(settingsService);

var repo   = new JobRepository(pathService, settingsService);
ILogger logger = LoggerFactory.Create(settingsService.LogFormat, pathService.GetLogDirectory);
repo.SetLogger(logger);

var businessMonitor = new ProcessMonitorService(
    () => settingsService.Current.BusinessSoftwareName);

var crypto = new CryptoDispatcher(
    settingsService,
    new XorCryptoService(settingsService),
    new AesCryptoService(settingsService),
    new EciesCryptoService(settingsService));

var engine = new BackupEngine(repo, logger,
    businessMonitor: businessMonitor,
    crypto: crypto,
    settings: settingsService);

var businessWatcher = new BusinessSoftwareWatcher(businessMonitor, intervalMs: 1000);
businessWatcher.Started += (_, _) => engine.PauseAllForBusinessSoftware();
businessWatcher.Stopped += (_, _) => engine.ResumeAllAfterBusinessSoftware();

var server = new TcpBackupServer(engine, repo, settingsService, bind, port);
server.Start();

Console.WriteLine($"[EasySave.Server] Démarré sur {server.Endpoint}. Ctrl+C pour arrêter.");

var shutdown = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.TrySetResult();

await shutdown.Task;

Console.WriteLine("[EasySave.Server] Arrêt en cours…");
businessWatcher.Dispose();
await server.DisposeAsync();
Console.WriteLine("[EasySave.Server] Arrêté.");
