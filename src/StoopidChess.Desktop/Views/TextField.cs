using System.Text;
using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// A line of text to type into, in the series' style (the toolkit's <c>view/TextField.h</c>): a well a step darker
/// than the window, its edge the accent at rest, lit while it holds the keyboard; the caret in the accent, the
/// selection in the selected row's colour. Taking the keyboard selects everything, so typing replaces it. Left, Right,
/// Home and End move the caret (with Shift, the selection), Backspace and Delete erase, Primary+A selects everything.
/// </summary>
internal class TextField : View
{
    private const float Inset = 8.0f;

    private string text = "";
    private int anchor;
    private float scroll;

    /// <summary>The text.</summary>
    public string Text
    {
        get => text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (text != value)
            {
                text = value;
                Caret = Math.Min(Caret, text.Length);
                anchor = Math.Min(anchor, text.Length);
                TextChanged?.Invoke();
                Invalidate();
            }
        }
    }

    /// <summary>Where the caret is, in characters from the start.</summary>
    public int Caret { get; private set; }

    /// <summary>The selected characters, from the first to past the last; empty when none.</summary>
    public (int Start, int End) Selection => (Math.Min(anchor, Caret), Math.Max(anchor, Caret));

    /// <summary>Which typed characters the field takes; null takes them all.</summary>
    public Func<char, bool>? IsAccepted { get; init; }

    /// <summary>The most characters the field holds.</summary>
    public int MaximumLength { get; init; } = 256;

    /// <summary>Raised whenever the text changes.</summary>
    public event Action? TextChanged;

    /// <summary>Raised when the field gives up the keyboard.</summary>
    public event Action? FocusLost;

    public override bool CanFocus => true;

    public override bool AcceptsText => true;

    /// <summary>
    /// Select every character, the caret at the end.
    /// </summary>
    public void SelectAll()
    {
        anchor = 0;
        Caret = text.Length;
        Invalidate();
    }

    protected override void Draw(SKCanvas canvas)
    {
        SKRect well = Plates.Reduced(LocalBounds, 0.5f);
        using var paint = new SKPaint { Color = Theme.Panel, IsAntialias = true };
        canvas.DrawRoundRect(well, 3, 3, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = IsFocused ? Theme.Amber : Theme.AmberDim;
        canvas.DrawRoundRect(well, 3, 3, paint);

        canvas.Save();
        canvas.ClipRect(new SKRect(Inset - 2, 0, Width - Inset + 2, Height));
        KeepCaretInView();
        float origin = Inset - scroll;
        (int start, int end) = Selection;
        if (IsFocused && end > start)
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = Theme.Select;
            float left = origin + Drawing.Measure(Fonts.Body, text[..start]);
            float right = origin + Drawing.Measure(Fonts.Body, text[..end]);
            canvas.DrawRect(new SKRect(left, 4, right, Height - 4), paint);
        }
        SKColor ink = IsEnabled ? Theme.Ink : Theme.Faint;
        Drawing.Text(canvas, text, new SKRect(origin, 0, origin + Drawing.Measure(Fonts.Body, text) + 1, Height), Fonts.Body, ink);
        if (IsFocused)
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = Theme.Amber;
            float x = MathF.Round(origin + Drawing.Measure(Fonts.Body, text[..Caret]));
            canvas.DrawRect(SKRect.Create(x, 5, 1.5f, Height - 10), paint);
        }
        canvas.Restore();
    }

    public override bool OnPointerDown(PointerEvent e)
    {
        if (e.Button != PointerButton.Left)
        {
            return false;
        }
        int at = IndexAt(e.Position.X);
        if (!e.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            anchor = at;
        }
        Caret = at;
        Invalidate();
        return true;
    }

    public override void OnPointerMove(PointerEvent e)
    {
        Caret = IndexAt(e.Position.X);
        Invalidate();
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        bool isExtending = e.Modifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Left when e.Modifiers is KeyModifiers.None or KeyModifiers.Shift:
                MoveCaret(Selection.End > Selection.Start && !isExtending ? Selection.Start : Caret - 1, isExtending);
                return true;
            case Key.Right when e.Modifiers is KeyModifiers.None or KeyModifiers.Shift:
                MoveCaret(Selection.End > Selection.Start && !isExtending ? Selection.End : Caret + 1, isExtending);
                return true;
            case Key.Home:
                MoveCaret(0, isExtending);
                return true;
            case Key.End:
                MoveCaret(text.Length, isExtending);
                return true;
            case Key.Backspace:
                Erase(-1);
                return true;
            case Key.Delete:
                Erase(+1);
                return true;
            case Key.A when e.Modifiers == KeyModifiers.Primary:
                SelectAll();
                return true;
            default:
                return false;
        }
    }

    public override void OnTextInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var accepted = new StringBuilder();
        foreach (char typed in text)
        {
            if (!char.IsControl(typed) && (IsAccepted?.Invoke(typed) ?? true))
            {
                accepted.Append(typed);
            }
        }
        (int start, int end) = Selection;
        int room = MaximumLength - (this.text.Length - (end - start));
        if (accepted.Length == 0 || room <= 0)
        {
            return;
        }
        string inserted = accepted.ToString()[..Math.Min(room, accepted.Length)];
        Caret = start + inserted.Length;
        anchor = Caret;
        Text = string.Concat(this.text.AsSpan(0, start), inserted, this.text.AsSpan(end));
    }

    public override void OnFocusChanged(bool isFocused)
    {
        if (isFocused)
        {
            SelectAll();
        }
        else
        {
            anchor = Caret;
            FocusLost?.Invoke();
        }
        base.OnFocusChanged(isFocused);
    }

    private void MoveCaret(int to, bool isExtending)
    {
        Caret = Math.Clamp(to, 0, text.Length);
        if (!isExtending)
        {
            anchor = Caret;
        }
        Invalidate();
    }

    private void Erase(int direction)
    {
        (int start, int end) = Selection;
        if (end == start)
        {
            start = direction < 0 ? Math.Max(0, Caret - 1) : Caret;
            end = direction < 0 ? Caret : Math.Min(text.Length, Caret + 1);
        }
        if (end == start)
        {
            return;
        }
        Caret = start;
        anchor = start;
        Text = string.Concat(text.AsSpan(0, start), text.AsSpan(end));
    }

    /// <summary>
    /// The caret's place nearest an x in the field.
    /// </summary>
    private int IndexAt(float x)
    {
        float target = x - Inset + scroll;
        for (int i = 0; i < text.Length; i++)
        {
            float middle = (Drawing.Measure(Fonts.Body, text[..i]) + Drawing.Measure(Fonts.Body, text[..(i + 1)])) / 2;
            if (target < middle)
            {
                return i;
            }
        }
        return text.Length;
    }

    private void KeepCaretInView()
    {
        float caret = Drawing.Measure(Fonts.Body, text[..Caret]);
        float room = Width - (2 * Inset);
        if (caret - scroll > room)
        {
            scroll = caret - room;
        }
        else if (caret < scroll)
        {
            scroll = caret;
        }
    }
}
