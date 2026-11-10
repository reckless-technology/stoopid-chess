using SkiaSharp;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The modal dialog's frame: the card centred over a veil that takes every press, the buttons along the bottom,
/// Return and Escape, Tab between fields, and the task that completes when it closes.
/// </summary>
public class DialogFrameTests
{
    private sealed class TwoFieldDialog : DialogView
    {
        public TwoFieldDialog(bool hasCancel = true) : base("Two fields")
        {
            AddChild(First);
            AddChild(Second);
            AddButton("OK", () =>
            {
                Answer = "ok";
                Close();
            }, isDefault: true);
            if (hasCancel)
            {
                AddButton("Cancel", () =>
                {
                    Answer = "cancel";
                    Close();
                }, isCancel: true);
            }
        }

        public FocusableView First { get; } = new();

        public FocusableView Second { get; } = new();

        public string? Answer { get; private set; }

        protected override SKSize ContentSize => new(240, 70);

        protected override void LayoutContent(SKRect area)
        {
            First.SetBounds(SKRect.Create(area.Left, area.Top, area.Width, 30));
            Second.SetBounds(SKRect.Create(area.Left, area.Top + 40, area.Width, 30));
        }
    }

    [Fact]
    public async Task ReturnPressesTheDefaultButtonAndTheDialogCloses()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 600, 400);
        var dialog = new TwoFieldDialog();
        Task shown = dialog.ShowAsync(window.Root);
        Assert.True(dialog.First.IsFocused);
        Assert.False(shown.IsCompleted);
        Assert.Equal(new SKPoint(300, 200), new SKPoint(dialog.Card.MidX, dialog.Card.MidY));
        Assert.Equal(Theme.Window, window.ColorAt(dialog.Card.Left + 5, dialog.Card.MidY));
        Assert.NotEqual(Theme.Window, window.ColorAt(5, 5));                     // The veil darkens the rest
        window.Key(Key.Tab);
        Assert.True(dialog.Second.IsFocused);
        window.Key(Key.Tab, KeyModifiers.Shift);
        Assert.True(dialog.First.IsFocused);
        window.Click(new SKPoint(5, 5));                                         // The veil: nothing happens
        Assert.False(dialog.IsClosed);
        window.Key(Key.F, KeyModifiers.Primary);                                 // Modal: no key passes it
        Assert.Empty(panel.UnhandledKeys);
        window.Key(Key.Return);
        await shown;
        Assert.Equal("ok", dialog.Answer);
        Assert.True(dialog.IsClosed);
        Assert.Empty(window.Root.Overlays);
    }

    [Fact]
    public async Task EscapePressesTheCancelButtonOrClosesTheDialog()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 600, 400);
        var dialog = new TwoFieldDialog();
        Task shown = dialog.ShowAsync(window.Root);
        Assert.Equal(["OK", "Cancel"], dialog.Buttons.Select(button => button.Text));
        Assert.True(dialog.Buttons[0].Bounds.Right < dialog.Buttons[1].Bounds.Left);
        window.Key(Key.Escape);
        await shown;
        Assert.Equal("cancel", dialog.Answer);

        var bare = new TwoFieldDialog(hasCancel: false);
        Task bareShown = bare.ShowAsync(window.Root);
        Assert.Same(bare.DefaultButton, bare.Buttons.Single());
        Assert.Null(bare.CancelButton);
        window.Key(Key.Escape);
        await bareShown;
        Assert.Null(bare.Answer);
        await Assert.ThrowsAsync<ArgumentNullException>(() => bare.ShowAsync(null!));
    }

    [Fact]
    public async Task AClickOnAButtonAnswers()
    {
        var panel = new TestPanel();
        using var window = new ViewHarness(panel, 600, 400);
        var dialog = new TwoFieldDialog();
        Task shown = dialog.ShowAsync(window.Root);
        window.Click(dialog.Buttons[1]);
        await shown;
        Assert.Equal("cancel", dialog.Answer);
    }
}
