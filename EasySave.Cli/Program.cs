using System.Text;
using EasySave;
using EasySave.Client;
using EasySave.Protocol;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;
using EasySave.Views;

Console.OutputEncoding = Encoding.UTF8;

var pathService     = new PathService();
var settingsService = new SettingsService(pathService);
pathService.Bind(settingsService);

Translator.Initialize(pathService.GetLangFilePath);
Translator.SetLanguage(settingsService.Current.Language);

// === Connexion au serveur EasySave ===
//   1) tente 127.0.0.1
//   2) sinon : utilise RemoteServerHost mémorisé
//   3) sinon : prompt console pour saisir IP/port
var session = await ConnectClientAsync(settingsService);
if (session is null)
{
    Console.Error.WriteLine("Connexion au serveur impossible. Abandon.");
    Environment.Exit(2);
    return;
}

// Sortie propre si le serveur tombe pendant l'utilisation.
session.Connection.Disconnected += (_, _) =>
{
    Console.Error.WriteLine("Connexion au serveur perdue. Sortie.");
    Environment.Exit(3);
};

if (args.Length > 0)
{
    var indices = CliParser.Parse(args[0]);
    if (indices.Count == 0)
    {
        Console.Error.WriteLine(Translator.Get("Error_InvalidArgs"));
        Environment.Exit(1);
        return;
    }
    var jobs = session.Repository.GetAllJobs();
    var ids = new List<int>();
    foreach (int index in indices)
    {
        if (index < 1 || index > jobs.Count) continue;
        ids.Add(jobs[index - 1].Id);
    }
    await session.Engine.ExecuteJobsAsync(ids);
    return;
}

var mainVm = new MainViewModel(session.Repository, session.Engine, settingsService, pathService);
var menu = new ConsoleMenu(mainVm);
menu.Run();

await session.DisposeAsync();

static async Task<RemoteBackupSession?> ConnectClientAsync(SettingsService settings)
{
    // 1. localhost
    var local = await RemoteBackupSession.TryConnectAsync(
        ProtocolConstants.DefaultLocalHost,
        ProtocolConstants.DefaultPort,
        TimeSpan.FromMilliseconds(500));
    if (local is not null) return local;

    string? host = settings.Current.RemoteServerHost;
    int port = settings.Current.RemoteServerPort ?? ProtocolConstants.DefaultPort;

    // 2. dernier hôte mémorisé
    if (!string.IsNullOrWhiteSpace(host))
    {
        var s = await RemoteBackupSession.TryConnectAsync(host, port, TimeSpan.FromSeconds(3));
        if (s is not null) return s;
    }

    // 3. prompt
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("Aucun serveur EasySave détecté en local.");
        Console.Write($"IP du serveur [{host ?? "?"}] : ");
        var inHost = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(inHost)) inHost = host;
        if (string.IsNullOrWhiteSpace(inHost)) return null;

        Console.Write($"Port [{port}] : ");
        var inPort = Console.ReadLine()?.Trim();
        if (!int.TryParse(inPort, out int p) || p <= 0 || p > 65535) p = port;

        var s = await RemoteBackupSession.TryConnectAsync(inHost, p, TimeSpan.FromSeconds(3));
        if (s is not null)
        {
            settings.Current.RemoteServerHost = inHost;
            settings.Current.RemoteServerPort = p;
            settings.Save();
            return s;
        }
        Console.Error.WriteLine($"Impossible de joindre {inHost}:{p}.");
        host = inHost;
        port = p;
    }
}
