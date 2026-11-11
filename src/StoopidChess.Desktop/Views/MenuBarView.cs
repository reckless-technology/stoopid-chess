using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// The menu bar, drawn on every OS as the toolkit's <c>app/MenuBarView.h</c> draws it where the platform has none, in
/// Amphitheater's colours (<c>theme::MenuBarLook</c>): the panel's colour under the interface's face, a rule beneath,
/// the title whose menu is open lit as a selected row. A press on a title opens its menu under it (a title with no
/// rows acts at once), a press on the open title closes it, and while one is open the pointer moving onto another
/// title opens that one instead. Alt and a title's first letter open it from the keyboard.
/// </summary>
internal sealed class MenuBarView : View
{
    /// <summary>The bar's height.</summary>
    public const float BarHeight = 30.0f;

    private const float TitlePadding = 11.0f;

    private readonly List<SKRect> _titles = [];
    private MenuPopup? _popup;

    /// <summary>The bar's menus, left to right: each a title whose items are its rows.</summary>
    public List<MenuItem> Menus { get; } = [];

    /// <summary>The index of the menu open; -1 for none.</summary>
    public int OpenMenu { get; private set; } = -1;

    /// <summary>The open menu's popup; null while none is open.</summary>
    public MenuPopup? Popup => _popup;

    /// <summary>
    /// The rectangle of title <paramref name="index"/>, in the bar's coordinates.
    /// </summary>
    public SKRect TitleBounds(int index)
    {
        LayOutTitles();
        return _titles[index];
    }

    /// <summary>
    /// Open menu <paramref name="index"/> under its title (or, for a title with no rows, act on it).
    /// </summary>
    public void Open(int index)
    {
        Close();
        if (index < 0 || index >= Menus.Count || !Menus[index].IsEnabled || Root is not { } root)
        {
            return;
        }
        MenuItem menu = Menus[index];
        if (!menu.HasSubmenu)
        {
            menu.Chosen?.Invoke();
            return;
        }
        LayOutTitles();
        SKRect title = _titles[index];
        MenuPopup opened = MenuPopup.Open(root, menu.Items, ToRoot(new SKPoint(title.Left, title.Bottom)));
        SKPoint origin = ToRoot(SKPoint.Empty);
        opened.PassThrough = SKRect.Create(origin.X, origin.Y, Width, Height);
        opened.Closed += () =>
        {
            if (_popup == opened)
            {
                _popup = null;
                OpenMenu = -1;
                Invalidate();
            }
        };
        opened.Sideways += direction =>
        {
            int next = (OpenMenu + direction + Menus.Count) % Menus.Count;
            Open(next);
            _popup?.HoverFirst();
        };
        _popup = opened;
        OpenMenu = index;
        Invalidate();
    }

    /// <summary>
    /// Close the open menu, choosing nothing.
    /// </summary>
    public void Close() => _popup?.Close();

    /// <summary>
    /// Open the menu whose title starts with <paramref name="letter"/>, its first row lit; false when no title does.
    /// </summary>
    public bool TryOpenByLetter(char letter)
    {
        int index = Menus.FindIndex(menu =>
            menu.Text.Length > 0 && char.ToUpperInvariant(menu.Text[0]) == char.ToUpperInvariant(letter)
        );
        if (index < 0)
        {
            return false;
        }
        Open(index);
        _popup?.HoverFirst();
        return true;
    }

    protected override void Draw(SKCanvas canvas)
    {
        LayOutTitles();
        using var paint = new SKPaint { Color = Theme.Panel };
        canvas.DrawRect(LocalBounds, paint);
        paint.Color = Theme.Rule;
        canvas.DrawRect(SKRect.Create(0, Height - 1, Width, 1), paint);
        for (int i = 0; i < _titles.Count; i++)
        {
            if (i == OpenMenu)
            {
                paint.Color = Theme.Select;
                canvas.DrawRect(
                    new SKRect(_titles[i].Left, _titles[i].Top, _titles[i].Right, _titles[i].Bottom - 1),
                    paint
                );
            }
            Drawing.Text(
                canvas,
                Menus[i].Text,
                _titles[i],
                Fonts.MenuBar,
                Menus[i].IsEnabled ? Theme.Ink : Theme.Faint,
                TextAlignment.Centre
            );
        }
    }

    public override bool OnPointerDown(PointerEvent e)
    {
        int index = TitleAt(e.Position);
        if (index < 0)
        {
            Close();
            return false;
        }
        if (index == OpenMenu)
        {
            Close();
        }
        else
        {
            Open(index);
        }
        return true;
    }

    public override void OnPointerMove(PointerEvent e)
    {
        int index = TitleAt(e.Position);
        if (_popup is not null && index >= 0 && index != OpenMenu && Menus[index].HasSubmenu)
        {
            Open(index);
        }
    }

    private int TitleAt(SKPoint point)
    {
        LayOutTitles();
        return _titles.FindIndex(title => title.Contains(point));
    }

    private void LayOutTitles()
    {
        _titles.Clear();
        float x = 0;
        foreach (MenuItem menu in Menus)
        {
            float width = MathF.Ceiling(Drawing.Measure(Fonts.MenuBar, menu.Text)) + (2 * TitlePadding);
            _titles.Add(SKRect.Create(x, 0, width, Height));
            x += width;
        }
    }
}
