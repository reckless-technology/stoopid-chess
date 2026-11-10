using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// The top of a window's views: its content, and the overlays over it - an open menu, a modal dialog - each covering
/// the whole window. The host hands it the window's size, its pointer, wheel, key and text events, in the window's
/// logical pixels, and a canvas to draw on when <see cref="IsDirty"/>; a test does the same. The root sends a press to
/// the topmost view under it that takes it, and that view then has the pointer until the release; keys go to the view
/// holding the keyboard and up through its parents, and to the top overlay before anything under it, so a dialog is
/// modal.
/// </summary>
internal sealed class ViewRoot : View
{
    private readonly List<View> _overlays = [];
    private View? _content;
    private View? _captured;
    private View? _hovered;

    /// <summary>The view holding the keyboard, if any.</summary>
    public View? Focused { get; private set; }

    /// <summary>Whether something has changed since the window was last drawn.</summary>
    public bool IsDirty { get; private set; } = true;

    /// <summary>Raised when the window wants drawing again.</summary>
    public event Action? Invalidated;

    /// <summary>The window's content, under every overlay.</summary>
    public View? Content
    {
        get => _content;
        set
        {
            if (_content is { } old)
            {
                RemoveChild(old);
            }
            _content = value;
            if (value is not null)
            {
                AddChild(value);
                value.SetBounds(LocalBounds);
                foreach (View overlay in _overlays)
                {
                    AddChild(overlay); // Back on top of the new content
                }
            }
        }
    }

    /// <summary>The overlays open, bottom to top.</summary>
    public IReadOnlyList<View> Overlays => _overlays;

    /// <summary>Whether the view holding the keyboard wants typed text.</summary>
    public bool WantsTextInput => Focused?.AcceptsText == true;

    /// <summary>
    /// The window's new size, in logical pixels.
    /// </summary>
    public void Resize(float width, float height) => SetBounds(SKRect.Create(width, height));

    protected override void Layout()
    {
        _content?.SetBounds(LocalBounds);
        foreach (View overlay in _overlays)
        {
            overlay.SetBounds(LocalBounds);
        }
    }

    /// <summary>
    /// Open <paramref name="overlay"/> over everything, covering the window.
    /// </summary>
    public void ShowOverlay(View overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlays.Add(overlay);
        AddChild(overlay);
        overlay.SetBounds(LocalBounds);
        SetHoveredView(null);
    }

