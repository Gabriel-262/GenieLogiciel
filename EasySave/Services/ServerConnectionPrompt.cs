using System.Windows;
using System.Windows.Controls;

namespace EasySave.Services;

// Dialog minimal pour saisir l'IP/port du serveur EasySave quand 127.0.0.1
// ne répond pas. Construit en code (pas de XAML séparé) pour rester léger
// et éviter d'ajouter une fenêtre dans le ResourceDictionary.
public static class ServerConnectionPrompt
{
    public sealed record Result(string Host, int Port);

    public static Result? Ask(string? lastHost, int lastPort, string? errorMessage = null)
    {
        var host = new TextBox
        {
            Text = lastHost ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 8),
            MinWidth = 240
        };
        var port = new TextBox
        {
            Text = lastPort.ToString(),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var stack = new StackPanel { Margin = new Thickness(16) };
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            stack.Children.Add(new TextBlock
            {
                Text = errorMessage,
                Foreground = System.Windows.Media.Brushes.Crimson,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
        }
        stack.Children.Add(new TextBlock { Text = "IP du serveur :", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(host);
        stack.Children.Add(new TextBlock { Text = "Port :", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(port);

        var ok = new Button { Content = "Connexion", IsDefault = true, Width = 100, Margin = new Thickness(4) };
        var cancel = new Button { Content = "Quitter", IsCancel = true, Width = 100, Margin = new Thickness(4) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        var window = new Window
        {
            Title = "EasySave — Connexion au serveur",
            Content = stack,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        bool confirmed = false;
        ok.Click += (_, _) => { confirmed = true; window.Close(); };

        window.ShowDialog();

        if (!confirmed) return null;
        if (!int.TryParse(port.Text, out int p) || p <= 0 || p > 65535) p = lastPort;
        return new Result(host.Text.Trim(), p);
    }
}
