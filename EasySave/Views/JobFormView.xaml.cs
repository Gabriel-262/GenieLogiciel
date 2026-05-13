using System.Windows;
using System.Windows.Controls;
using EasySave.Services;
using EasySave.ViewModels;

namespace EasySave.Views;

public partial class JobFormView : UserControl
{
    public JobFormView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private JobFormViewModel Vm => (JobFormViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Mode distant : le sélecteur Windows ouvrirait le file system du client
        // alors que les chemins doivent exister côté serveur. On masque les "..."
        // et on affiche le bandeau d'avertissement à la place.
        bool remote = !App.IsLocalServer;
        RemotePathHint.Visibility    = remote ? Visibility.Visible : Visibility.Collapsed;
        BrowseSourceButton.Visibility = remote ? Visibility.Collapsed : Visibility.Visible;
        BrowseTargetButton.Visibility = remote ? Visibility.Collapsed : Visibility.Visible;
    }

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
