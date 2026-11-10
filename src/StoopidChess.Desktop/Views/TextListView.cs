using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// Lines of text to read, in a panel that scrolls both ways: the move list and the engine's lines, in the monospaced
/// face on the panel's colour inside a rule. A scroll bar shows along each edge where the lines do not fit, its thumb
/// the accent (Amphitheater's <c>AccentScrollBar</c>) and a quarter brighter under the pointer, as the toolkit's
/// <c>view/ScrollBar.h</c> draws it; the wheel scrolls three lines a notch, across with Shift or a sideways wheel, a
/// thumb drags, and a press on the track beside it moves a page.
/// </summary>
internal sealed class TextListView : View
{
    /// <summary>A scroll bar's thickness.</summary>
    public const float BarThickness = 9.0f;

    private const float Inset = 6.0f;

    private IReadOnlyList<string> _lines = [];
    private float _contentWidth;
    private bool _isDraggingVertical;
    private bool _isDraggingHorizontal;
    private float _dragFrom;
    private float _scrollAtDragStart;
    private SKPoint _pointer = new(-1, -1);

    /// <summary>The font.</summary>
    public SKFont Font { get; init; } = Fonts.Mono;

    /// <summary>The lines shown.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>How far down the lines are scrolled, in pixels.</summary>
    public float ScrollY { get; private set; }

    /// <summary>How far across the lines are scrolled, in pixels.</summary>
    public float ScrollX { get; private set; }

    /// <summary>The furthest down the lines scroll.</summary>
    public float MaximumScrollY => Math.Max(0, ContentHeight - ViewportHeight);

    /// <summary>The furthest across the lines scroll.</summary>
    public float MaximumScrollX => Math.Max(0, _contentWidth + (2 * Inset) - ViewportWidth);

    private float LineHeight => Drawing.LineHeight(Font);

    private float ContentHeight => (_lines.Count * LineHeight) + (2 * Inset);

    private bool HasVerticalBar => ContentHeight > Height - (_contentWidth + (2 * Inset) > Width ? BarThickness : 0);

    private bool HasHorizontalBar => _contentWidth + (2 * Inset) > Width - (ContentHeight > Height ? BarThickness : 0);

    private float ViewportWidth => Width - (HasVerticalBar ? BarThickness : 0);

    private float ViewportHeight => Height - (HasHorizontalBar ? BarThickness : 0);

