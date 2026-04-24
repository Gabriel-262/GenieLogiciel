using System.Text.Json;
using EasySave.Models;

namespace EasySave.Services;

public class SettingsService
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

    public void Save()
    {
        File.WriteAllText(
            _paths.GetSettingsFilePath(),
            JsonSerializer.Serialize(_settings, JsonOptions));
    }

    private AppSettings LoadFromDisk()
    {
        string path = _paths.GetSettingsFilePath();
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
