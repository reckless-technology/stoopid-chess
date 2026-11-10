using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The drawn menu bar and its menus: opened by a press, switched by the pointer, chosen by a press and release or
/// from the keyboard, submenus opened by resting on their row, ticks and shortcuts drawn, and closed by a press
/// outside.
/// </summary>
public class MenuTests
{
    private sealed class Bar
    {
        public Bar()
        {
            var panel = new TestPanel();
            Window = new ViewHarness(panel, 600, 400);
            MenuBar = panel.Place(new MenuBarView(), 0, 0, 600, MenuBarView.BarHeight);
            var file = new MenuItem("File");
            file.Items.Add(new MenuItem("Save game", () => Chosen.Add("save")) { Shortcut = "Ctrl+S" });
            file.Items.Add(MenuItem.Separator());
            file.Items.Add(new MenuItem("Disabled", () => Chosen.Add("disabled")) { IsEnabled = false });
            file.Items.Add(new MenuItem("Exit", () => Chosen.Add("exit")));
            var game = new MenuItem("Game");
            var newGame = new MenuItem("New Game");
            newGame.Items.Add(new MenuItem("White", () => Chosen.Add("white")) { IsChecked = true });
            newGame.Items.Add(new MenuItem("Black", () => Chosen.Add("black")));
            game.Items.Add(newGame);
            game.Items.Add(new MenuItem("Undo", () => Chosen.Add("undo")));
            MenuBar.Menus.Add(file);
            MenuBar.Menus.Add(game);
            MenuBar.Menus.Add(new MenuItem("Help", () => Chosen.Add("help")));
            MenuBar.Menus.Add(new MenuItem("Off") { IsEnabled = false });
        }

        public ViewHarness Window { get; }

        public MenuBarView MenuBar { get; }

        public List<string> Chosen { get; } = [];

        public SKPoint Title(int index)
        {
            SKRect title = MenuBar.TitleBounds(index);
            return new SKPoint(title.MidX, title.MidY);
        }

        public MenuPopup Popup => MenuBar.Popup ?? throw new InvalidOperationException("no menu is open");
    }

    [Fact]
    public void APressOnATitleOpensItsMenuAndAPressAndReleaseOnARowChoosesIt()
    {
        var bar = new Bar();
        bar.Window.Click(bar.Title(0));
        Assert.Equal(0, bar.MenuBar.OpenMenu);
        Assert.Single(bar.Window.Root.Overlays);
        bar.Window.Render();
        bar.Window.Click(bar.Popup.RowCentre(0, 3));
        Assert.Equal(["exit"], bar.Chosen);
        Assert.Equal(-1, bar.MenuBar.OpenMenu);
        Assert.Empty(bar.Window.Root.Overlays);
    }

    [Fact]
    public void ADisabledRowASeparatorAndTheBorderChooseNothing()
    {
        var bar = new Bar();
        bar.Window.Click(bar.Title(0));
        MenuPopup popup = bar.Popup;
        bar.Window.Click(popup.RowCentre(0, 2)); // Disabled
        bar.Window.Click(popup.RowCentre(0, 1)); // The separator
        SKRect box = popup.MenuBounds(0);
        bar.Window.Click(new SKPoint(box.Left + 0.5f, box.MidY)); // The border
        bar.Window.Press(popup.RowCentre(0, 0));
        bar.Window.Release(popup.RowCentre(0, 3)); // Released on another row
        Assert.Empty(bar.Chosen);
        Assert.Equal(0, bar.MenuBar.OpenMenu);
    }

    [Fact]
    public void APressOnTheOpenTitleOrOutsideTheMenuClosesIt()
    {
        var bar = new Bar();
        bar.Window.Click(bar.Title(0));
        bar.Window.Click(bar.Title(0));
        Assert.Equal(-1, bar.MenuBar.OpenMenu);
        bar.Window.Click(bar.Title(1));
        bar.Window.Click(new SKPoint(500, 300));
        Assert.Equal(-1, bar.MenuBar.OpenMenu);
        Assert.Empty(bar.Window.Root.Overlays);
        bar.Window.Click(bar.Title(1));
        bar.Window.Click(new SKPoint(590, 15)); // The bar beyond its titles
        Assert.Equal(-1, bar.MenuBar.OpenMenu);
        bar.Window.Click(bar.Title(3)); // A disabled title
        Assert.Equal(-1, bar.MenuBar.OpenMenu);
        Assert.Empty(bar.Chosen);
    }

    [Fact]
    public void ThePointerMovingOntoAnotherTitleSwitchesMenus()
    {
        var bar = new Bar();
        bar.Window.Click(bar.Title(0));
        bar.Window.Move(bar.Title(1));
        Assert.Equal(1, bar.MenuBar.OpenMenu);
        Assert.Single(bar.Window.Root.Overlays);
        bar.Window.Move(bar.Title(2)); // Help has no menu to switch to
        Assert.Equal(1, bar.MenuBar.OpenMenu);
        bar.MenuBar.Close();
        bar.Window.Move(bar.Title(0)); // Nothing open: nothing opens
        Assert.Equal(-1, bar.MenuBar.OpenMenu);
    }

