using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// An open menu, drawn over the window as the toolkit's <c>view/Menu.h</c> draws its popup in the series' style
/// (<c>panel/PanelStyle.h</c>'s <c>MenuStyle</c>): the window's colour behind the ink, a rule round it, the row under
/// the pointer lit in the accent, ticks at the left, shortcuts at the right, an arrow beside a row with a submenu,
/// which opens beside it when the pointer rests on it. A press and release on a row chooses it; a press outside the
/// menu closes it, except in <see cref="PassThrough"/> (the menu bar, which switches menus). Up, Down, Left, Right,
/// Return and Escape work it from the keyboard.
/// </summary>
internal sealed class MenuPopup : View
{
    /// <summary>A row's height.</summary>
    public const float RowHeight = 26.0f;

    private const float SeparatorHeight = 10.0f;
    private const float TickColumn = 26.0f;
    private const float ArrowColumn = 22.0f;
    private const float Padding = 12.0f;
    private const float ShortcutGap = 28.0f;
    private const float MinimumWidth = 140.0f;

    private static readonly SKColor Border = Theme.Ink.WithOpacity(0.6f);
    private static readonly SKColor Highlight = Theme.Amber.WithOpacity(0.35f);
    private static readonly SKColor DisabledText = Theme.Ink.WithMultipliedAlpha(0.3f);

    private readonly List<Level> levels = [];
    private int pressedLevel = -1;
    private int pressedRow = -1;

    private MenuPopup(IReadOnlyList<MenuItem> items, SKPoint at) => anchor = (items, at);

    private readonly (IReadOnlyList<MenuItem> Items, SKPoint At) anchor;

    /// <summary>Where a press goes through to the views under the menu, in the window's coordinates.</summary>
    public SKRect PassThrough { get; set; } = SKRect.Empty;

    /// <summary>Raised once the menu has closed, chosen or not.</summary>
    public event Action? Closed;

    /// <summary>Raised by Left on the top menu (-1) or Right on a row with no submenu (+1): the menu bar's next menu.</summary>
    public event Action<int>? Sideways;

    /// <summary>The menus open: the first, and each submenu opened from it.</summary>
    public int LevelCount => levels.Count;

    /// <summary>
    /// Open a menu of <paramref name="items"/> with its top left at <paramref name="at"/> (in the window's
    /// coordinates), kept inside the window.
    /// </summary>
    public static MenuPopup Open(ViewRoot root, IReadOnlyList<MenuItem> items, SKPoint at)
    {
        ArgumentNullException.ThrowIfNull(root);
        var popup = new MenuPopup(items, at);
        root.ShowOverlay(popup);
        return popup;
    }

    /// <summary>
    /// The rectangle of menu <paramref name="level"/> (0 the first), in the window's coordinates.
    /// </summary>
    public SKRect MenuBounds(int level) => levels[level].Box;

    /// <summary>
    /// The centre of row <paramref name="row"/> of menu <paramref name="level"/>, in the window's coordinates.
    /// </summary>
    public SKPoint RowCentre(int level, int row)
    {
        SKRect box = RowBox(levels[level], row);
        return new SKPoint(box.MidX, box.MidY);
    }

    /// <summary>The row under the pointer in menu <paramref name="level"/>; -1 for none.</summary>
    public int HoveredRow(int level) => levels[level].Hover;

    /// <summary>
    /// Light the first row that can be chosen, as a menu opened from the keyboard does.
    /// </summary>
    public void HoverFirst()
    {
        if (levels.Count > 0)
        {
            Step(levels[0], +1);
        }
    }

    /// <summary>
    /// Close the menu, choosing nothing.
    /// </summary>
    public void Close()
    {
        if (Root is { } root)
        {
            root.CloseOverlay(this);
            Closed?.Invoke();
        }
    }

    protected override void Layout()
    {
        if (levels.Count == 0)
        {
            levels.Add(MakeLevel(anchor.Items, anchor.At, null));
        }
    }

    public override View? HitTest(SKPoint point) => PassThrough.Contains(point) ? null : base.HitTest(point);

    protected override void Draw(SKCanvas canvas)
    {
        foreach (Level level in levels)
        {
            DrawLevel(canvas, level);
        }
    }

    public override void OnPointerMove(PointerEvent e)
    {
        (int levelIndex, int row) = RowAt(e.Position);
        if (levelIndex < 0)
        {
            return;
        }
        Level level = levels[levelIndex];
        if (level.Hover != row)
        {
            level.Hover = row;
            Invalidate();
        }
        CloseLevelsAfter(levelIndex, keepSubmenuOf: row);
        if (row >= 0 && level.Items[row] is { HasSubmenu: true, IsEnabled: true } && levels.Count == levelIndex + 1)
        {
            OpenSubmenu(levelIndex);
        }
    }

