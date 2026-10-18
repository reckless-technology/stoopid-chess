using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The GUI's dialogs under the headless platform: what each one starts with, and what it hands back when
/// its buttons are pressed - the choices the main window acts on.
/// </summary>
[Collection(HeadlessGui.Name)]
public class DialogTests(HeadlessSessionFixture fixture)
{
    private Task<T> OnUiThread<T>(Func<T> test) => fixture.Session.Dispatch(test, CancellationToken.None);

    private static T Find<T>(Window window, Func<T, bool>? match = null) where T : Control =>
        window.GetLogicalDescendants().OfType<T>().First(control => match?.Invoke(control) ?? true);

    private static List<T> FindAll<T>(Window window) where T : Control =>
        [.. window.GetLogicalDescendants().OfType<T>()];

    private static void Press(Window window, string content) =>
        Find<Button>(window, b => Equals(b.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public async Task TheNewGameDialogOffersTheDefaultClock()
    {
        (bool confirmed, bool custom, TimeControl? choice) = await OnUiThread(() =>
        {
            var dialog = new NewGameDialog(current: null);
            Press(dialog, "Start game");
            return (dialog.Confirmed, dialog.CustomRequested, dialog.Choice);
        });
        Assert.True(confirmed);
        Assert.False(custom);
        Assert.Equal(TimeControl.Standard(40, 5), choice);
    }

    [Fact]
    public async Task TheNewGameDialogStartsOnTheCurrentClock()
    {
        TimeControl? choice = await OnUiThread(() =>
        {
            var dialog = new NewGameDialog(TimeControl.OfMinutes(5, 5));
            Press(dialog, "Start game");
            return dialog.Choice;
        });
        Assert.Equal(TimeControl.OfMinutes(5, 5), choice);
    }

    [Fact]
    public async Task TheNewGameDialogAsksForACustomClockItCannotOffer()
    {
        (bool confirmed, bool custom, TimeControl? choice) = await OnUiThread(() =>
        {
            var dialog = new NewGameDialog(TimeControl.OfMinutes(15, 10));
            Press(dialog, "Start game");
            return (dialog.Confirmed, dialog.CustomRequested, dialog.Choice);
        });
        Assert.True(confirmed);
        Assert.True(custom);
        Assert.Null(choice);
    }

    [Fact]
    public async Task TheNewGameDialogGoesNowhereWithoutAChoiceOrWhenCancelled()
    {
        (bool noChoice, bool cancelled) = await OnUiThread(() =>
        {
            var dialog = new NewGameDialog(current: null);
            foreach (RadioButton button in FindAll<RadioButton>(dialog))
            {
                button.IsChecked = false;
            }
            Press(dialog, "Start game");
            var other = new NewGameDialog(current: null);
            Press(other, "Cancel");
            return (dialog.Confirmed, other.Confirmed);
        });
        Assert.False(noChoice);
        Assert.False(cancelled);
    }

    [Fact]
    public async Task TheCustomClockDialogReturnsItsFields()
    {
        (TimeControl? kept, TimeControl? session, TimeControl? cancelled) = await OnUiThread(() =>
        {
            var dialog = new CustomTimeControlDialog(TimeControl.OfMinutes(15, 10));
            Press(dialog, "OK");
            var withSession = new CustomTimeControlDialog(null);
            FindAll<NumericUpDown>(withSession)[2].Value = 40;
            Press(withSession, "OK");
            var other = new CustomTimeControlDialog(null);
            Press(other, "Cancel");
            return (dialog.Choice, withSession.Choice, other.Choice);
        });
        Assert.Equal(TimeControl.OfMinutes(15, 10), kept);
        Assert.Equal(new TimeControl(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(3), 40), session);
        Assert.Null(cancelled);
    }

    [Fact]
    public async Task TheMatchDialogPairsTwoEngines()
    {
        ((int, int)? pair, (int, int)? alone, (int, int)? unselected, (int, int)? cancelled) = await OnUiThread(() =>
        {
            var dialog = new MatchDialog(["Built-in engine", "Pawnstar"]);
            Press(dialog, "Start match");
            var single = new MatchDialog(["Built-in engine"]);
            Press(single, "Start match");
            var none = new MatchDialog(["Built-in engine", "Pawnstar"]);
            Find<ComboBox>(none).SelectedIndex = -1;
            Press(none, "Start match");
            var other = new MatchDialog(["Built-in engine", "Pawnstar"]);
            Press(other, "Cancel");
            return (dialog.Choice, single.Choice, none.Choice, other.Choice);
        });
        Assert.Equal((0, 1), pair);
        Assert.Equal((0, 0), alone);
        Assert.Null(unselected);
        Assert.Null(cancelled);
        await Assert.ThrowsAsync<ArgumentNullException>(() => OnUiThread(() => new MatchDialog(null!)));
    }

    [Fact]
    public async Task TheEngineOptionsDialogEditsHashAndThreadsAndKeepsTheRest()
    {
        (string title, Dictionary<string, string>? saved) = await OnUiThread(() =>
        {
            var dialog = new EngineOptionsDialog("Pawnstar", new Dictionary<string, string> { ["Hash"] = "64", ["Threads"] = "4", ["Ponder"] = "false" });
            List<NumericUpDown> fields = FindAll<NumericUpDown>(dialog);
            fields[0].Value = 128;
            Press(dialog, "OK");
            return (dialog.Title ?? "", dialog.Saved);
        });
        Assert.Equal("Pawnstar options", title);
        Assert.Equal(new Dictionary<string, string> { ["Ponder"] = "false", ["Hash"] = "128", ["Threads"] = "4" }, saved);
    }

    [Fact]
    public async Task TheEngineOptionsDialogLeavesZeroAndNonsenseToTheEngine()
    {
        (decimal? hash, decimal? threads, Dictionary<string, string>? saved, Dictionary<string, string>? cancelled) = await OnUiThread(() =>
        {
            var dialog = new EngineOptionsDialog("Engine", new Dictionary<string, string> { ["Hash"] = "lots", ["Threads"] = "999" });
            List<NumericUpDown> fields = FindAll<NumericUpDown>(dialog);
            (decimal? hashValue, decimal? threadsValue) = (fields[0].Value, fields[1].Value);
            fields[1].Value = 0;
            Press(dialog, "OK");
            var other = new EngineOptionsDialog("Engine", new Dictionary<string, string>());
            Press(other, "Cancel");
            return (hashValue, threadsValue, dialog.Saved, other.Saved);
        });
        Assert.Equal(0m, hash);
        Assert.Equal(64m, threads);          // Clamped to the field's range
        Assert.Equal(new Dictionary<string, string>(), saved);
        Assert.Null(cancelled);
        await Assert.ThrowsAsync<ArgumentNullException>(() => OnUiThread(() => new EngineOptionsDialog("Engine", null!)));
    }

    [Fact]
    public async Task ThePromotionDialogReturnsThePiecePressed()
    {
        Piece choice = await OnUiThread(() =>
        {
            var dialog = new PromotionDialog(Color.Black);
            FindAll<Button>(dialog)[3].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return dialog.Choice;
        });
        Assert.Equal(Piece.Knight, choice);
    }

    [Fact]
    public async Task TheAnalysisWindowKeepsItsLatestLinesAndClears()
    {
        (string? full, string? cleared) = await OnUiThread(() =>
        {
            var window = new AnalysisWindow();
            for (int i = 0; i < 450; i++)
            {
                window.Append($"line {i}");
            }
            TextBox output = Find<TextBox>(window);
            string? text = output.Text;
            Press(window, "Clear");
            return (text, output.Text);
        });
        string[] lines = full!.Split('\n');
        Assert.Equal(400, lines.Length);
        Assert.Equal("line 50", lines[0]);
        Assert.Equal("line 449", lines[^1]);
        Assert.Equal("", cleared);
    }

    [Fact]
    public async Task AMessageDialogShowsItsMessageAndClosesOnOk()
    {
        (string? title, string? message, bool closed) = await fixture.Session.Dispatch(async () =>
        {
            var owner = new Window();
            owner.Show();
            Task shown = MessageDialog.ShowAsync(owner, "Engine", "The engine could not be started.");
            Window dialog = owner.OwnedWindows[0];
            string? text = Find<TextBlock>(dialog).Text;
            Press(dialog, "OK");
            await shown.WaitAsync(TimeSpan.FromSeconds(10));
            owner.Close();
            return (dialog.Title, text, shown.IsCompleted);
        }, CancellationToken.None);
        Assert.Equal("Engine", title);
        Assert.Equal("The engine could not be started.", message);
        Assert.True(closed);
    }

    [Fact]
    public void TheVersionIsTheBuildsThreeNumbers()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", StoopidChess.Desktop.Program.Version);
    }
}
