using System.Text.Json;

namespace EasySave.Resources;

public static class Translator
{
    private static Dictionary<string, string> _current = new();
    private static Dictionary<string, string> _fallback = new();
    private static Func<string, string>? _pathResolver;

    public static event Action? LanguageChanged;

    public static IReadOnlyDictionary<string, string> Strings => _current;
    public static IReadOnlyDictionary<string, string> FallbackStrings => _fallback;

    public static void Initialize(Func<string, string> langFilePathFor)
    {
        _pathResolver = langFilePathFor;
        _fallback = LoadFile(AppConfig.DefaultLanguage);
        if (_current.Count == 0) _current = _fallback;
    }

    public static void SetLanguage(string code)
    {
        if (_pathResolver is null) return;
        _current = LoadFile(code);
        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out string? v)) return v;
        if (_fallback.TryGetValue(key, out string? f)) return f;
        return key;
    }

    private static Dictionary<string, string> LoadFile(string code)
    {
        if (_pathResolver is null) return new Dictionary<string, string>();
        string path = _pathResolver(code);
        if (!File.Exists(path)) return new Dictionary<string, string>();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
