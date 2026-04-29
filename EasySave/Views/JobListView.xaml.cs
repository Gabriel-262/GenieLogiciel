using System.Windows;
using System.Windows.Controls;
using EasySave.Resources;
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
                string.Format(Translator.Get("Error_MaxJobs"), AppConfig.MaxJobs),
                Translator.Get("UI_Cannot_AddJob"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            string.Format(Translator.Get("UI_Confirm_DeleteJob"), selected.Name, selected.Id),
            Translator.Get("UI_Confirm_DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        Vm.JobList.DeleteJobCommand.Execute(selected.Id);
    }

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not JobItemViewModel selected) return;
        _ = Vm.JobList.ExecuteJobCommand.ExecuteAsync(selected.Id);
    }

    private void ExecuteAll_Click(object sender, RoutedEventArgs e)
    {
        _ = Vm.JobList.ExecuteAllCommand.ExecuteAsync(null);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Vm.JobList.Refresh();
    }
}
