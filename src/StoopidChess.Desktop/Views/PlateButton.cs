using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// A button, drawn as the series' legend plate (Amphitheater's <c>theme::PlatePainter</c>): its caption in the plate
/// face, or a picture, lit under the pointer and while held. A release over it, after a press on it, raises
/// <see cref="Clicked"/>; so does <see cref="Click"/>, as Return does for a dialog's default button.
/// </summary>
internal sealed class PlateButton(string text) : View
{
    private bool isHeld;

    /// <summary>The caption.</summary>
    public string Text { get; set; } = text;

    /// <summary>A picture drawn on the plate in place of the caption: a piece, for the promotion dialog.</summary>
    public SKImage? Image { get; init; }

    /// <summary>Whether the button is on, for a button that toggles: its edge is lit.</summary>
    public bool IsOn { get; set; }

    /// <summary>Raised when the button is clicked.</summary>
    public event Action? Clicked;

    /// <summary>
    /// Click the button, if it is enabled.
    /// </summary>
    public void Click()
    {
        if (IsEnabled)
        {
            Clicked?.Invoke();
        }
    }

    protected override void Draw(SKCanvas canvas)
    {
        Plates.Button(canvas, LocalBounds, IsOn, IsHovered && IsEnabled, isHeld);
        if (Image is { } image)
        {
            float side = Math.Min(Width, Height) - 8;
            var picture = SKRect.Create((Width - side) / 2, (Height - side) / 2, side, side);
            if (IsEnabled)
            {
                Drawing.Image(canvas, image, picture);
            }
            return;
        }
        SKColor ink = IsEnabled ? Theme.Ink : Theme.Ink.WithOpacity(0.4f);
        Drawing.Text(canvas, Text, SKRect.Create(4, 0, Width - 8, Height), Fonts.Plate, ink, TextAlignment.Centre);
    }

    public override bool OnPointerDown(PointerEvent e)
    {
        if (e.Button != PointerButton.Left)
        {
            return false;
        }
        isHeld = true;
        Invalidate();
        return true;
    }

    public override void OnPointerUp(PointerEvent e)
    {
        isHeld = false;
        Invalidate();
        if (LocalBounds.Contains(e.Position))
        {
            Click();
        }
    }
}
