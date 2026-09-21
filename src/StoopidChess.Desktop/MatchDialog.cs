using Avalonia.Controls;
using Avalonia.Layout;

namespace StoopidChess.Desktop;

/// <summary>
/// Modal dialog choosing the two engines of an engine match. The selection is in
/// <see cref="Choice"/> once the dialog closes (indices into the list of names the dialog was
/// given); null means it was cancelled.
/// </summary>
internal sealed class MatchDialog : Window
{
    private readonly ComboBox white = new() { MinWidth = 220 };
    private readonly ComboBox black = new() { MinWidth = 220 };

    public MatchDialog(IReadOnlyList<string> engineNames)
    {
        ArgumentNullException.ThrowIfNull(engineNames);
        Title = "Engine match";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        white.ItemsSource = engineNames;
        black.ItemsSource = engineNames;
        white.SelectedIndex = 0;
        black.SelectedIndex = engineNames.Count > 1 ? 1 : 0;

        var startButton = new Button { Content = "Start match", IsDefault = true, MinWidth = 100 };
        startButton.Click += (_, _) =>
        {
            if (white.SelectedIndex >= 0 && black.SelectedIndex >= 0)
            {
                Choice = (white.SelectedIndex, black.SelectedIndex);
            }
            Close();
        };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        cancelButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                Row("White:", white),
                Row("Black:", black),
                new TextBlock { Text = "The match plays under the chosen time control (or by level when untimed)." },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { startButton, cancelButton },
                },
            },
        };
    }

    public (int White, int Black)? Choice { get; private set; }

    private static StackPanel Row(string label, Control input)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MinWidth = 60 },
                input,
            },
        };
    }
}
