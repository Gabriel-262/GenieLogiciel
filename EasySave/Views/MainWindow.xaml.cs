using System.Windows;

namespace EasySave.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainViewModel;
        ShowJobs();
    }

    private void NavJobs_Click(object sender, RoutedEventArgs e) => ShowJobs();
    private void NavLogs_Click(object sender, RoutedEventArgs e) => ShowLogs();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowJobs()
    {
        ContentHost.Content = new JobListView { DataContext = App.MainViewModel };
    }

    private void ShowLogs()
    {
        App.MainViewModel.Logs.Refresh();
        ContentHost.Content = new LogsView { DataContext = App.MainViewModel.Logs };
    }

    private void ShowSettings()
    {
        ContentHost.Content = new SettingsView { DataContext = App.MainViewModel.Settings };
    }
}
