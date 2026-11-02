using SkiaSharp;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop;

/// <summary>
/// One reading in the status bar: its caption, and its value, a clock's in the monospaced face and, while it runs,
/// in the accent.
/// </summary>
internal sealed record StatusSegment(string Caption, string Value, bool IsClock = false, bool IsRunning = false);

/// <summary>
/// The status bar along the window's foot, Amphitheater's: the darkest strip under a rule, each reading's caption in
/// the muted ink and its value in the ink, left to right, and the clocks at the right end.
/// </summary>
internal sealed class StatusBarView : View
{
    /// <summary>The bar's height.</summary>
    public const float BarHeight = 28.0f;

    private const float Gap = 18.0f;
    private const float CaptionGap = 6.0f;
    private const float Inset = 10.0f;

    private IReadOnlyList<StatusSegment> segments = [];

    /// <summary>The readings shown.</summary>
    public IReadOnlyList<StatusSegment> Segments
    {
        get => segments;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!segments.SequenceEqual(value))
            {
                segments = value;
                Invalidate();
            }
        }
    }

    protected override void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint { Color = Theme.TitleBar };
        canvas.DrawRect(LocalBounds, paint);
        paint.Color = Theme.Rule;
        canvas.DrawRect(SKRect.Create(0, 0, Width, 1), paint);
        // The clocks at the right end, where a long reading at the left cannot push them out of the window
        float right = Width - Inset;
        foreach (StatusSegment clock in segments.Where(segment => segment.IsClock).Reverse())
        {
            right = DrawSegment(canvas, clock, right - SegmentWidth(clock), Width - Inset) - SegmentWidth(clock) - Gap;
        }
        float x = Inset;
        foreach (StatusSegment segment in segments.Where(segment => !segment.IsClock))
        {
            if (x >= right)
            {
                break;
            }
            x = DrawSegment(canvas, segment, x, right) + Gap;
        }
    }

    private static SKFont ValueFont(StatusSegment segment) => segment.IsClock ? Fonts.Clock : Fonts.SmallBold;

    private static float SegmentWidth(StatusSegment segment) =>
        Drawing.Measure(Fonts.Small, segment.Caption) + CaptionGap + Drawing.Measure(ValueFont(segment), segment.Value);

    /// <summary>
    /// A reading from <paramref name="x"/>, cut at <paramref name="limit"/>; returns where it ends.
    /// </summary>
    private float DrawSegment(SKCanvas canvas, StatusSegment segment, float x, float limit)
    {
        Drawing.Text(canvas, segment.Caption, new SKRect(x, 1, limit, Height), Fonts.Small, Theme.Muted);
        float valueStart = x + Drawing.Measure(Fonts.Small, segment.Caption) + CaptionGap;
        if (valueStart < limit)
        {
            Drawing.Text(canvas, segment.Value, new SKRect(valueStart, 1, limit, Height), ValueFont(segment),
                segment.IsRunning ? Theme.Amber : Theme.Ink);
        }
        return x + SegmentWidth(segment);
    }
}