    /// <summary>
    /// Show <paramref name="text"/>, a line for each of its lines.
    /// </summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SetLines(text.Length == 0 ? [] : text.Replace("\r", "", StringComparison.Ordinal).Split('\n'));
    }

    /// <summary>
    /// Show <paramref name="newLines"/>.
    /// </summary>
    public void SetLines(IReadOnlyList<string> newLines)
    {
        ArgumentNullException.ThrowIfNull(newLines);
        _lines = newLines;
        _contentWidth = _lines.Count == 0 ? 0 : _lines.Max(line => Drawing.Measure(Font, line));
        ScrollTo(ScrollX, ScrollY);
    }

    /// <summary>
    /// Scroll to the last line.
    /// </summary>
    public void ScrollToEnd() => ScrollTo(ScrollX, MaximumScrollY);

    /// <summary>
    /// Scroll to <paramref name="x"/> across and <paramref name="y"/> down, held to the lines.
    /// </summary>
    public void ScrollTo(float x, float y)
    {
        ScrollX = Math.Clamp(x, 0, MaximumScrollX);
        ScrollY = Math.Clamp(y, 0, MaximumScrollY);
        Invalidate();
    }

    protected override void Layout() => ScrollTo(ScrollX, ScrollY);

    protected override void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint { Color = Theme.Panel, IsAntialias = true };
        canvas.DrawRect(LocalBounds, paint);
        canvas.Save();
        canvas.ClipRect(SKRect.Create(ViewportWidth, ViewportHeight));
        float lineHeight = LineHeight;
        int first = Math.Max(0, (int)((ScrollY - Inset) / lineHeight));
        for (int i = first; i < _lines.Count; i++)
        {
            float y = Inset + (i * lineHeight) - ScrollY;
            if (y > ViewportHeight)
            {
                break;
            }
            float x = Inset - ScrollX;
            Drawing.Text(canvas, _lines[i], new SKRect(x, y, x + _contentWidth + 1, y + lineHeight), Font, Theme.Ink);
        }
        canvas.Restore();
        if (HasVerticalBar)
        {
            DrawThumb(canvas, paint, VerticalThumb(), _isDraggingVertical);
        }
        if (HasHorizontalBar)
        {
            DrawThumb(canvas, paint, HorizontalThumb(), _isDraggingHorizontal);
        }
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = Theme.Rule;
        canvas.DrawRect(Plates.Reduced(LocalBounds, 0.5f), paint);
    }

    public override bool OnWheel(WheelEvent e)
    {
        float step = 3 * LineHeight;
        float across = e.Modifiers.HasFlag(KeyModifiers.Shift) ? e.DeltaY : e.DeltaX;
        float down = e.Modifiers.HasFlag(KeyModifiers.Shift) ? 0 : e.DeltaY;
        ScrollTo(ScrollX + (across * step), ScrollY + (down * step));
        return true;
    }

    public override bool OnPointerDown(PointerEvent e)
    {
        if (e.Button != PointerButton.Left)
        {
            return false;
        }
        if (HasVerticalBar && e.Position.X >= ViewportWidth && e.Position.Y < ViewportHeight)
        {
            SKRect thumb = VerticalThumb();
            if (e.Position.Y < thumb.Top || e.Position.Y > thumb.Bottom)
            {
                ScrollTo(ScrollX, ScrollY + (Math.Sign(e.Position.Y - thumb.MidY) * ViewportHeight));
                return true;
            }
            _isDraggingVertical = true;
            _dragFrom = e.Position.Y;
            _scrollAtDragStart = ScrollY;
            return true;
        }
        if (HasHorizontalBar && e.Position.Y >= ViewportHeight && e.Position.X < ViewportWidth)
        {
            SKRect thumb = HorizontalThumb();
            if (e.Position.X < thumb.Left || e.Position.X > thumb.Right)
            {
                ScrollTo(ScrollX + (Math.Sign(e.Position.X - thumb.MidX) * ViewportWidth), ScrollY);
                return true;
            }
            _isDraggingHorizontal = true;
            _dragFrom = e.Position.X;
            _scrollAtDragStart = ScrollX;
            return true;
        }
        return false;
    }

    public override void OnPointerMove(PointerEvent e)
    {
        _pointer = e.Position;
        if (_isDraggingVertical)
        {
            float travel = ViewportHeight - VerticalThumb().Height;
            ScrollTo(ScrollX, _scrollAtDragStart + ((e.Position.Y - _dragFrom) * MaximumScrollY / Math.Max(1, travel)));
        }
        else if (_isDraggingHorizontal)
        {
            float travel = ViewportWidth - HorizontalThumb().Width;
            ScrollTo(_scrollAtDragStart + ((e.Position.X - _dragFrom) * MaximumScrollX / Math.Max(1, travel)), ScrollY);
        }
        else
        {
            Invalidate(); // The thumb under the pointer brightens
        }
    }

    public override void OnPointerUp(PointerEvent e)
    {
        _isDraggingVertical = false;
        _isDraggingHorizontal = false;
        Invalidate();
    }

    public override void OnHoverChanged(bool isHovered)
    {
        if (!isHovered)
        {
            _pointer = new SKPoint(-1, -1);
        }
        base.OnHoverChanged(isHovered);
    }

    /// <summary>The vertical thumb, in the view's coordinates.</summary>
    public SKRect VerticalThumb()
    {
        float track = ViewportHeight;
        float length = Math.Max(2 * BarThickness, track * ViewportHeight / Math.Max(1, ContentHeight));
        float top = MaximumScrollY <= 0 ? 0 : (track - length) * ScrollY / MaximumScrollY;
        return SKRect.Create(ViewportWidth, top, BarThickness, length);
    }

    /// <summary>The horizontal thumb, in the view's coordinates.</summary>
    public SKRect HorizontalThumb()
    {
        float track = ViewportWidth;
        float length = Math.Max(2 * BarThickness, track * ViewportWidth / Math.Max(1, _contentWidth + (2 * Inset)));
        float left = MaximumScrollX <= 0 ? 0 : (track - length) * ScrollX / MaximumScrollX;
        return SKRect.Create(left, ViewportHeight, length, BarThickness);
    }

    private void DrawThumb(SKCanvas canvas, SKPaint paint, SKRect thumb, bool isDragging)
    {
        bool isLit = isDragging || thumb.Contains(_pointer);
        paint.Style = SKPaintStyle.Fill;
        paint.Color = isLit ? Theme.Amber.Brighter(0.25f) : Theme.Amber;
        canvas.DrawRoundRect(Plates.Reduced(thumb, 1.5f), 4, 4, paint);
    }
}