    public override bool OnPointerDown(PointerEvent e)
    {
        (int levelIndex, int row) = RowAt(e.Position);
        if (levelIndex < 0)
        {
            if (!levels.Any(level => level.Box.Contains(e.Position)))
            {
                Close(); // A press outside the menu closes it, and goes no further
                return false;
            }
            pressedLevel = -1;
            return true;
        }
        pressedLevel = levelIndex;
        pressedRow = row;
        return true;
    }

    public override void OnPointerUp(PointerEvent e)
    {
        (int levelIndex, int row) = RowAt(e.Position);
        if (levelIndex >= 0 && levelIndex == pressedLevel && row == pressedRow)
        {
            Activate(levelIndex, row);
        }
        pressedLevel = -1;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (levels.Count == 0)
        {
            return false;
        }
        int deepest = levels.Count - 1;
        Level level = levels[deepest];
        switch (e.Key)
        {
            case Key.Escape:
                if (deepest == 0)
                {
                    Close();
                }
                else
                {
                    CloseLevelsAfter(deepest - 1, keepSubmenuOf: -1);
                }
                break;
            case Key.Up:
                Step(level, -1);
                break;
            case Key.Down:
                Step(level, +1);
                break;
            case Key.Right:
                if (level.Hover >= 0 && level.Items[level.Hover] is { HasSubmenu: true, IsEnabled: true })
                {
                    OpenSubmenu(deepest);
                    Step(levels[^1], +1);
                }
                else
                {
                    Sideways?.Invoke(+1);
                }
                break;
            case Key.Left:
                if (deepest > 0)
                {
                    CloseLevelsAfter(deepest - 1, keepSubmenuOf: -1);
                }
                else
                {
                    Sideways?.Invoke(-1);
                }
                break;
            case Key.Return:
                if (level.Hover >= 0)
                {
                    Activate(deepest, level.Hover);
                }
                break;
            default:
                break;
        }
        return true; // An open menu takes every key
    }

    /// <summary>
    /// A row chosen: a submenu opens; an action closes the menu, then runs.
    /// </summary>
    private void Activate(int levelIndex, int row)
    {
        MenuItem item = levels[levelIndex].Items[row];
        if (!item.IsEnabled || item.IsSeparator)
        {
            return;
        }
        if (item.HasSubmenu)
        {
            levels[levelIndex].Hover = row;
            CloseLevelsAfter(levelIndex, keepSubmenuOf: -1);
            OpenSubmenu(levelIndex);
            Step(levels[^1], +1);
            return;
        }
        Close();
        item.Chosen?.Invoke();
    }

    private void OpenSubmenu(int levelIndex)
    {
        Level level = levels[levelIndex];
        SKRect row = RowBox(level, level.Hover);
        levels.Add(MakeLevel(level.Items[level.Hover].Items, new SKPoint(level.Box.Right - 2, row.Top - 1), level.Box));
        Invalidate();
    }

    private void CloseLevelsAfter(int levelIndex, int keepSubmenuOf)
    {
        int keep = levelIndex + 1;
        if (keepSubmenuOf >= 0 && levels[levelIndex].Items[keepSubmenuOf].HasSubmenu && levels.Count > keep)
        {
            keep++; // The submenu of the row under the pointer stays open
        }
        if (levels.Count > keep)
        {
            levels.RemoveRange(keep, levels.Count - keep);
            Invalidate();
        }
    }

    private void Step(Level level, int direction)
    {
        int count = level.Items.Count;
        int row = level.Hover;
        for (int i = 0; i < count; i++)
        {
            row = row < 0 ? (direction > 0 ? 0 : count - 1) : (row + direction + count) % count;
            if (level.Items[row] is { IsSeparator: false, IsEnabled: true })
            {
                level.Hover = row;
                Invalidate();
                return;
            }
        }
    }

    /// <summary>
    /// The menu and row under a point in the window, the deepest menu first; (-1, -1) outside every menu, and row -1
    /// on a menu's border.
    /// </summary>
    private (int Level, int Row) RowAt(SKPoint point)
    {
        for (int i = levels.Count - 1; i >= 0; i--)
        {
            Level level = levels[i];
            if (!level.Box.Contains(point))
            {
                continue;
            }
            for (int row = 0; row < level.Items.Count; row++)
            {
                if (!level.Items[row].IsSeparator && RowBox(level, row).Contains(point))
                {
                    return (i, row);
                }
            }
            return (-1, -1);
        }
        return (-1, -1);
    }

