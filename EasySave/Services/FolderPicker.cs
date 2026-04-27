using Microsoft.Win32;

namespace EasySave.Views;

// Uses the WPF-native OpenFolderDialog (shipped in .NET 8 / WPF).
public static class FolderPicker
{
    public static string? Pick(string? initial = null)
    {
        var dlg = new OpenFolderDialog
        {
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(initial) && System.IO.Directory.Exists(initial))
            dlg.InitialDirectory = initial;

        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
