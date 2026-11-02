using SkiaSharp;

namespace StoopidChess.Desktop.Look;

/// <summary>
/// The series' legend plate, the one drawing every button, choice and lamp in the window is made of. A C# translation
/// of reckless-toolkit's <c>look/LookDrawing.cpp</c> (<c>DrawButtonPlate</c>, <c>DrawPlateArrow</c>,
/// <c>DrawPlateLamp</c>) and its metrics in <c>look/LookMetrics.h</c>, Copyright Jonny Reckless, GPLv3 like this
/// repository; coloured as Amphitheater's <c>theme::Buttons()</c> colours its plates.
/// </summary>
internal static class Plates
{
    /// <summary>A plate's lift under the pointer (the toolkit's kPlateLitLift).</summary>
    private const float LitLift = 0.04f;

    /// <summary>A plate's lift while held (kPlatePressedLift).</summary>
    private const float PressedLift = 0.15f;

    /// <summary>An arrow's or a lamp's centre in from the plate's edge (kArrowInset).</summary>
    public const float ArrowInset = 12.0f;

    /// <summary>An arrow's half-width (kArrowHalfWidth).</summary>
    private const float ArrowHalfWidth = 4.0f;

    /// <summary>A lamp's lens (kPlateLampDiameter).</summary>
    private const float LampDiameter = 7.0f;

    /// <summary>
    /// A button's plate: <paramref name="bounds"/> less a pixel, a shade lighter under the pointer and lighter again while
    /// held, its edge in the accent at rest, coming up when lit and further while the button is on.
    /// </summary>
    public static void Button(SKCanvas canvas, SKRect bounds, bool isOn, bool isPointerOver, bool isHeld)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        SKRect plate = Reduced(bounds, 1.0f);
        bool isLit = isPointerOver || isHeld || isOn;
        SKColor fill = isHeld ? Theme.Plate.Brighter(PressedLift) : isLit ? Theme.Plate.Brighter(LitLift) : Theme.Plate;
        using var paint = new SKPaint { Color = fill, IsAntialias = true };
        canvas.DrawRoundRect(plate, 4.0f, 4.0f, paint);
        paint.Color = Theme.AmberDim.WithMultipliedAlpha(isOn ? 1.8f : isLit ? 1.6f : 1.0f);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1.2f;
        canvas.DrawRoundRect(plate, 4.0f, 4.0f, paint);
    }

    /// <summary>
    /// A menu's arrow on a plate that opens one: a small triangle pointing down, its centre
    /// <see cref="ArrowInset"/> in from the plate's right.
    /// </summary>
    public static void Arrow(SKCanvas canvas, SKRect bounds, SKColor color)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        SKRect rect = Reduced(bounds, 1.0f);
        float x = rect.Right - ArrowInset;
        float y = rect.MidY;
        using var triangle = new SKPath();
        triangle.MoveTo(x - ArrowHalfWidth, y - 2.5f);
        triangle.LineTo(x + ArrowHalfWidth, y - 2.5f);
        triangle.LineTo(x, y + 3.0f);
        triangle.Close();
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawPath(triangle, paint);
    }

    /// <summary>
    /// A toggle's lamp: a lens in its chrome rim, centred <see cref="ArrowInset"/> in from the left of
    /// <paramref name="bounds"/>; lit, amber with its glow on the plate round it, so a choice that is on is seen to be.
    /// </summary>
    public static void Lamp(SKCanvas canvas, SKRect bounds, bool isLit)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        SKRect rect = Reduced(bounds, 1.0f);
        var centre = new SKPoint(rect.Left + ArrowInset, rect.MidY);
        float radius = LampDiameter / 2;
        using var paint = new SKPaint { IsAntialias = true };
        if (isLit)
        {
            // The glow round the lens, two rings fading out, then the lens hot at its centre and amber to its rim.
            paint.Color = Theme.LampAmber.WithOpacity(0.22f);
            canvas.DrawCircle(centre, radius + (0.6f * LampDiameter), paint);
            paint.Color = Theme.LampAmber.WithOpacity(0.45f);
            canvas.DrawCircle(centre, radius + (0.25f * LampDiameter), paint);
            paint.Shader = SKShader.CreateRadialGradient(centre, radius, [Theme.LampCentre, Theme.LampAmber], SKShaderTileMode.Clamp);
        }
        else
        {
            var highlight = new SKPoint(centre.X - (0.2f * LampDiameter), centre.Y - (0.2f * LampDiameter));
            paint.Shader = SKShader.CreateRadialGradient(highlight, LampDiameter,
                [Theme.LensGlass.Brighter(0.6f), Theme.LensGlass.Darker(0.4f)], SKShaderTileMode.Clamp);
        }
        canvas.DrawCircle(centre, radius, paint);
        paint.Shader = null;
        paint.Color = Theme.LensRim;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1.0f;
        canvas.DrawCircle(centre, radius + 0.5f, paint);
    }

    /// <summary>
    /// <paramref name="rect"/> brought in by <paramref name="amount"/> on every side.
    /// </summary>
    public static SKRect Reduced(SKRect rect, float amount) =>
        new(rect.Left + amount, rect.Top + amount, rect.Right - amount, rect.Bottom - amount);
}
