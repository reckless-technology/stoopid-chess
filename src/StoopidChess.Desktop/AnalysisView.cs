using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop;

/// <summary>
/// The Analysis window's content: the engine's thinking, one line per "info" report with depth, score, nodes and
/// principal variation, in the monospaced face, the latest at the foot; Clear empties it. It keeps the most recent
/// <see cref="MaximumLines"/> lines.
/// </summary>
internal sealed class AnalysisView : View
{
    /// <summary>The most lines kept.</summary>
    public const int MaximumLines = 400;

    private const float Margin = 6.0f;

    private readonly List<string> lines = [];

    public AnalysisView()
    {
        ClearButton.Clicked += Clear;
        AddChild(Output);
        AddChild(ClearButton);
    }

    /// <summary>The lines.</summary>
    public TextListView Output { get; } = new();

    /// <summary>The button that empties it.</summary>
    public PlateButton ClearButton { get; } = new("Clear");

    /// <summary>
    /// Add a line at the foot, and scroll to it.
    /// </summary>
    public void Append(string line)
    {
        lines.Add(line);
        if (lines.Count > MaximumLines)
        {
            lines.RemoveRange(0, lines.Count - MaximumLines);
        }
        Output.SetLines([.. lines]);
        Output.ScrollToEnd();
    }

    /// <summary>
    /// Empty it.
    /// </summary>
    public void Clear()
    {
        lines.Clear();
        Output.SetLines([]);
    }

    protected override void Layout()
    {
        float buttonWidth = 96;
        Output.SetBounds(new SKRect(Margin, Margin, Width - Margin, Height - Margin - DialogView.ButtonHeight - Margin));
        ClearButton.SetBounds(SKRect.Create(Width - Margin - buttonWidth, Height - Margin - DialogView.ButtonHeight,
            buttonWidth, DialogView.ButtonHeight));
    }
}
