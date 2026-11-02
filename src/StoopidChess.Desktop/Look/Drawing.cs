using SkiaSharp;

namespace StoopidChess.Desktop.Look;

/// <summary>
/// How a line of text sits in its box across the box's width.
/// </summary>
internal enum TextAlignment
{
    Left,
    Centre,
    Right,
}

/// <summary>
/// The drawing every view shares: text placed in a box, wrapped and fitted, and pictures drawn sharp.
/// </summary>
internal static class Drawing
{
    private const string Ellipsis = "…";

    /// <summary>
    /// The width <paramref name="text"/> takes in <paramref name="font"/>.
    /// </summary>
    public static float Measure(SKFont font, string text) => text.Length == 0 ? 0 : font.MeasureText(text);

    /// <summary>
    /// The height of one line of <paramref name="font"/>: its ascent, descent and leading.
    /// </summary>
    public static float LineHeight(SKFont font)
    {
        SKFontMetrics metrics = font.Metrics;
        return MathF.Ceiling(metrics.Descent - metrics.Ascent + metrics.Leading);
    }

    /// <summary>
    /// One line of text, centred down <paramref name="box"/> and aligned across it, cut with an ellipsis where it is
    /// wider than the box.
    /// </summary>
    public static void Text(SKCanvas canvas, string text, SKRect box, SKFont font, SKColor color,
        TextAlignment alignment = TextAlignment.Left)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(font);
        string shown = Fit(text, font, box.Width);
        if (shown.Length == 0)
        {
            return;
        }
        float width = Measure(font, shown);
        float x = alignment switch
        {
            TextAlignment.Centre => box.MidX - (width / 2),
            TextAlignment.Right => box.Right - width,
            _ => box.Left,
        };
        SKFontMetrics metrics = font.Metrics;
        float baseline = box.MidY - ((metrics.Ascent + metrics.Descent) / 2);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(shown, x, baseline, SKTextAlign.Left, font, paint);
    }

    /// <summary>
    /// <paramref name="text"/> as it fits in <paramref name="width"/>: whole, or cut at the end with an ellipsis.
    /// </summary>
    public static string Fit(string text, SKFont font, float width)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Measure(font, text) <= width)
        {
            return text;
        }
        int length = text.Length;
        while (length > 0 && Measure(font, string.Concat(text.AsSpan(0, length), Ellipsis)) > width)
        {
            length--;
        }
        return length == 0 ? "" : string.Concat(text.AsSpan(0, length).TrimEnd(), Ellipsis);
    }

    /// <summary>
    /// <paramref name="text"/> broken into lines no wider than <paramref name="width"/>: at its own line breaks, and
    /// between words. A word wider than the line stands on a line of its own.
    /// </summary>
    public static List<string> Wrap(string text, SKFont font, float width)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length != 0 && Measure(font, candidate) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// A picture drawn into <paramref name="destination"/> as sharp as its pixels allow: mipmapped where it is drawn
    /// smaller than it is, a cubic resample where larger (the 64 px pieces on a big board or a high-density display).
    /// </summary>
    public static void Image(SKCanvas canvas, SKImage image, SKRect destination)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(image);
        float scale = canvas.TotalMatrix.ScaleX * destination.Width / image.Width;
        SKSamplingOptions sampling = scale > 1.0f
            ? new SKSamplingOptions(SKCubicResampler.Mitchell)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, destination, sampling, paint);
    }
}
