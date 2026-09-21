using Avalonia.Controls;
using Avalonia.Layout;

namespace StoopidChess.Desktop;

/// <summary>
/// Modal dialog for the common UCI options of a registered engine: Hash (MB) and Threads, zero
/// meaning the engine's own default. The result is in <see cref="Saved"/> once the dialog closes;
/// null means it was cancelled.
/// </summary>
internal sealed class EngineOptionsDialog : Window
{
    private readonly NumericUpDown hash = new() { Minimum = 0, Maximum = 4096, Increment = 16, FormatString = "0", Width = 120 };
    private readonly NumericUpDown threads = new() { Minimum = 0, Maximum = 64, Increment = 1, FormatString = "0", Width = 120 };

    public EngineOptionsDialog(string engineName, IReadOnlyDictionary<string, string> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        Title = $"{engineName} options";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        hash.Value = current.TryGetValue("Hash", out string? hashValue) && int.TryParse(hashValue, out int h) ? Math.Clamp(h, 0, 4096) : 0;
        threads.Value = current.TryGetValue("Threads", out string? threadsValue) && int.TryParse(threadsValue, out int t) ? Math.Clamp(t, 0, 64) : 0;

        var okButton = new Button { Content = "OK", IsDefault = true, MinWidth = 88 };
        okButton.Click += (_, _) =>
        {
            var options = new Dictionary<string, string>();
            foreach ((string key, string value) in current)
            {
                if (key is not ("Hash" or "Threads"))
                {
                    options[key] = value;   // Keep any options this dialog does not edit
                }
            }
            if ((int)(hash.Value ?? 0) is > 0 and int hashMb)
            {
                options["Hash"] = hashMb.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if ((int)(threads.Value ?? 0) is > 0 and int threadCount)
            {
                options["Threads"] = threadCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            Saved = options;
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
                new TextBlock { Text = "Zero leaves the engine's own default. Applied when the engine starts.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 320 },
                Row("Hash (MB):", hash),
                Row("Threads:", threads),
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

    public Dictionary<string, string>? Saved { get; private set; }

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
