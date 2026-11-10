using SkiaSharp;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// A rectangle of the window that draws itself and takes input: the base of every view. A view's
/// <see cref="Bounds"/> are in its parent's coordinates; it draws, and is handed pointer positions, in its own, with
/// its top left at the origin. Views know nothing of the window system: the <see cref="ViewRoot"/> at the top of the
/// tree is handed the host's events and a canvas to draw on, so a test can do both.
/// </summary>
internal abstract class View
{
    private readonly List<View> _children = [];
    private bool _isVisible = true;
    private bool _isEnabled = true;

    /// <summary>The view this one is a child of; null for a root, or for a view not yet placed.</summary>
    public View? Parent { get; private set; }

    /// <summary>Where the view is, in its parent's coordinates.</summary>
    public SKRect Bounds { get; private set; }

    /// <summary>The view's width.</summary>
    public float Width => Bounds.Width;

    /// <summary>The view's height.</summary>
    public float Height => Bounds.Height;

    /// <summary>The view's own rectangle: its size at the origin.</summary>
    public SKRect LocalBounds => SKRect.Create(Width, Height);

    /// <summary>The views inside this one, drawn in order (the last on top).</summary>
    public IReadOnlyList<View> Children => _children;

    /// <summary>Whether the view is drawn and can be pointed at.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                Invalidate();
            }
        }
    }

    /// <summary>Whether the view takes input; a disabled view draws itself dimmed.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                Invalidate();
            }
        }
    }

    /// <summary>Whether the pointer is over the view (and over no child of it that takes the pointer).</summary>
    public bool IsHovered { get; private set; }

    /// <summary>Whether the view holds the keyboard.</summary>
    public bool IsFocused => Root?.Focused == this;

    /// <summary>Whether a press on the view gives it the keyboard.</summary>
    public virtual bool CanFocus => false;

    /// <summary>Whether the view, holding the keyboard, wants typed text (the host turns on text input for it).</summary>
    public virtual bool AcceptsText => false;

    /// <summary>The root at the top of the view's tree; null while the view is in none.</summary>
    public ViewRoot? Root
    {
        get
        {
            View view = this;
            while (view.Parent is { } parent)
            {
                view = parent;
            }
            return view as ViewRoot;
        }
    }

    /// <summary>
    /// Place <paramref name="child"/> inside this view, on top of the others.
    /// </summary>
    public void AddChild(View child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent?.RemoveChild(child);
        child.Parent = this;
        _children.Add(child);
        Invalidate();
    }

    /// <summary>
    /// Take <paramref name="child"/> out of this view.
    /// </summary>
    public void RemoveChild(View child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_children.Remove(child))
        {
            Invalidate();
            child.Parent = null;
        }
    }

    /// <summary>
    /// Move the view to <paramref name="bounds"/>, in its parent's coordinates, and lay out what it holds.
    /// </summary>
    public void SetBounds(SKRect bounds)
    {
        Bounds = bounds;
        Layout();
        Invalidate();
    }

    /// <summary>
    /// Ask for the window to be drawn again.
    /// </summary>
    public void Invalidate() => Root?.Invalidate();

    /// <summary>
    /// Whether <paramref name="view"/> is this view or inside it.
    /// </summary>
    public bool Contains(View? view)
    {
        for (View? at = view; at is not null; at = at.Parent)
        {
            if (at == this)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A point in the root's coordinates, in this view's.
    /// </summary>
    public SKPoint FromRoot(SKPoint point)
    {
        for (View? at = this; at?.Parent is not null; at = at.Parent)
        {
            point.Offset(-at.Bounds.Left, -at.Bounds.Top);
        }
        return point;
    }

    /// <summary>
    /// A point in this view's coordinates, in the root's.
    /// </summary>
    public SKPoint ToRoot(SKPoint point)
    {
        for (View? at = this; at?.Parent is not null; at = at.Parent)
        {
            point.Offset(at.Bounds.Left, at.Bounds.Top);
        }
        return point;
    }

    /// <summary>
    /// The view, and its children over it, drawn on <paramref name="canvas"/> in the view's coordinates.
    /// </summary>
    public void Paint(SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!IsVisible)
        {
            return;
        }
        Draw(canvas);
        foreach (View child in _children)
        {
            if (!child.IsVisible)
            {
                continue;
            }
            canvas.Save();
            canvas.Translate(child.Bounds.Left, child.Bounds.Top);
            if (child.ClipsChildren)
            {
                canvas.ClipRect(child.LocalBounds);
            }
            child.Paint(canvas);
            canvas.Restore();
        }
    }

    /// <summary>
    /// The view under <paramref name="point"/> (in this view's coordinates): the topmost visible child that takes it,
    /// else this view; null where the point is outside it.
    /// </summary>
    public virtual View? HitTest(SKPoint point)
    {
        if (!IsVisible || point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height)
        {
            return null;
        }
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            View child = _children[i];
            if (child.HitTest(new SKPoint(point.X - child.Bounds.Left, point.Y - child.Bounds.Top)) is { } hit)
            {
                return hit;
            }
        }
        return this;
    }

    /// <summary>Whether the children are clipped to the view's rectangle as they are drawn.</summary>
    protected virtual bool ClipsChildren => true;

    /// <summary>Lay out the children for the view's new size.</summary>
    protected virtual void Layout() { }

    /// <summary>Draw the view itself, under its children.</summary>
    protected virtual void Draw(SKCanvas canvas) { }

    /// <summary>A press on the view. Return true to take it, and with it the pointer's moves and its release.</summary>
    public virtual bool OnPointerDown(PointerEvent e) => false;

    /// <summary>The pointer moved: over the view, or anywhere while the view holds a press it took.</summary>
    public virtual void OnPointerMove(PointerEvent e) { }

    /// <summary>The press the view took was released.</summary>
    public virtual void OnPointerUp(PointerEvent e) { }

    /// <summary>The wheel turned over the view. Return true to take it; otherwise it goes to the parent.</summary>
    public virtual bool OnWheel(WheelEvent e) => false;

    /// <summary>A key, the view holding the keyboard or on the way to the one that does. Return true to take it.</summary>
    public virtual bool OnKeyDown(KeyEvent e) => false;

    /// <summary>Text typed while the view holds the keyboard.</summary>
    public virtual void OnTextInput(string text) { }

    /// <summary>The view took or gave up the keyboard.</summary>
    public virtual void OnFocusChanged(bool isFocused) => Invalidate();

    /// <summary>The pointer came over the view or left it.</summary>
    public virtual void OnHoverChanged(bool isHovered) => Invalidate();

    /// <summary>Set by the root as the pointer comes and goes.</summary>
    internal void SetHovered(bool isHovered)
    {
        if (IsHovered != isHovered)
        {
            IsHovered = isHovered;
            OnHoverChanged(isHovered);
        }
    }
}
