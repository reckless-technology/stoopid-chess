using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Desktop.Dialogs;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The GUI's dialogs, each open over a window: what it starts with, and what it hands back when its buttons are
/// pressed - the choices the main view acts on - and the Analysis window's lines.
/// </summary>
public sealed class DialogTests : IDisposable
{
    private readonly ViewHarness window = new(new TestPanel());

    public void Dispose() => window.Dispose();

    private T Open<T>(T dialog) where T : DialogView
    {
        _ = dialog.ShowAsync(window.Root);
        window.Render();
        return dialog;
    }

    private void Press(DialogView dialog, string text) => window.Click(dialog.Buttons.Single(button => button.Text == text));

    [Fact]
    public void TheNewGameDialogOffersTheDefaultClock()
    {
        NewGameDialog dialog = Open(new NewGameDialog(current: null));
        Assert.False(dialog.Confirmed);
        Press(dialog, "Start game");
        Assert.True(dialog.Confirmed);
        Assert.False(dialog.CustomRequested);
        Assert.Equal(TimeControl.Standard(40, 5), dialog.Choice);
    }

    [Fact]
    public void TheNewGameDialogStartsOnTheCurrentClock()
    {
        NewGameDialog dialog = Open(new NewGameDialog(TimeControl.OfMinutes(5, 5)));
        Assert.Equal(3, dialog.Choices.SelectedIndex);
        window.Key(Key.Up);                                     // The list holds the keyboard
        Press(dialog, "Start game");
        Assert.Equal(TimeControl.Standard(40, 1), dialog.Choice);
    }

    [Fact]
    public void TheNewGameDialogAsksForACustomClockItCannotOffer()
    {
        NewGameDialog dialog = Open(new NewGameDialog(TimeControl.OfMinutes(15, 10)));
        Press(dialog, "Start game");
        Assert.True(dialog.Confirmed);
        Assert.True(dialog.CustomRequested);
        Assert.Null(dialog.Choice);
    }

    [Fact]
    public void TheNewGameDialogGoesNowhereWithoutAChoiceOrWhenCancelled()
    {
        NewGameDialog dialog = Open(new NewGameDialog(current: null));
        dialog.Choices.SelectedIndex = -1;
        Press(dialog, "Start game");
        NewGameDialog other = Open(new NewGameDialog(current: null));
        Press(other, "Cancel");
        Assert.False(dialog.Confirmed);
        Assert.False(other.Confirmed);
    }

    [Fact]
    public void TheCustomClockDialogReturnsItsFields()
    {
        CustomTimeControlDialog dialog = Open(new CustomTimeControlDialog(TimeControl.OfMinutes(15, 10)));
        Assert.Equal(15, dialog.Minutes.Value);
        Press(dialog, "OK");
        Assert.Equal(TimeControl.OfMinutes(15, 10), dialog.Choice);

        CustomTimeControlDialog withSession = Open(new CustomTimeControlDialog(null));
        Assert.True(withSession.Minutes.Field.IsFocused);
        window.Type("7");                                      // Replaces the 5 taking the keyboard selected
        window.Key(Key.Tab);
        window.Key(Key.Tab);
        window.Type("40");
        window.Key(Key.Return);
        Assert.Equal(new TimeControl(TimeSpan.FromMinutes(7), TimeSpan.FromSeconds(3), 40), withSession.Choice);

        CustomTimeControlDialog other = Open(new CustomTimeControlDialog(null));
        Press(other, "Cancel");
        Assert.Null(other.Choice);
    }

    [Fact]
    public void TheMatchDialogPairsTwoEngines()
    {
        MatchDialog dialog = Open(new MatchDialog(["Built-in engine", "Pawnstar"]));
        Press(dialog, "Start match");
        Assert.Equal((0, 1), dialog.Choice);

        MatchDialog single = Open(new MatchDialog(["Built-in engine"]));
        Press(single, "Start match");
        Assert.Equal((0, 0), single.Choice);

        MatchDialog none = Open(new MatchDialog(["Built-in engine", "Pawnstar"]));
        none.White.SelectedIndex = -1;
        Press(none, "Start match");
        Assert.Null(none.Choice);

        MatchDialog chosen = Open(new MatchDialog(["Built-in engine", "Pawnstar"]));
        window.Click(chosen.White);
        MenuPopup menu = chosen.White.Menu!;
        window.Click(menu.RowCentre(0, 1));
        Press(chosen, "Start match");
        Assert.Equal((1, 1), chosen.Choice);

        MatchDialog other = Open(new MatchDialog(["Built-in engine", "Pawnstar"]));
        Press(other, "Cancel");
        Assert.Null(other.Choice);
        Assert.Throws<ArgumentNullException>(() => new MatchDialog(null!));
    }

