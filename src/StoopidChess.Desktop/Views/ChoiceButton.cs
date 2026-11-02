using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// One choice of several, one in view: the series' legend plate with the choice printed on it and the plate's arrow
/// at its right (the toolkit's <c>DrawPlateArrow</c>), which opens a menu of the choices, the one made ticked. Up and
/// Down step the choice while the plate holds the keyboard.
/// </summary>
internal sealed class ChoiceButton(IReadOnlyList<string> choices) : View
{
    private int selectedIndex = -1;
    private bool isHeld;

    /// <summary>The choices.</summary>
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

    /// <summary>The menu of choices while it is open; null otherwise.</summary>
    public MenuPopup? Menu { get; private set; }

    public override bool CanFocus => true;

    /// <summary>
    /// Open the menu of choices under the plate.
    /// </summary>
    public void OpenMenu()
    {
        if (Root is not { } root || Choices.Count == 0)
        {
            return;
        }
        var items = new List<MenuItem>();
        for (int i = 0; i < Choices.Count; i++)
        {
            int index = i;
            items.Add(new MenuItem(Choices[i], () => SelectedIndex = index) { IsChecked = i == selectedIndex });
        }
        MenuPopup menu = MenuPopup.Open(root, items, ToRoot(new SKPoint(0, Height)));
        menu.Closed += () => Menu = null;
        Menu = menu;
    }

    protected override void Draw(SKCanvas canvas)
    {
        Plates.Button(canvas, LocalBounds, IsFocused, IsHovered, isHeld);
        string text = selectedIndex >= 0 ? Choices[selectedIndex] : "";
        Drawing.Text(canvas, text, new SKRect(10, 0, Width - Plates.ArrowInset - 10, Height), Fonts.Body, Theme.Ink);
        Plates.Arrow(canvas, LocalBounds, Theme.Ink);
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
            OpenMenu();
        }
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
