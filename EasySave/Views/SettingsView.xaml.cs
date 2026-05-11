using System.Windows;
using System.Windows.Controls;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;

namespace EasySave.Views;

public partial class SettingsView : UserControl
{
    private bool _themeInitialized;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Reflect current theme without firing the SelectionChanged persist.
        _themeInitialized = false;
        ThemeCombo.SelectedValue = string.Equals(App.SettingsService.Current.Theme, "dark",
            System.StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        _themeInitialized = true;
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_themeInitialized) return;
        if (ThemeCombo.SelectedValue is not string tag) return;
        var theme = tag == "dark" ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.Apply(theme);
        App.SettingsService.Current.Theme = tag;
        App.SettingsService.Save();
    }

    private void SaveMaxJobs_Click(object sender, RoutedEventArgs e)
    {
        string raw = MaxJobsBox.Text?.Trim() ?? string.Empty;
        Vm.ChangeMaxJobsCommand.Execute(raw);

        if (int.TryParse(raw, out int n) && n > 0)
        {
            int existing = App.MainViewModel.JobList.Count;
            if (existing > n)
            {
                MessageBox.Show(
                    string.Format(Translator.Get("UI_Warn_MaxJobsExceeded"), existing, n),
                    Translator.Get("UI_Settings_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SavePaths_Click(object sender, RoutedEventArgs e)
    {
        Vm.ChangeLogPathCommand.Execute(Vm.LogPath);
        Vm.ChangeStatePathCommand.Execute(Vm.StatePath);
        Vm.ChangeConfigPathCommand.Execute(Vm.ConfigPath);
        Vm.ChangeLangPathCommand.Execute(Vm.LangPath);
        MessageBox.Show(Translator.Get("UI_Settings_PathsSaved"), Translator.Get("UI_Settings_Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
