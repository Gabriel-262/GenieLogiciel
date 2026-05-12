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

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Vm.CanSave) return;
        // Attendre que l'ajout/màj (potentiellement distant) soit réellement
        // terminé avant de fermer la fenêtre, sinon le Refresh de la liste
        // s'exécute avant que le job n'existe côté repository.
        await Vm.SaveCommand.ExecuteAsync(null);
        Window.GetWindow(this)?.Close();
    }
}
