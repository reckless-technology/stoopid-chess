using SkiaSharp;

namespace StoopidChess.Desktop.Look;

/// <summary>
/// The window's colours: the series' dark look as Reckless Amphitheater wears it (the tokens of its
/// <c>src/app/Theme.h</c>), so the two chess GUIs of the series read as one family.
/// </summary>
internal static class Theme
{
    /// <summary>The window's colour, behind the panels.</summary>
    public static readonly SKColor Window = new(0xff29282c);

    /// <summary>A card, a list, a strip: one step darker than the window.</summary>
    public static readonly SKColor Panel = new(0xff1b1a1d);

    /// <summary>Every other row of a list.</summary>
    public static readonly SKColor Row = new(0xff222125);

    /// <summary>The rules and the frames round a card.</summary>
    public static readonly SKColor Rule = new(0xff3b3936);

    /// <summary>The text.</summary>
    public static readonly SKColor Ink = new(0xffe8dec8);

    /// <summary>Secondary text: a caption, a label in the status bar.</summary>
    public static readonly SKColor Muted = new(0xff9c9483);

    /// <summary>A hint, a disabled item.</summary>
    public static readonly SKColor Faint = new(0xff5f5a52);

    /// <summary>The accent: the running clock, a lit plate, a focused field, the scroll bar's thumb.</summary>
    public static readonly SKColor Amber = new(0xffe2a23e);

    /// <summary>The accent at rest: a plate's edge.</summary>
    public static readonly SKColor AmberDim = new(0xff6e5428);

    /// <summary>The menu title open, the row under the pointer.</summary>
    public static readonly SKColor Select = new(0xff5d4423);

    /// <summary>A bad state: the king in check.</summary>
    public static readonly SKColor Minus = new(0xffe2705f);

    /// <summary>The light squares.</summary>
    public static readonly SKColor LightSquare = new(0xffecdab8);

    /// <summary>The dark squares.</summary>
    public static readonly SKColor DarkSquare = new(0xff9d7553);

    /// <summary>The board's frame, an editing field's well.</summary>
    public static readonly SKColor BoardEdge = new(0xff121113);

    /// <summary>The status bar.</summary>
    public static readonly SKColor TitleBar = new(0xff19181b);

    /// <summary>A button's plate.</summary>
    public static readonly SKColor Plate = new(0xff232125);

    /// <summary>The last move on a light square: the accent at 42 %.</summary>
    public static readonly SKColor LastMoveLight = new(0xffe8c285);

    /// <summary>The last move on a dark square: the accent at 70 %, where 42 % barely shows.</summary>
    public static readonly SKColor LastMoveDark = new(0xffcd9444);

    /// <summary>The veil a modal dialog draws over the window.</summary>
    public static readonly SKColor Scrim = new(0x8c000000);

    /// <summary>The chrome rim round a plate's lamp (the toolkit's <c>colors::kLensRim</c>).</summary>
    public static readonly SKColor LensRim = new(0xff7d7a75);

    /// <summary>A plate lamp's unlit glass.</summary>
    public static readonly SKColor LensGlass = new(0xff2a2825);

    /// <summary>A lamp's amber (the toolkit's <c>colors::kAmber</c>).</summary>
    public static readonly SKColor LampAmber = new(0xffe0a038);

    /// <summary>A lit lamp's hot centre.</summary>
    public static readonly SKColor LampCentre = new(0xffffe2a0);

    /// <summary>
    /// A lighter shade, as the toolkit's <c>Color::Brighter</c>: each channel's distance from white scaled by
    /// 1 / (1 + <paramref name="amount"/>), the alpha kept.
    /// </summary>
    public static SKColor Brighter(this SKColor color, float amount)
    {
        float scale = 1.0f / (1.0f + amount);
        byte Lift(byte channel) => (byte)(255.0f - (scale * (255 - channel)));
        return new SKColor(Lift(color.Red), Lift(color.Green), Lift(color.Blue), color.Alpha);
    }

    /// <summary>
    /// A darker shade, as the toolkit's <c>Color::Darker</c>: each channel scaled by 1 / (1 + <paramref name="amount"/>).
    /// </summary>
    public static SKColor Darker(this SKColor color, float amount)
    {
        float scale = 1.0f / (1.0f + amount);
        byte Drop(byte channel) => (byte)(scale * channel);
        return new SKColor(Drop(color.Red), Drop(color.Green), Drop(color.Blue), color.Alpha);
    }

    /// <summary>
    /// The colour at <paramref name="opacity"/> (0 to 1) of full strength.
    /// </summary>
    public static SKColor WithOpacity(this SKColor color, float opacity) =>
        color.WithAlpha((byte)Math.Clamp(MathF.Round(opacity * 255.0f), 0, 255));

    /// <summary>
    /// The colour with its own alpha multiplied by <paramref name="multiplier"/>, held to opaque.
    /// </summary>
    public static SKColor WithMultipliedAlpha(this SKColor color, float multiplier) =>
        color.WithAlpha((byte)Math.Clamp(MathF.Round(multiplier * color.Alpha), 0, 255));
}
