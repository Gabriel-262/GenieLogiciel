using System.Windows;
using System.Windows.Controls;
using EasySave.ViewModels;

namespace EasySave.Views;

public partial class JobListView : UserControl
{
    public JobListView()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.JobList.IsFull)
        {
            MessageBox.Show(
                $"Maximum number of backup jobs ({AppConfig.MaxJobs}) reached.",
                "Cannot add job", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Vm.JobForm.LoadForCreate();
        new JobFormWindow { DataContext = Vm.JobForm, Owner = Window.GetWindow(this) }.ShowDialog();
        Vm.JobList.Refresh();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not JobItemViewModel selected) return;
        Vm.JobForm.LoadForEdit(selected.ToModel());
        new JobFormWindow { DataContext = Vm.JobForm, Owner = Window.GetWindow(this) }.ShowDialog();
        Vm.JobList.Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not JobItemViewModel selected) return;
        var confirm = MessageBox.Show(
            $"Delete job \"{selected.Name}\" (id {selected.Id})?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        Vm.JobList.DeleteJobCommand.Execute(selected.Id);
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not JobItemViewModel selected) return;
        await Vm.JobList.ExecuteJobCommand.ExecuteAsync(selected.Id);
    }

    private async void ExecuteAll_Click(object sender, RoutedEventArgs e)
    {
        await Vm.JobList.ExecuteAllCommand.ExecuteAsync(null);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Vm.JobList.Refresh();
    }
}
