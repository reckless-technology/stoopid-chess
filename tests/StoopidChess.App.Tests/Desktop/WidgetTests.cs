using SkiaSharp;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// A view that only holds others where a test puts them.
/// </summary>
internal sealed class TestPanel : View
{
    public T Place<T>(T child, float x, float y, float width, float height)
        where T : View
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
/// A blank view that can take the keyboard focus, standing in for an entry field.
/// </summary>
internal sealed class FocusableView : View
{
    public override bool CanFocus => true;

    public override bool OnPointerDown(PointerEvent e) => true;
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
        window.Drag(ViewHarness.Centre(button), new SKPoint(250, 150)); // Released away from it
        Assert.Equal(1, clicks);
        window.Click(ViewHarness.Centre(button), PointerButton.Right); // Not the left button
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
        Assert.NotEqual(Theme.Plate, window.ColorAt(186, 56)); // The queen's middle, not the plate
        picture.IsEnabled = false;
        window.Render();
    }

    [Fact]
    public void ALabelDrawsOneLineOrWraps()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 300, 200);
        var line = panel.Place(new LabelView("One line"), 10, 10, 200, 20);
        var wrapped = new LabelView("A message long enough to need three lines in a narrow card.\nAnd another.")
        {
            IsWrapped = true,
        };
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
        list.SetText(
            string.Join(
                "\r\n",
                Enumerable.Range(1, 60).Select(i => $"{i, 2}. e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5")
            )
        );
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
        window.Click(ViewHarness.Centre(list)); // The text itself takes no press
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
        Assert.Equal(
            "Ctrl+Shift+N".Replace("Ctrl", Shortcut.PrimaryName, StringComparison.Ordinal),
            Shortcut.Text(Key.N, KeyModifiers.Primary | KeyModifiers.Shift)
        );
        Assert.Equal("Alt+F", Shortcut.Text(Key.F, KeyModifiers.Alt));
    }

    [Fact]
    public void TheRootSendsTabBetweenFieldsAndIgnoresASecondPress()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 400, 300);
        var first = panel.Place(new FocusableView(), 10, 10, 100, 30);
        var second = panel.Place(new FocusableView(), 10, 50, 100, 30);
        var hidden = panel.Place(new FocusableView { IsVisible = false }, 10, 90, 100, 30);
        panel.Place(new FocusableView { IsEnabled = false }, 10, 130, 100, 30);
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
        window.Key(Key.Tab, KeyModifiers.Primary); // Not a focus move
        Assert.Single(panel.UnhandledKeys);

        window.Press(ViewHarness.Centre(first));
        window.Press(ViewHarness.Centre(second)); // While the first is held: ignored
        Assert.True(first.IsFocused);
        window.Release(ViewHarness.Centre(first));
        window.Release(ViewHarness.Centre(first)); // Nothing held
        window.Root.PointerLeave();
        window.Press(new SKPoint(-5, -5)); // Outside the window
        window.Type("ignored");
        window.Root.Content = new TestPanel(); // Content replaced
        window.Key(Key.Tab); // Nothing to focus
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
