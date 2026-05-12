using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyLog;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private readonly PathService _paths;


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ObservableCollection<LogFileEntry> Files { get; } = new();
    public ObservableCollection<LogEntry> Entries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private LogFileEntry? selectedFile;

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string logDirectory = string.Empty;

    public bool HasSelection => SelectedFile is not null;

    public LogsViewModel(PathService paths)
    {
        _paths = paths;
        Refresh();
    }

    partial void OnSelectedFileChanged(LogFileEntry? value)
    {
        LoadEntries(value);
    }

    [RelayCommand]
    public void Refresh()
    {
        LogDirectory = _paths.GetLogDirectory();

        var previousPath = SelectedFile?.FullPath;
        Files.Clear();

        if (!Directory.Exists(LogDirectory))
        {
            StatusMessage = $"Dossier introuvable : {LogDirectory}";
            Entries.Clear();
            return;
        }

        try
        {
            var dir = new DirectoryInfo(LogDirectory);
            var files = dir.EnumerateFiles("*.*")
                .Where(f => f.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                         || f.Extension.Equals(".xml",  StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime);

            foreach (var f in files)
                Files.Add(new LogFileEntry(f));

            StatusMessage = Files.Count == 0
                ? "Aucun fichier de log trouvé."
                : $"{Files.Count} fichier(s) de log.";

            // Restore previous selection, else select first.
            SelectedFile = Files.FirstOrDefault(f => f.FullPath == previousPath) ?? Files.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur de lecture : {ex.Message}";
        }
    }

    private void LoadEntries(LogFileEntry? file)
    {
        Entries.Clear();
        if (file is null) return;

        try
        {
            if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                LoadJson(file.FullPath);
            else
                LoadXml(file.FullPath);

            StatusMessage = $"{Entries.Count} entrée(s) — {file.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur de lecture : {ex.Message}";
        }
    }

    private void LoadJson(string path)
    {
        // NDJSON: one JSON object per line.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line, JsonOptions);
                if (entry is not null) Entries.Add(entry);
            }
            catch
            {
                // Skip malformed lines.
            }
        }
    }

    private void LoadXml(string path)
    {
        // File is a sequence of <LogEntry> elements without a single root.
        // Wrap them in a synthetic root to use XDocument.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string content = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(content)) return;

        var wrapped = $"<Root>{content}</Root>";
        var doc = XDocument.Parse(wrapped);

        foreach (var node in doc.Root!.Elements("LogEntry"))
        {
            Entries.Add(new LogEntry
            {
                Timestamp           = ParseDate(node.Element("Timestamp")?.Value),
                JobId               = ParseInt(node.Element("JobId")?.Value),
                BackupName          = node.Element("BackupName")?.Value ?? string.Empty,
                Action              = ParseAction(node.Element("Action")?.Value),
                SourceFilePath      = node.Element("SourceFilePath")?.Value ?? string.Empty,
                DestinationFilePath = node.Element("DestinationFilePath")?.Value ?? string.Empty,
                FileSizeBytes       = ParseLong(node.Element("FileSizeBytes")?.Value),
                TransferTimeMs      = ParseLong(node.Element("TransferTimeMs")?.Value),
                CryptoTimeMs        = ParseLong(node.Element("CryptoTimeMs")?.Value),
                ThreadId            = ParseInt(node.Element("ThreadId")?.Value),
                MaxDegreeOfParallelism = ParseInt(node.Element("MaxDegreeOfParallelism")?.Value),
                ThreadsUsed         = ParseInt(node.Element("ThreadsUsed")?.Value),
                ChunkCount          = ParseInt(node.Element("ChunkCount")?.Value),
            });
        }
    }

    private static DateTime ParseDate(string? s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;

    private static int ParseInt(string? s) =>
        int.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static long ParseLong(string? s) =>
        long.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static LogAction ParseAction(string? s) =>
        Enum.TryParse<LogAction>(s, ignoreCase: true, out var a) ? a : LogAction.Create;

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            if (Directory.Exists(LogDirectory))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LogDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
        }
        catch { /* ignore */ }
    }
}

public class LogFileEntry
{
    public LogFileEntry(FileInfo info)
    {
        Name = info.Name;
        FullPath = info.FullName;
        Extension = info.Extension;
        SizeBytes = info.Length;
        LastWriteTime = info.LastWriteTime;
    }

    public string Name { get; }
    public string FullPath { get; }
    public string Extension { get; }
    public long SizeBytes { get; }
    public DateTime LastWriteTime { get; }

    public string SizeDisplay => SizeBytes switch
    {
        < 1024            => $"{SizeBytes} o",
        < 1024 * 1024     => $"{SizeBytes / 1024.0:0.#} Ko",
        < 1024 * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):0.#} Mo",
        _                 => $"{SizeBytes / (1024.0 * 1024 * 1024):0.#} Go"
    };
}
