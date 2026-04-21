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
    }

    [ObservableProperty] private bool autoAssignJobId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LanguageDisplayName))]
    private string language;

    [ObservableProperty] private string backKey;

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
