using Avalonia.Controls;
using Avalonia.Layout;

namespace StoopidChess.Desktop;

/// <summary>
/// A non-modal window showing the engine's thinking: one line per "info" report with depth, score,
/// nodes and principal variation. The window keeps the most recent lines; Clear empties it.
/// </summary>
internal sealed class AnalysisWindow : Window
{
    private const int MaxLines = 400;

    private readonly TextBox output = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
        FontFamily = new Avalonia.Media.FontFamily("Consolas,DejaVu Sans Mono,Menlo,monospace"),
    };

    private readonly List<string> lines = [];

    public AnalysisWindow()
    {
        Title = "Analysis";
        Width = 720;
        Height = 400;
        ShowInTaskbar = true;
        var clearButton = new Button { Content = "Clear", Margin = new Avalonia.Thickness(4), HorizontalAlignment = HorizontalAlignment.Right };
        clearButton.Click += (_, _) => Clear();
        var layout = new DockPanel();
        DockPanel.SetDock(clearButton, Dock.Bottom);
        layout.Children.Add(clearButton);
        layout.Children.Add(output);
        Content = layout;
    }

    public void Append(string line)
    {
        lines.Add(line);
        if (lines.Count > MaxLines)
        {
            lines.RemoveRange(0, lines.Count - MaxLines);
        }
        output.Text = string.Join('\n', lines);
        output.CaretIndex = output.Text?.Length ?? 0;
    }

    public void Clear()
    {
        lines.Clear();
        output.Text = "";
    }
}
