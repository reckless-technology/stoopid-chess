using Avalonia.Controls;
using Avalonia.Layout;
using StoopidChess.Play;

namespace StoopidChess.Desktop;

/// <summary>
/// Modal dialog asking for a base time in minutes and a Fischer increment in seconds. The chosen
/// control is in <see cref="Choice"/> once the dialog closes; null means it was cancelled.
/// </summary>
internal sealed class CustomTimeControlDialog : Window
{
    private readonly NumericUpDown minutes = new() { Minimum = 1, Maximum = 180, Increment = 1, FormatString = "0", Width = 120 };
    private readonly NumericUpDown increment = new() { Minimum = 0, Maximum = 120, Increment = 1, FormatString = "0", Width = 120 };

    public CustomTimeControlDialog(TimeControl? current)
    {
        Title = "Custom time control";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        minutes.Value = (decimal)Math.Clamp(Math.Round(current?.Base.TotalMinutes ?? 5), 1, 180);
        increment.Value = (decimal)Math.Clamp(Math.Round(current?.Increment.TotalSeconds ?? 3), 0, 120);

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 88 };
        okButton.Click += (_, _) =>
        {
            Choice = TimeControl.OfMinutes((int)(minutes.Value ?? 5), (int)(increment.Value ?? 0));
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
                Row("Minutes per side:", minutes),
                Row("Increment (seconds):", increment),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };
    }

    public TimeControl? Choice { get; private set; }

    private static StackPanel Row(string label, Control input)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 },
                input,
            },
        };
    }
}