    [Fact]
    public void ATitleWithNoRowsActsAtOnce()
    {
        var bar = new Bar();
        bar.Window.Click(bar.Title(2));
        Assert.Equal(["help"], bar.Chosen);
        Assert.Empty(bar.Window.Root.Overlays);
        bar.MenuBar.Open(9);
        Assert.Empty(bar.Window.Root.Overlays);
    }

    [Fact]
    public void RestingOnARowWithASubmenuOpensItBeside()
    {
        var bar = new Bar();
        bar.Window.Click(bar.Title(1));
        MenuPopup popup = bar.Popup;
        bar.Window.Move(popup.RowCentre(0, 0));
        Assert.Equal(2, popup.LevelCount);
        Assert.True(popup.MenuBounds(1).Left >= popup.MenuBounds(0).Right - 2);
        bar.Window.Render();
        bar.Window.Move(popup.RowCentre(1, 1));
        Assert.Equal(1, popup.HoveredRow(1));
        bar.Window.Move(new SKPoint(550, 380)); // Off every menu: nothing changes
        Assert.Equal(2, popup.LevelCount);
        bar.Window.Move(popup.RowCentre(0, 1)); // Another row of the first menu closes it
        Assert.Equal(1, popup.LevelCount);
        bar.Window.Move(popup.RowCentre(0, 0));
        bar.Window.Click(popup.RowCentre(1, 1));
        Assert.Equal(["black"], bar.Chosen);
    }

    [Fact]
    public void APressOnARowWithASubmenuOpensIt()
    {
        var bar = new Bar();
        bar.Window.Click(bar.Title(1));
        bar.Window.Press(bar.Popup.RowCentre(0, 0));
        bar.Window.Release(bar.Popup.RowCentre(0, 0));
        Assert.Equal(2, bar.Popup.LevelCount);
        Assert.Equal(0, bar.Popup.HoveredRow(1));
        Assert.Empty(bar.Chosen);
    }

    [Fact]
    public void TheKeyboardWorksTheMenus()
    {
        var bar = new Bar();
        Assert.True(bar.MenuBar.OpenByLetter('g'));
        Assert.Equal(1, bar.MenuBar.OpenMenu);
        MenuPopup game = bar.Popup;
        Assert.Equal(0, game.HoveredRow(0));
        bar.Window.Key(Key.Right); // Into New Game's submenu
        Assert.Equal(2, game.LevelCount);
        bar.Window.Key(Key.Down);
        bar.Window.Key(Key.Up);
        bar.Window.Key(Key.Up); // Round to the last
        Assert.Equal(1, game.HoveredRow(1));
        bar.Window.Key(Key.Left); // Back out
        Assert.Equal(1, game.LevelCount);
        bar.Window.Key(Key.Right);
        bar.Window.Key(Key.Escape); // Closes the submenu only
        Assert.Equal(1, game.LevelCount);
        bar.Window.Key(Key.Down);
        bar.Window.Key(Key.Right); // Undo has no submenu: the next title
        Assert.Equal(-1, bar.MenuBar.OpenMenu); // Help: acts, and opens nothing
        Assert.Contains("help", bar.Chosen);
        Assert.True(bar.MenuBar.OpenByLetter('F'));
        bar.Window.Key(Key.Left); // The previous title, round to the last
        Assert.Equal(-1, bar.MenuBar.OpenMenu); // Off is disabled: nothing opens
        bar.MenuBar.OpenByLetter('F');
        bar.Window.Key(Key.Down); // Past the separator and the disabled row
        Assert.Equal(3, bar.Popup.HoveredRow(0));
        bar.Window.Key(Key.Return);
        Assert.Equal(["help", "exit"], bar.Chosen);
        Assert.False(bar.MenuBar.OpenByLetter('Z'));
        bar.MenuBar.OpenByLetter('G');
        bar.Window.Key(Key.Return); // New Game: its submenu opens
        Assert.Equal(2, bar.Popup.LevelCount);
        bar.Window.Key(Key.Return);
        Assert.Equal(["help", "exit", "white"], bar.Chosen);
        bar.MenuBar.OpenByLetter('G');
        bar.Window.Key(Key.A); // Any other key is the menu's, and does nothing
        bar.Window.Key(Key.Escape);
        Assert.Empty(bar.Window.Root.Overlays);
    }

    [Fact]
    public void AMenuIsKeptInsideTheWindow()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        var items = new List<MenuItem>();
        var deep = new MenuItem("Deeper");
        deep.Items.Add(new MenuItem("Leaf"));
        items.Add(deep);
        for (int i = 0; i < 4; i++)
        {
            items.Add(new MenuItem($"Item {i}"));
        }
        MenuPopup popup = MenuPopup.Open(window.Root, items, new SKPoint(280, 190));
        SKRect box = popup.MenuBounds(0);
        Assert.True(box.Right <= 300 && box.Bottom <= 200);
        window.Move(popup.RowCentre(0, 0));
        Assert.True(popup.MenuBounds(1).Right <= box.Left + 2); // No room at the right: opens at the left
        Assert.Equal(-1, popup.HoveredRow(1));
        window.Render();
        window.Key(Key.Escape);
        window.Key(Key.Escape);
        Assert.Empty(window.Root.Overlays);
        popup.Close(); // Closed already: nothing
        Assert.Throws<ArgumentNullException>(() => MenuPopup.Open(null!, items, SKPoint.Empty));
        popup.HoverFirst();
    }
}
