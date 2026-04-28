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