    /// <summary>
    /// Close <paramref name="overlay"/>; the keyboard and the pointer let go of anything inside it.
    /// </summary>
    public void CloseOverlay(View overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!_overlays.Remove(overlay))
        {
            return;
        }
        if (overlay.Contains(Focused))
        {
            Focus(null);
        }
        if (overlay.Contains(_captured))
        {
            _captured = null;
        }
        if (overlay.Contains(_hovered))
        {
            SetHoveredView(null);
        }
        RemoveChild(overlay);
    }

    /// <summary>
    /// Give the keyboard to <paramref name="view"/>, or to nothing.
    /// </summary>
    public void Focus(View? view)
    {
        if (Focused == view)
        {
            return;
        }
        View? old = Focused;
        Focused = view;
        old?.OnFocusChanged(false);
        view?.OnFocusChanged(true);
        Invalidate();
    }

    /// <summary>
    /// Ask for the window to be drawn again.
    /// </summary>
    public new void Invalidate()
    {
        IsDirty = true;
        Invalidated?.Invoke();
    }

    /// <summary>
    /// The window drawn on <paramref name="canvas"/>, in logical pixels.
    /// </summary>
    public void Render(SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        IsDirty = false;
        canvas.Clear(Theme.Window);
        Paint(canvas);
    }

    /// <summary>
    /// A press, at a point in the window: the keyboard goes to the first view up from the one pressed that takes it
    /// (and stays where it was when none does), and the press to the first that takes the press.
    /// </summary>
    public void PointerDown(PointerEvent e)
    {
        if (_captured is not null)
        {
            return; // One press at a time: a second button while the first is held is ignored
        }
        List<View> chain = ChainFrom(HitTest(e.Position));
        if (chain.Count == 0)
        {
            return;
        }
        if (chain.FirstOrDefault(view => view.CanFocus) is { } focusable)
        {
            Focus(focusable);
        }
        foreach (View view in chain)
        {
            if (view.OnPointerDown(e.At(view.FromRoot(e.Position))))
            {
                _captured = view;
                break;
            }
        }
    }

    /// <summary>
    /// The pointer moved to a point in the window.
    /// </summary>
    public void PointerMove(PointerEvent e)
    {
        if (_captured is { } holder)
        {
            holder.OnPointerMove(e.At(holder.FromRoot(e.Position)));
            return;
        }
        View? target = HitTest(e.Position);
        SetHoveredView(target);
        if (target is not null && target != this && target.IsEnabled)
        {
            target.OnPointerMove(e.At(target.FromRoot(e.Position)));
        }
    }

    /// <summary>
    /// The press was released, at a point in the window.
    /// </summary>
    public void PointerUp(PointerEvent e)
    {
        if (_captured is not { } holder)
        {
            return;
        }
        _captured = null;
        holder.OnPointerUp(e.At(holder.FromRoot(e.Position)));
        SetHoveredView(HitTest(e.Position));
    }

    /// <summary>
    /// The pointer left the window.
    /// </summary>
    public void PointerLeave()
    {
        if (_captured is null)
        {
            SetHoveredView(null);
        }
    }

    /// <summary>
    /// The wheel turned at a point in the window: to the first view up from the one under it that takes it.
    /// </summary>
    public void Wheel(WheelEvent e)
    {
        foreach (View view in ChainFrom(HitTest(e.Position)))
        {
            if (view.OnWheel(e with { Position = view.FromRoot(e.Position) }))
            {
                return;
            }
        }
    }

    /// <summary>
    /// A key: to the view holding the keyboard and up through its parents, the top overlay first where the keyboard is
    /// outside it, and the content last. Tab, where nothing takes it, moves the keyboard to the next view that takes
    /// it (Shift+Tab the one before).
    /// </summary>
    public void KeyDown(KeyEvent e)
    {
        View? top = _overlays.Count > 0 ? _overlays[^1] : _content;
        View? start = top is not null && !top.Contains(Focused) ? top : Focused;
        foreach (View view in ChainFrom(start))
        {
            if (view.OnKeyDown(e))
            {
                return;
            }
        }
        if (e.Key == Key.Tab && top is not null && e.Modifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            MoveFocus(top, e.Modifiers == KeyModifiers.Shift);
        }
    }

    /// <summary>
    /// Text typed: to the view holding the keyboard, when it is in the top layer.
    /// </summary>
    public void TextInput(string text)
    {
        View? top = _overlays.Count > 0 ? _overlays[^1] : _content;
        if (Focused is { } focused && (top?.Contains(focused) ?? false) && focused.IsEnabled)
        {
            focused.OnTextInput(text);
        }
    }

    /// <summary>
    /// The keyboard moved to the next (or previous) view in <paramref name="scope"/> that takes it, round to the first.
    /// </summary>
    private void MoveFocus(View scope, bool isBackwards)
    {
        var candidates = new List<View>();
        Collect(scope, candidates);
        if (candidates.Count == 0)
        {
            return;
        }
        int index = Focused is null ? -1 : candidates.IndexOf(Focused);
        int next = isBackwards ? (index <= 0 ? candidates.Count - 1 : index - 1) : (index + 1) % candidates.Count;
        Focus(candidates[next]);

        static void Collect(View view, List<View> into)
        {
            if (!view.IsVisible || !view.IsEnabled)
            {
                return;
            }
            if (view.CanFocus)
            {
                into.Add(view);
            }
            foreach (View child in view.Children)
            {
                Collect(child, into);
            }
        }
    }

    /// <summary>
    /// <paramref name="view"/> and its parents up to (not including) the root, the disabled ones left out.
    /// </summary>
    private List<View> ChainFrom(View? view)
    {
        var chain = new List<View>();
        for (View? at = view; at is not null && at != this; at = at.Parent)
        {
            if (at.IsEnabled)
            {
                chain.Add(at);
            }
        }
        return chain;
    }

    private void SetHoveredView(View? view)
    {
        if (view == this)
        {
            view = null;
        }
        if (_hovered == view)
        {
            return;
        }
        _hovered?.SetHovered(false);
        _hovered = view;
        view?.SetHovered(true);
    }
}
