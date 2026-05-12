using System.Linq;
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
                string.Format(Translator.Get("Error_MaxJobs"), Vm.JobList.MaxJobs),
                Translator.Get("UI_Cannot_AddJob"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Vm.JobForm.LoadForCreate();
        new JobFormWindow { DataContext = Vm.JobForm, Owner = Window.GetWindow(this) }.ShowDialog();
        Vm.JobList.Refresh();
    }

    private void ExecuteOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not int id) return;
        if (!EnsureWithinLimit(1)) return;
        _ = Vm.JobList.ExecuteJobCommand.ExecuteAsync(id);
    }

    private void EditOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not int id) return;
        var item = Vm.JobList.FindById(id);
        if (item is null) return;
        Vm.JobForm.LoadForEdit(item.ToModel());
        new JobFormWindow { DataContext = Vm.JobForm, Owner = Window.GetWindow(this) }.ShowDialog();
        Vm.JobList.Refresh();
    }

    private void DeleteOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not int id) return;
        var item = Vm.JobList.FindById(id);
        if (item is null) return;
        var confirm = MessageBox.Show(
            string.Format(Translator.Get("UI_Confirm_DeleteJob"), item.Name, item.Id),
            Translator.Get("UI_Confirm_DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        Vm.JobList.DeleteJobCommand.Execute(item.Id);
    }

    private void ExecuteSelection_Click(object sender, RoutedEventArgs e)
    {
        var ids = Vm.JobList.Jobs.Where(j => j.IsSelected).Select(j => j.Id).ToList();
        if (ids.Count == 0) return;
        if (!EnsureWithinLimit(ids.Count)) return;
        _ = Vm.JobList.ExecuteManyCommand.ExecuteAsync(ids);
    }

    private void ExecuteAll_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureWithinLimit(Vm.JobList.Count)) return;
        _ = Vm.JobList.ExecuteAllCommand.ExecuteAsync(null);
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var j in Vm.JobList.Jobs) j.IsSelected = true;
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var j in Vm.JobList.Jobs) j.IsSelected = false;
    }

    private bool EnsureWithinLimit(int requestedCount)
    {
        int max = Vm.JobList.MaxJobs;
        if (requestedCount <= max) return true;
        MessageBox.Show(
            string.Format(Translator.Get("UI_Warn_TooManyJobsToExecute"), requestedCount, max),
            Translator.Get("UI_Cannot_Execute"),
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Vm.JobList.Refresh();
    }
}
