using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasySave.Resources;
using EasySave.Services;

namespace EasySave.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        language = settings.Current.Language;
        backKey = settings.Current.BackKey;
        logFormat = (settings.Current.LogFormat ?? "json").ToLowerInvariant();
        maxJobs = settings.Current.MaxJobs ?? AppConfig.MaxJobs;
        logPath = settings.Current.LogPath ?? string.Empty;
        statePath = settings.Current.StatePath ?? string.Empty;
        configPath = settings.Current.ConfigPath ?? string.Empty;
        langPath = settings.Current.LangPath ?? string.Empty;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageDisplayName))]
    private string language;

    [ObservableProperty] private string backKey;
    [ObservableProperty] private string logFormat;
    [ObservableProperty] private int maxJobs;
    [ObservableProperty] private string logPath;
    [ObservableProperty] private string statePath;
    [ObservableProperty] private string configPath;
    [ObservableProperty] private string langPath;

    public string LanguageDisplayName => LanguageLabel(Language);

    [RelayCommand(CanExecute = nameof(CanChangeLanguage))]
    private void ChangeLanguage(string code)
    {
        Language = code;
        _settings.Current.Language = code;
        _settings.Save();
        Translator.SetLanguage(code);
    }

    private bool CanChangeLanguage(string? code) => IsSupportedLanguage(code);

    [RelayCommand(CanExecute = nameof(CanChangeBackKey))]
    private void ChangeBackKey(string key)
    {
        BackKey = key.ToLowerInvariant();
        _settings.Current.BackKey = BackKey;
        _settings.Save();
    }

    private bool CanChangeBackKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.Length == 1 && char.IsLetter(key[0]);

    [RelayCommand]
    private void ToggleLogFormat()
    {
        LogFormat = LogFormat == "xml" ? "json" : "xml";
        _settings.Current.LogFormat = LogFormat;
        _settings.Save();
        AppConfig.Settings = _settings.Current;
    }

    [RelayCommand(CanExecute = nameof(CanChangeMaxJobs))]
    private void ChangeMaxJobs(int value)
    {
        MaxJobs = value;
        _settings.Current.MaxJobs = value;
        _settings.Save();
        AppConfig.Settings = _settings.Current;
    }

    private bool CanChangeMaxJobs(int value) => value > 0;

    [RelayCommand]
    private void ChangeLogPath(string path)    { LogPath = path;    _settings.Current.LogPath = Nullable(path);    _settings.Save(); AppConfig.Settings = _settings.Current; }
    [RelayCommand]
    private void ChangeStatePath(string path)  { StatePath = path;  _settings.Current.StatePath = Nullable(path);  _settings.Save(); AppConfig.Settings = _settings.Current; }
    [RelayCommand]
    private void ChangeConfigPath(string path) { ConfigPath = path; _settings.Current.ConfigPath = Nullable(path); _settings.Save(); AppConfig.Settings = _settings.Current; }
    [RelayCommand]
    private void ChangeLangPath(string path)   { LangPath = path;   _settings.Current.LangPath = Nullable(path);   _settings.Save(); AppConfig.Settings = _settings.Current; }

    private static string? Nullable(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public bool IsBackInput(string? input) =>
        !string.IsNullOrEmpty(input) &&
        input.Trim().Equals(BackKey, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedLanguage(string? code) =>
        code is "en" or "fr" or "zh" or "he";

    public static string LanguageLabel(string code) => code switch
    {
        "en" => "English",
        "fr" => "Français",
        "zh" => "中文",
        "he" => "עברית",
        _    => code
    };
}
