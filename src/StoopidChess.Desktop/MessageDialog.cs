using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace StoopidChess.Desktop;

/// <summary>
/// A minimal message box (Avalonia has none built in): a message and an OK button.
/// </summary>
internal static class MessageDialog
{
    public static Task ShowAsync(Window owner, string title, string message)
    {
        var okButton = new Button
        {
            Content = "OK",
            IsDefault = true,
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                MaxWidth = 520,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    okButton,
                },
            },
        };
        okButton.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(owner);
    }
}
