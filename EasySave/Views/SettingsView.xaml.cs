using System.Windows;
using System.Windows.Controls;
using EasySave.Resources;
using EasySave.Services;
using EasySave.ViewModels;

namespace EasySave.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string code)
            Vm.ChangeLanguageCommand.Execute(code);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        var theme = tag == "dark" ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.Apply(theme);
        App.SettingsService.Current.Theme = tag;
        App.SettingsService.Save();
    }

    private void SaveMaxJobs_Click(object sender, RoutedEventArgs e)
    {
        string raw = MaxJobsBox.Text?.Trim() ?? string.Empty;
        Vm.ChangeMaxJobsCommand.Execute(raw);

        // Avertit l'utilisateur s'il vient de fixer une limite inférieure au
        // nombre de jobs déjà existants : on ne supprime rien, on l'informe.
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
