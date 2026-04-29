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
    private void NavExecution_Click(object sender, RoutedEventArgs e) => ShowExecution();
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowJobs()
    {
        ContentHost.Content = new JobListView { DataContext = App.MainViewModel };
    }

    private void ShowExecution()
    {
        App.MainViewModel.MarkExecutionViewed();
        ContentHost.Content = new JobExecutionView { DataContext = App.MainViewModel.Execution };
    }

    private void ShowSettings()
    {
        ContentHost.Content = new SettingsView { DataContext = App.MainViewModel.Settings };
    }
}