    [Fact]
    public void TheEngineOptionsDialogEditsHashAndThreadsAndKeepsTheRest()
    {
        EngineOptionsDialog dialog = Open(new EngineOptionsDialog("Pawnstar",
            new Dictionary<string, string> { ["Hash"] = "64", ["Threads"] = "4", ["Ponder"] = "false" }));
        Assert.Equal("Pawnstar options", dialog.Title);
        dialog.Hash.Value = 128;
        Press(dialog, "OK");
        Assert.Equal(new Dictionary<string, string> { ["Ponder"] = "false", ["Hash"] = "128", ["Threads"] = "4" }, dialog.Saved);
    }

    [Fact]
    public void TheEngineOptionsDialogLeavesZeroAndNonsenseToTheEngine()
    {
        EngineOptionsDialog dialog = Open(new EngineOptionsDialog("Engine",
            new Dictionary<string, string> { ["Hash"] = "lots", ["Threads"] = "999" }));
        Assert.Equal(0, dialog.Hash.Value);
        Assert.Equal(64, dialog.Threads.Value);                // Held to the field's range
        dialog.Threads.Value = 0;
        Press(dialog, "OK");
        Assert.Equal(new Dictionary<string, string>(), dialog.Saved);

        EngineOptionsDialog other = Open(new EngineOptionsDialog("Engine", new Dictionary<string, string>()));
        Press(other, "Cancel");
        Assert.Null(other.Saved);
        Assert.Throws<ArgumentNullException>(() => new EngineOptionsDialog("Engine", null!));
    }

    [Fact]
    public void ThePromotionDialogReturnsThePiecePressedOrAQueen()
    {
        PromotionDialog dialog = Open(new PromotionDialog(Color.Black));
        Assert.Equal(Piece.Queen, dialog.Choice);
        window.Click(dialog.Pieces[3]);
        Assert.Equal(Piece.Knight, dialog.Choice);

        PromotionDialog closed = Open(new PromotionDialog(Color.White));
        window.Key(Key.Escape);
        Assert.True(closed.IsClosed);
        Assert.Equal(Piece.Queen, closed.Choice);
    }

    [Fact]
    public async Task AMessageDialogShowsItsMessageAndClosesOnOk()
    {
        Task shown = MessageDialog.ShowAsync(window.Root, "Engine", "The engine could not be started.");
        MessageDialog dialog = Assert.IsType<MessageDialog>(window.Root.Overlays.Single());
        window.Render();
        Assert.Equal("Engine", dialog.Title);
        Assert.Equal("The engine could not be started.", dialog.Message.Text);
        Press(dialog, "OK");
        await shown;
        Assert.Empty(window.Root.Overlays);
    }

    [Fact]
    public void TheAnalysisViewKeepsItsLatestLinesAndClears()
    {
        var analysis = new AnalysisView();
        using var analysisWindow = new ViewHarness(analysis, 720, 400);
        for (int i = 0; i < 450; i++)
        {
            analysis.Append($"line {i}");
        }
        IReadOnlyList<string> lines = analysis.Output.Lines;
        Assert.Equal(AnalysisView.MaximumLines, lines.Count);
        Assert.Equal("line 50", lines[0]);
        Assert.Equal("line 449", lines[^1]);
        Assert.Equal(analysis.Output.MaximumScrollY, analysis.Output.ScrollY);
        analysisWindow.Render();
        analysisWindow.Click(analysis.ClearButton);
        Assert.Empty(analysis.Output.Lines);
    }

    [Fact]
    public void TheVersionIsTheBuildsThreeNumbers()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", Program.Version);
    }
}