    private Level MakeLevel(IReadOnlyList<MenuItem> items, SKPoint at, SKRect? parent)
    {
        float textWidth = 0;
        float shortcutWidth = 0;
        float height = 2;
        foreach (MenuItem item in items)
        {
            textWidth = Math.Max(textWidth, Drawing.Measure(Fonts.Menu, item.Text));
            shortcutWidth = Math.Max(shortcutWidth, item.Shortcut is { } shortcut ? Drawing.Measure(Fonts.Menu, shortcut) : 0);
            height += item.IsSeparator ? SeparatorHeight : RowHeight;
        }
        float width = MathF.Ceiling(Math.Max(MinimumWidth,
            TickColumn + textWidth + (shortcutWidth > 0 ? ShortcutGap + shortcutWidth : 0) + Padding + ArrowColumn));
        float x = at.X;
        float y = at.Y;
        if (x + width > Width)
        {
            x = parent is { } beside ? beside.Left - width + 2 : Width - width;
        }
        if (y + height > Height)
        {
            y = Height - height;
        }
        return new Level(items, SKRect.Create(Math.Max(0, x), Math.Max(0, y), width, height));
    }

    private static SKRect RowBox(Level level, int row)
    {
        float y = level.Box.Top + 1;
        for (int i = 0; i < row; i++)
        {
            y += level.Items[i].IsSeparator ? SeparatorHeight : RowHeight;
        }
        float height = level.Items[row].IsSeparator ? SeparatorHeight : RowHeight;
        return new SKRect(level.Box.Left + 1, y, level.Box.Right - 1, y + height);
    }

    private static void DrawLevel(SKCanvas canvas, Level level)
    {
        using var paint = new SKPaint { Color = Theme.Window, IsAntialias = true };
        canvas.DrawRect(level.Box, paint);
        paint.Color = Border;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        canvas.DrawRect(Plates.Reduced(level.Box, 0.5f), paint);
        for (int i = 0; i < level.Items.Count; i++)
        {
            MenuItem item = level.Items[i];
            SKRect row = RowBox(level, i);
            if (item.IsSeparator)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = Border;
                float middle = MathF.Floor(row.MidY) + 0.5f;
                canvas.DrawLine(row.Left + Padding, middle, row.Right - Padding, middle, paint);
                continue;
            }
            bool isLit = i == level.Hover && item.IsEnabled;
            if (isLit)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = Highlight;
                canvas.DrawRect(row, paint);
            }
            SKColor ink = item.IsEnabled ? Theme.Ink : DisabledText;
            if (item.IsChecked)
            {
                // A tick: two strokes in the left column
                var box = new SKRect(row.Left + 8, row.Top + 8, row.Left + TickColumn - 8, row.Bottom - 8);
                using var tick = new SKPath();
                tick.MoveTo(box.Left, box.Top + (box.Height * 0.55f));
                tick.LineTo(box.Left + (box.Width * 0.4f), box.Bottom);
                tick.LineTo(box.Right, box.Top);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.8f;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.Color = ink;
                canvas.DrawPath(tick, paint);
                paint.StrokeWidth = 1;
            }
            var text = new SKRect(row.Left + TickColumn, row.Top, row.Right - Padding - (item.HasSubmenu ? ArrowColumn : 0), row.Bottom);
            Drawing.Text(canvas, item.Text, text, Fonts.Menu, ink);
            if (item.Shortcut is { } shortcut)
            {
                Drawing.Text(canvas, shortcut, text, Fonts.Menu, isLit ? Theme.Ink : Theme.Muted, TextAlignment.Right);
            }
            if (item.HasSubmenu)
            {
                // An arrow: a small triangle pointing right, in the right column
                float x = row.Right - Padding - (ArrowColumn / 2);
                float y = row.MidY;
                using var arrow = new SKPath();
                arrow.MoveTo(x - 3, y - 4);
                arrow.LineTo(x - 3, y + 4);
                arrow.LineTo(x + 3, y);
                arrow.Close();
                paint.Style = SKPaintStyle.Fill;
                paint.Color = ink;
                canvas.DrawPath(arrow, paint);
            }
        }
    }

    /// <summary>
    /// One open menu: its rows, where it is, and the row under the pointer.
    /// </summary>
    private sealed class Level(IReadOnlyList<MenuItem> items, SKRect box)
    {
        public IReadOnlyList<MenuItem> Items { get; } = items;

        public SKRect Box { get; } = box;

        public int Hover { get; set; } = -1;
    }
}
