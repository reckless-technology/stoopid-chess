using SkiaSharp;

namespace StoopidChess.Desktop.Look;

/// <summary>
/// The window's fonts, embedded in the assembly so every OS draws the same letters: Noto Serif for the interface and
/// Monaspace Xenon for the move list, the FEN, the engine's lines and the clocks (the series' two families; both under
/// the SIL Open Font License, Assets/Fonts). Each role is one font, made once and never changed, at the size this
/// window's layout sets it: Amphitheater's roles, a step smaller for a window two thirds its size.
/// </summary>
internal static class Fonts
{
    private static readonly SKTypeface SerifRegular = Load("NotoSerif-Regular.ttf");
    private static readonly SKTypeface SerifBold = Load("NotoSerif-Bold.ttf");
    private static readonly SKTypeface MonoRegular = Load("monaspace.MonaspaceXenon-Regular.otf");
    private static readonly SKTypeface MonoBold = Load("monaspace.MonaspaceXenon-Bold.otf");

    /// <summary>The interface's text: a dialog's message, a field's caption.</summary>
    public static SKFont Body { get; } = Make(SerifRegular, 15.0f);

    /// <summary>The body face, bold: a value in the status bar.</summary>
    public static SKFont BodyBold { get; } = Make(SerifBold, 15.0f);

    /// <summary>Smaller text: the status bar's captions and values.</summary>
    public static SKFont Small { get; } = Make(SerifRegular, 13.5f);

    /// <summary>The small face, bold.</summary>
    public static SKFont SmallBold { get; } = Make(SerifBold, 13.5f);

    /// <summary>A section's label, in capitals and bold.</summary>
    public static SKFont Label { get; } = Make(SerifBold, 12.5f);

    /// <summary>A dialog's title.</summary>
    public static SKFont Title { get; } = Make(SerifBold, 16.0f);

    /// <summary>The menu bar's titles.</summary>
    public static SKFont MenuBar { get; } = Make(SerifRegular, 15.5f);

    /// <summary>A menu's items and their shortcuts.</summary>
    public static SKFont Menu { get; } = Make(SerifRegular, 15.0f);

    /// <summary>A plate's caption: the body face, bold, a little smaller.</summary>
    public static SKFont Plate { get; } = Make(SerifBold, 14.0f);

    /// <summary>The move list, the engine's lines and the text typed into a field: the monospaced family.</summary>
    public static SKFont Mono { get; } = Make(MonoRegular, 14.0f);

    /// <summary>A clock's reading in the status bar: the monospaced family, bold.</summary>
    public static SKFont Clock { get; } = Make(MonoBold, 13.5f);

    /// <summary>
    /// The serif, bold, at <paramref name="size"/>: the board's coordinates, whose size follows the square's. The
    /// caller disposes it.
    /// </summary>
    public static SKFont SerifBoldAt(float size) => Make(SerifBold, size);

    private static SKFont Make(SKTypeface typeface, float size) => new(typeface, size)
    {
        Edging = SKFontEdging.Antialias,
        Subpixel = true,
        Hinting = SKFontHinting.Slight,
    };

    private static SKTypeface Load(string name)
    {
        using Stream stream = Assets.Open($"Fonts.{name}");
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidDataException($"The embedded font {name} could not be read.");
    }
}
