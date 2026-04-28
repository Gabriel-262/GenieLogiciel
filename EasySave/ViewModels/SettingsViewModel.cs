using System.Collections.ObjectModel;
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
        autoAssignJobId = settings.Current.AutoAssignJobId;
        language = settings.Current.Language;
        backKey = settings.Current.BackKey;
        logFormat = (settings.Current.LogFormat ?? "json").ToLowerInvariant();
        businessSoftwareName = settings.Current.BusinessSoftwareName;
        logPath = settings.Current.LogPath ?? string.Empty;
        statePath = settings.Current.StatePath ?? string.Empty;
        configPath = settings.Current.ConfigPath ?? string.Empty;
        langPath = settings.Current.LangPath ?? string.Empty;
        cryptoMode = string.IsNullOrEmpty(settings.Current.CryptoMode) ? "Rapide" : settings.Current.CryptoMode;
        EncryptedExtensions = new ObservableCollection<string>(settings.Current.EncryptedExtensions);
    }

    [ObservableProperty] private bool autoAssignJobId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageDisplayName))]
    private string language;

    [ObservableProperty] private string backKey;
    [ObservableProperty] private string logFormat;
    [ObservableProperty] private string businessSoftwareName;
    [ObservableProperty] private string logPath;
    [ObservableProperty] private string statePath;
    [ObservableProperty] private string configPath;
    [ObservableProperty] private string langPath;
    [ObservableProperty] private string cryptoMode;

    public ObservableCollection<string> EncryptedExtensions { get; }

    public string LanguageDisplayName => LanguageLabel(Language);

    [RelayCommand]
    private void ToggleAutoId()
    {
        AutoAssignJobId = !AutoAssignJobId;
        _settings.Current.AutoAssignJobId = AutoAssignJobId;
        _settings.Save();
    }

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

    [RelayCommand]
    private void ChangeBusinessSoftwareName(string name)
    {
        BusinessSoftwareName = name?.Trim() ?? string.Empty;
        _settings.Current.BusinessSoftwareName = BusinessSoftwareName;
        _settings.Save();
        AppConfig.Settings = _settings.Current;
    }

    [RelayCommand]
    private void ChangeLogPath(string path)    { LogPath = path;    _settings.Current.LogPath = Nullable(path);    _settings.Save(); AppConfig.Settings = _settings.Current; }
    [RelayCommand]
    private void ChangeStatePath(string path)  { StatePath = path;  _settings.Current.StatePath = Nullable(path);  _settings.Save(); AppConfig.Settings = _settings.Current; }
    [RelayCommand]
    private void ChangeConfigPath(string path) { ConfigPath = path; _settings.Current.ConfigPath = Nullable(path); _settings.Save(); AppConfig.Settings = _settings.Current; }
    [RelayCommand]
    private void ChangeLangPath(string path)   { LangPath = path;   _settings.Current.LangPath = Nullable(path);   _settings.Save(); AppConfig.Settings = _settings.Current; }

    [RelayCommand]
    private void ToggleCryptoMode()
    {
        CryptoMode = CryptoMode switch
        {
            "Rapide"   => "Standard",
            "Standard" => "Premium",
            _          => "Rapide"
        };
        _settings.Current.CryptoMode = CryptoMode;
        _settings.Save();
    }

    [RelayCommand(CanExecute = nameof(CanAddExtension))]
    private void AddExtension(string extension)
    {
        string normalized = NormalizeExtension(extension);
        if (string.IsNullOrEmpty(normalized)) return;
        if (EncryptedExtensions.Any(e => string.Equals(e, normalized, StringComparison.OrdinalIgnoreCase))) return;

        EncryptedExtensions.Add(normalized);
        _settings.Current.EncryptedExtensions = EncryptedExtensions.ToList();
        _settings.Save();
    }

    private bool CanAddExtension(string? extension) => !string.IsNullOrWhiteSpace(extension);

    [RelayCommand]
    private void RemoveExtension(string extension)
    {
        string normalized = NormalizeExtension(extension);
        var match = EncryptedExtensions.FirstOrDefault(e =>
            string.Equals(e, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;

        EncryptedExtensions.Remove(match);
        _settings.Current.EncryptedExtensions = EncryptedExtensions.ToList();
        _settings.Save();
    }

    private static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return string.Empty;
        ext = ext.Trim().ToLowerInvariant();
        return ext.StartsWith('.') ? ext : "." + ext;
    }

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
