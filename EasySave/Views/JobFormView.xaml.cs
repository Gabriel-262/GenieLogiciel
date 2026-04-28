using System.Windows;
using System.Windows.Controls;
using EasySave.ViewModels;

namespace EasySave.Views;

public partial class JobFormView : UserControl
{
    public JobFormView()
    {
        InitializeComponent();
    }

    private JobFormViewModel Vm => (JobFormViewModel)DataContext;

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        string? path = FolderPicker.Pick();
        if (path is not null) Vm.SourcePath = path;
    }

    private void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        string? path = FolderPicker.Pick();
        if (path is not null) Vm.TargetPath = path;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // SaveCommand has already run via Button.Command if CanSave is true.
        if (Vm.CanSave) Window.GetWindow(this)?.Close();
    }
}
