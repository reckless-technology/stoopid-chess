using SkiaSharp;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// A view that only holds others where a test puts them.
/// </summary>
internal sealed class TestPanel : View
{
    public T Place<T>(T child, float x, float y, float width, float height) where T : View
    {
        AddChild(child);
        child.SetBounds(SKRect.Create(x, y, width, height));
        return child;
    }

    public List<KeyEvent> UnhandledKeys { get; } = [];

    public override bool OnKeyDown(KeyEvent e)
    {
        UnhandledKeys.Add(e);
        return false;
    }
}

/// <summary>
/// The widgets the window and its dialogs are made of, drawn to a bitmap and worked by synthesized pointer, wheel, key
/// and text events: what each one shows and what it does.
/// </summary>
public class WidgetTests
{
    [Fact]
    public void APlateButtonClicksOnAReleaseOverIt()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        PlateButton button = panel.Place(new PlateButton("Start"), 20, 20, 100, 30);
        int clicks = 0;
        button.Clicked += () => clicks++;
        window.Click(button);
        Assert.Equal(1, clicks);
        window.Drag(ViewHarness.Centre(button), new SKPoint(250, 150));     // Released away from it
        Assert.Equal(1, clicks);
        window.Click(ViewHarness.Centre(button), PointerButton.Right);         // Not the left button
        Assert.Equal(1, clicks);
        button.IsEnabled = false;
        window.Click(button);
        button.Click();
        Assert.Equal(1, clicks);
        Assert.Equal("Start", button.Text);
    }

    [Fact]
    public void APlateButtonIsLitUnderThePointerAndAPictureOneDrawsItsPicture()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        PlateButton button = panel.Place(new PlateButton("Lit"), 20, 20, 100, 30);
        SKColor resting = window.ColorAt(30, 40);
        window.Move(ViewHarness.Centre(button));
        Assert.True(button.IsHovered);
        SKColor lit = window.ColorAt(30, 40);
        Assert.NotEqual(resting, lit);
        window.Move(new SKPoint(250, 150));
        Assert.False(button.IsHovered);

        using SKImage piece = Assets.Image("WhiteQueen.png");
        PlateButton picture = panel.Place(new PlateButton("") { Image = piece, IsOn = true }, 150, 20, 72, 72);
        Assert.NotEqual(Theme.Plate, window.ColorAt(186, 56));   // The queen's middle, not the plate
        picture.IsEnabled = false;
        window.Render();
    }

    [Fact]
    public void ATextFieldTakesTypingAndEditing()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        var field = panel.Place(new TextField { IsAccepted = char.IsAsciiLetter, MaximumLength = 8 }, 10, 10, 200, 30);
        int changes = 0;
        field.TextChanged += () => changes++;
        window.Click(field);
        Assert.True(field.IsFocused);
        Assert.True(window.Root.WantsTextInput);
        window.Type("abc1d");                       // The digit is refused
        Assert.Equal("abcd", field.Text);
        window.Key(Key.Left);
        window.Key(Key.Left, KeyModifiers.Shift);
        Assert.Equal((2, 3), field.Selection);
        window.Type("XY");                           // Replaces the selection
        Assert.Equal("abXYd", field.Text);
        window.Key(Key.Backspace);
        window.Key(Key.Home);
        window.Key(Key.Delete);
        Assert.Equal("bXd", field.Text);
        window.Key(Key.End);
        Assert.Equal(3, field.Caret);
        window.Key(Key.Right);
        window.Key(Key.Delete);                      // Nothing after the caret
        window.Key(Key.Home);
        window.Key(Key.Backspace);                   // Nothing before it
        Assert.Equal("bXd", field.Text);
        window.Key(Key.Right, KeyModifiers.Shift);
        window.Key(Key.Right);                       // Collapses the selection to its end
        Assert.Equal((1, 1), field.Selection);
        window.Key(Key.Left, KeyModifiers.Shift);
        window.Key(Key.Left);                        // And to its start
        Assert.Equal((0, 0), field.Selection);
        window.Key(Key.A, KeyModifiers.Primary);
        Assert.Equal((0, 3), field.Selection);
        window.Type("abcdefghijk");                  // Held to the most it takes
        Assert.Equal("abcdefgh", field.Text);
        window.Type("z");
        Assert.Equal("abcdefgh", field.Text);
        window.Key(Key.Escape);                      // Not the field's
        Assert.Contains(panel.UnhandledKeys, key => key.Key == Key.Escape);
        Assert.True(changes > 0);
        window.Render();
        Assert.Throws<ArgumentNullException>(() => field.Text = null!);
        Assert.Throws<ArgumentNullException>(() => field.OnTextInput(null!));
    }

    [Fact]
    public void ATextFieldPlacesItsCaretWhereItIsPressedAndScrollsToIt()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        var field = panel.Place(new TextField { MaximumLength = 200 }, 10, 10, 120, 30);
        bool lost = false;
        field.FocusLost += () => lost = true;
        window.Click(field);
        window.Type(new string('m', 40));            // Wider than the field: it scrolls to the caret
        window.Render();
        window.Key(Key.Home);
        window.Render();
        window.Click(new SKPoint(18, 25));           // Near the start
        Assert.InRange(field.Caret, 0, 1);
        window.Click(new SKPoint(18, 25), modifiers: KeyModifiers.Shift);
        window.Press(new SKPoint(18, 25));
        window.Move(new SKPoint(126, 25));           // Dragged across: a selection
        window.Release(new SKPoint(126, 25));
        Assert.True(field.Selection.End > field.Selection.Start);
        Assert.False(field.OnPointerDown(new PointerEvent(new SKPoint(5, 5), PointerButton.Right)));
        window.Root.Focus(null);
        Assert.True(lost);
        Assert.False(window.Root.WantsTextInput);
        window.Render();
    }

    [Fact]
    public void ANumberFieldIsHeldToItsRange()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        var number = panel.Place(new NumberField(1, 180, 5), 10, 10, 160, 30);
        Assert.Equal(1, number.Value);
        number.Value = 500;
        Assert.Equal(180, number.Value);
        Assert.Equal("180", number.Field.Text);
        window.Click(number.StepDown);
        Assert.Equal(175, number.Value);
        window.Click(number.StepUp);
        window.Click(number.StepUp);
        Assert.Equal(180, number.Value);
        window.Click(number.Field);
        window.Key(Key.Down);
        Assert.Equal(175, number.Value);
        window.Key(Key.Up);
        Assert.Equal(180, number.Value);
        window.Type("0");                            // Selected by the step, so replaced
        Assert.Equal("0", number.Field.Text);
        Assert.Equal(1, number.Value);               // Read held to the range
        window.Root.Focus(null);
        Assert.Equal("1", number.Field.Text);        // And written so once the keyboard goes
        window.Click(number.Field);
        window.Key(Key.Backspace);
        Assert.Equal(1, number.Value);               // No number: the last one held
        window.Wheel(ViewHarness.Centre(number), -2);
        Assert.Equal(6, number.Value);
        window.Wheel(ViewHarness.Centre(number), 1);
        Assert.Equal(1, number.Value);
        window.Wheel(ViewHarness.Centre(number), 0, 1);
        Assert.Equal(1, number.Value);
        window.Key(Key.Tab);
        Assert.False(number.OnKeyDown(new KeyEvent(Key.Left)));
        window.Render();
    }

    [Fact]
    public void AChoiceListChoosesByPressAndByKey()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 400, 300);
        var list = panel.Place(new ChoiceList(["One", "Two", "Three"]), 10, 10, 300, 3 * ChoiceList.RowHeight);
        Assert.Equal(3 * ChoiceList.RowHeight, list.PreferredHeight);
        Assert.Equal(-1, list.SelectedIndex);
        window.Click(list.ToRoot(list.RowCentre(1)));
        Assert.Equal(1, list.SelectedIndex);
        Assert.True(list.IsFocused);
        window.Key(Key.Down);
        window.Key(Key.Down);
        Assert.Equal(2, list.SelectedIndex);
        window.Key(Key.Up);
        Assert.Equal(1, list.SelectedIndex);
        window.Click(list.ToRoot(list.RowCentre(0)), PointerButton.Right);
        Assert.Equal(1, list.SelectedIndex);
        list.SelectedIndex = 7;
        Assert.Equal(2, list.SelectedIndex);
        window.Key(Key.Escape);
        Assert.Single(panel.UnhandledKeys);
        window.Render();
    }

    [Fact]
    public void AChoiceButtonOpensAMenuOfItsChoices()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 400, 300);
        var choice = panel.Place(new ChoiceButton(["Built-in engine", "Pawnstar"]) { SelectedIndex = 0 }, 10, 10, 220, 30);
        window.Click(choice);
        MenuPopup menu = Assert.IsType<MenuPopup>(window.Root.Overlays.Single());
        Assert.Same(menu, choice.Menu);
        window.Click(menu.RowCentre(0, 1));
        Assert.Equal(1, choice.SelectedIndex);
        Assert.Null(choice.Menu);
        Assert.Empty(window.Root.Overlays);
        window.Root.Focus(choice);
        window.Key(Key.Up);
        Assert.Equal(0, choice.SelectedIndex);
        window.Key(Key.Down);
        Assert.Equal(1, choice.SelectedIndex);
        window.Key(Key.Escape);
        Assert.Single(panel.UnhandledKeys);
        choice.SelectedIndex = -1;
        window.Render();
        window.Click(ViewHarness.Centre(choice), PointerButton.Right);
        Assert.Empty(window.Root.Overlays);
        var empty = new ChoiceButton([]);
        empty.OpenMenu();                            // In no window, and with nothing to choose
        Assert.Null(empty.Menu);
    }

    [Fact]
    public void ALabelDrawsOneLineOrWraps()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        var line = panel.Place(new LabelView("One line"), 10, 10, 200, 20);
        var wrapped = new LabelView("A message long enough to need three lines in a narrow card.\nAnd another.") { IsWrapped = true };
        float height = wrapped.HeightFor(150);
        Assert.True(height > 3 * Drawing.LineHeight(wrapped.Font));
        panel.Place(wrapped, 10, 40, 150, height);
        Assert.Equal(1 * Drawing.LineHeight(line.Font), line.HeightFor(10));
        window.Render();
        line.Text = "Changed";
        Assert.Equal("Changed", line.Text);
    }

    [Fact]
    public void ATextListScrollsToItsEndAndBothWays()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 400, 300);
        var list = panel.Place(new TextListView(), 10, 10, 200, 150);
        list.SetText("");
        Assert.Empty(list.Lines);
        list.SetText(string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"{i,2}. e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5")));
        Assert.Equal(60, list.Lines.Count);
        Assert.True(list.MaximumScrollY > 0);
        Assert.True(list.MaximumScrollX > 0);
        list.ScrollToEnd();
        Assert.Equal(list.MaximumScrollY, list.ScrollY);
        window.Render();
        window.Wheel(ViewHarness.Centre(list), -1);
        Assert.True(list.ScrollY < list.MaximumScrollY);
        window.Wheel(ViewHarness.Centre(list), 1, modifiers: KeyModifiers.Shift);
        Assert.True(list.ScrollX > 0);
        window.Wheel(ViewHarness.Centre(list), 0, -10);
        Assert.Equal(0, list.ScrollX);

        // The vertical thumb dragged to the top, and a press on the track below it moves a page
        SKRect thumb = list.VerticalThumb();
        SKPoint grip = list.ToRoot(new SKPoint(thumb.MidX, thumb.MidY));
        window.Drag(grip, new SKPoint(grip.X, 0));
        Assert.Equal(0, list.ScrollY);
        window.Click(list.ToRoot(new SKPoint(thumb.MidX, list.VerticalThumb().Bottom + 20)));
        Assert.True(list.ScrollY > 0);
        // The horizontal one dragged right, and a press on its track to the left moves back
        SKRect across = list.HorizontalThumb();
        SKPoint handle = list.ToRoot(new SKPoint(across.MidX, across.MidY));
        window.Drag(handle, new SKPoint(handle.X + 400, handle.Y));
        Assert.Equal(list.MaximumScrollX, list.ScrollX);
        window.Click(list.ToRoot(new SKPoint(list.HorizontalThumb().Left - 5, across.MidY)));
        Assert.True(list.ScrollX < list.MaximumScrollX);
        window.Move(handle);
        window.Move(new SKPoint(390, 290));
        window.Click(ViewHarness.Centre(list));      // The text itself takes no press
        window.Click(ViewHarness.Centre(list), PointerButton.Right);
        list.SetBounds(SKRect.Create(10, 10, 390, 280));
        window.Render();
        Assert.Throws<ArgumentNullException>(() => list.SetText(null!));
        Assert.Throws<ArgumentNullException>(() => list.SetLines(null!));
    }

    [Fact]
    public void TheThemesShadesAndTheTextFittingFollowTheToolkit()
    {
        Assert.Equal(new SKColor(0, 0, 0), new SKColor(0, 0, 0).Brighter(0));
        Assert.Equal(new SKColor(127, 127, 127), new SKColor(0, 0, 0).Brighter(1));
        Assert.Equal(new SKColor(100, 50, 25), new SKColor(200, 100, 50).Darker(1));
        Assert.Equal(255, Theme.AmberDim.WithMultipliedAlpha(1.8f).Alpha);
        Assert.Equal(128, Theme.Ink.WithOpacity(0.5f).Alpha);

        Assert.Equal("short", Drawing.Fit("short", Fonts.Body, 200));
        string cut = Drawing.Fit("a line much too long for the room it is given", Fonts.Body, 80);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        Assert.True(Drawing.Measure(Fonts.Body, cut) <= 80);
        Assert.Equal("", Drawing.Fit("wide", Fonts.Body, 1));
        Assert.Equal(["one", "two"], Drawing.Wrap("one\ntwo", Fonts.Body, 500));
        Assert.Equal(["unbreakablewordthatiswide"], Drawing.Wrap("unbreakablewordthatiswide", Fonts.Body, 10));
        Assert.Equal(0, Drawing.Measure(Fonts.Body, ""));
        Assert.Throws<FileNotFoundException>(() => Assets.Open("Missing.png"));
        Assert.Equal("Ctrl+Shift+N".Replace("Ctrl", Shortcut.PrimaryName, StringComparison.Ordinal),
            Shortcut.Text(Key.N, KeyModifiers.Primary | KeyModifiers.Shift));
        Assert.Equal("Alt+F", Shortcut.Text(Key.F, KeyModifiers.Alt));
    }

    [Fact]
    public void TheRootSendsTabBetweenFieldsAndIgnoresASecondPress()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 400, 300);
        var first = panel.Place(new TextField(), 10, 10, 100, 30);
        var second = panel.Place(new TextField(), 10, 50, 100, 30);
        var hidden = panel.Place(new TextField { IsVisible = false }, 10, 90, 100, 30);
        panel.Place(new TextField { IsEnabled = false }, 10, 130, 100, 30);
        window.Key(Key.Tab);
        Assert.True(first.IsFocused);
        window.Key(Key.Tab);
        Assert.True(second.IsFocused);
        window.Key(Key.Tab);
        Assert.True(first.IsFocused);
        window.Key(Key.Tab, KeyModifiers.Shift);
        Assert.True(second.IsFocused);
        window.Key(Key.Tab, KeyModifiers.Shift);
        Assert.True(first.IsFocused);
        window.Root.Focus(null);
        window.Key(Key.Tab, KeyModifiers.Shift);
        Assert.True(second.IsFocused);
        Assert.False(hidden.IsFocused);
        panel.UnhandledKeys.Clear();
        window.Key(Key.Tab, KeyModifiers.Primary);    // Not a focus move
        Assert.Single(panel.UnhandledKeys);

        window.Press(ViewHarness.Centre(first));
        window.Press(ViewHarness.Centre(second));    // While the first is held: ignored
        Assert.True(first.IsFocused);
        window.Release(ViewHarness.Centre(first));
        window.Release(ViewHarness.Centre(first));   // Nothing held
        window.Root.PointerLeave();
        window.Press(new SKPoint(-5, -5));           // Outside the window
        window.Type("ignored");
        Assert.Equal("", second.Text);
        window.Root.Content = new TestPanel();       // Content replaced
        window.Key(Key.Tab);                         // Nothing to focus
        window.Root.Content = null;
        window.Root.Focus(null);
        window.Key(Key.Tab);
        window.Type("nowhere");
        window.Render();
        panel.RemoveChild(first);
        Assert.Null(first.Parent);
        Assert.Throws<ArgumentNullException>(() => panel.AddChild(null!));
        Assert.Throws<ArgumentNullException>(() => panel.RemoveChild(null!));
    }
}
