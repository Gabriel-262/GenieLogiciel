using System.Text.Json;
using EasyLog;
using EasySave.Models;

namespace EasySave.Services;

public class SettingsService : ISettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly PathService _paths;
    private AppSettings _settings;

    public SettingsService(PathService paths)
    {
        _paths = paths;
        _settings = LoadFromDisk();
    }

    public AppSettings Current => _settings;

    // Valeurs dérivées (anciennement sur AppConfig). On centralise ici pour
    // qu'il n'y ait plus qu'une seule source de vérité injectable.
    public int MaxJobs => AppConfig.ResolveMaxJobs(_settings.MaxJobs);
    public LogFormat LogFormat => AppConfig.ResolveLogFormat(_settings.LogFormat);

    public void Save() => WriteToDisk(_settings, _paths.GetSettingsFilePath());

    private AppSettings LoadFromDisk()
    {
        string path = _paths.GetSettingsFilePath();
        if (!File.Exists(path))
        {
            var fresh = new AppSettings();
            WriteToDisk(fresh, path);
            return fresh;
        }

        string rawOnDisk;
        AppSettings loaded;
        try
        {
            rawOnDisk = File.ReadAllText(path);
            loaded = JsonSerializer.Deserialize<AppSettings>(rawOnDisk) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }

        // Migration: si le fichier sur disque ne contient pas toutes les clés
        // courantes du modèle, on le réécrit pour exposer les nouvelles valeurs par défaut.
        string canonical = JsonSerializer.Serialize(loaded, JsonOptions);
        if (!JsonEquivalent(rawOnDisk, canonical))
            File.WriteAllText(path, canonical);

        return loaded;
    }

    private static void WriteToDisk(AppSettings settings, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static bool JsonEquivalent(string a, string b)
    {
        try
        {
            using var da = JsonDocument.Parse(a);
            using var db = JsonDocument.Parse(b);
            return JsonElementEquals(da.RootElement, db.RootElement);
        }
        catch { return false; }
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                if (aProps.Count != bProps.Count) return false;
                foreach (var kv in aProps)
                    if (!bProps.TryGetValue(kv.Key, out var bv) || !JsonElementEquals(kv.Value, bv)) return false;
                return true;
            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength()) return false;
                var ae = a.EnumerateArray().GetEnumerator();
                var be = b.EnumerateArray().GetEnumerator();
                while (ae.MoveNext() && be.MoveNext())
                    if (!JsonElementEquals(ae.Current, be.Current)) return false;
                return true;
            default:
                return a.GetRawText() == b.GetRawText();
        }
    }
}
