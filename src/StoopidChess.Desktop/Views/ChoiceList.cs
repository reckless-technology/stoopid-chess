using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// One choice of several, all in view, each a row with the series' plate lamp at its left (the toolkit's
/// <c>DrawPlateLamp</c>), lit on the one chosen: a press on a row chooses it, and Up and Down move the choice while the
/// list holds the keyboard.
/// </summary>
internal sealed class ChoiceList(IReadOnlyList<string> choices) : View
{
    /// <summary>A row's height.</summary>
    public const float RowHeight = 28.0f;

    private const float TextInset = 28.0f;

    private int selectedIndex = -1;

    /// <summary>The choices, top to bottom.</summary>
    public IReadOnlyList<string> Choices { get; } = choices;

    /// <summary>The choice made; -1 for none.</summary>
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = Math.Clamp(value, -1, Choices.Count - 1);
            Invalidate();
        }
    }

    /// <summary>The height the list takes.</summary>
    public float PreferredHeight => Choices.Count * RowHeight;

    public override bool CanFocus => true;

    /// <summary>
    /// The centre of row <paramref name="index"/>, in the list's coordinates.
    /// </summary>
    public SKPoint RowCentre(int index) => new(Width / 2, (index + 0.5f) * RowHeight);

    protected override void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = true };
        for (int i = 0; i < Choices.Count; i++)
        {
            var row = SKRect.Create(0, i * RowHeight, Width, RowHeight);
            if (i == selectedIndex && IsFocused)
            {
                paint.Color = Theme.Row;
                canvas.DrawRoundRect(row, 3, 3, paint);
            }
            Plates.Lamp(canvas, SKRect.Create(0, row.Top, Width, RowHeight), i == selectedIndex);
            Drawing.Text(canvas, Choices[i], new SKRect(TextInset, row.Top, Width, row.Bottom), Fonts.Body, Theme.Ink);
        }
    }

    public override bool OnPointerDown(PointerEvent e)
    {
        int index = (int)(e.Position.Y / RowHeight);
        if (e.Button == PointerButton.Left && index >= 0 && index < Choices.Count)
        {
            SelectedIndex = index;
        }
        return true;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Is(Key.Up))
        {
            SelectedIndex = Math.Max(0, selectedIndex - 1);
            return true;
        }
        if (e.Is(Key.Down))
        {
            SelectedIndex = Math.Min(Choices.Count - 1, selectedIndex + 1);
            return true;
        }
        return false;
    }
}
