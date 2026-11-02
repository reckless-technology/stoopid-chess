using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// Text that only shows: one line centred down the view, or, wrapped, as many lines as it takes from the top.
/// </summary>
internal sealed class LabelView(string text) : View
{
    /// <summary>The text.</summary>
    public string Text { get; set; } = text;

    /// <summary>The font.</summary>
    public SKFont Font { get; init; } = Fonts.Body;

    /// <summary>The colour.</summary>
    public SKColor Color { get; init; } = Theme.Ink;

    /// <summary>Whether the text is broken into lines to fit the view's width.</summary>
    public bool IsWrapped { get; init; }

    /// <summary>
    /// The height the text takes at <paramref name="width"/>: one line, or the wrapped lines.
    /// </summary>
    public float HeightFor(float width) =>
        (IsWrapped ? Drawing.Wrap(Text, Font, width).Count : 1) * Drawing.LineHeight(Font);

    protected override void Draw(SKCanvas canvas)
    {
        if (!IsWrapped)
        {
            Drawing.Text(canvas, Text, LocalBounds, Font, Color);
            return;
        }
        float lineHeight = Drawing.LineHeight(Font);
        float y = 0;
        foreach (string line in Drawing.Wrap(Text, Font, Width))
        {
            Drawing.Text(canvas, line, SKRect.Create(0, y, Width, lineHeight), Font, Color);
            y += lineHeight;
        }
    }
}
