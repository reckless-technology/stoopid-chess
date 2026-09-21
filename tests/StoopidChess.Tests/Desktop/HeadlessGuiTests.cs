using Avalonia.Headless;
using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// One headless Avalonia session for all the GUI tests; every touch of a control is dispatched
/// onto its UI thread.
/// </summary>
public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(App));

    public void Dispose() => Session.Dispose();
}

/// <summary>
/// Smoke tests of the GUI layer under the headless platform: the windows and dialogs construct,
/// their XAML compiles and loads, and their defaults are sane. The game and engine logic behind
/// them is covered by the controller tests.
/// </summary>
public class HeadlessGuiTests(HeadlessSessionFixture fixture) : IClassFixture<HeadlessSessionFixture>
{
    private Task<T> OnUiThread<T>(Func<T> test) => fixture.Session.Dispatch(test, CancellationToken.None);

    [Fact]
    public async Task TheMainWindowConstructs()
    {
        string title = await OnUiThread(() =>
        {
            using var window = new MainWindow();
            return window.Title ?? "";
        });
        Assert.Equal("Stoopid Chess", title);
    }

    [Fact]
    public async Task TheNewGameDialogStartsUnconfirmed()
    {
        (bool confirmed, bool custom, TimeControl? choice) = await OnUiThread(() =>
        {
            var dialog = new NewGameDialog(current: null);
            return (dialog.Confirmed, dialog.CustomRequested, dialog.Choice);
        });
        Assert.False(confirmed);
        Assert.False(custom);
        Assert.Null(choice);
    }

    [Fact]
    public async Task ThePromotionDialogDefaultsToAQueen()
    {
        Piece choice = await OnUiThread(() => new PromotionDialog(Color.White).Choice);
        Assert.Equal(Piece.Queen, choice);
    }

    [Fact]
    public async Task TheClockAndMatchDialogsConstruct()
    {
        bool constructed = await OnUiThread(() =>
        {
            _ = new CustomTimeControlDialog(TimeControl.OfMinutes(15, 10));
            _ = new MatchDialog(["Built-in engine", "Pawnstar"]);
            return true;
        });
        Assert.True(constructed);
    }

    [Fact]
    public async Task TheAnalysisWindowAppendsAndClears()
    {
        bool survived = await OnUiThread(() =>
        {
            var window = new AnalysisWindow();
            window.Append("d 12  +0.34  1,234 n  e2e4 e7e5");
            window.Append("d 13  +0.31  5,678 n  e2e4 e7e5 g1f3");
            window.Clear();
            window.Append("d  1  +0.10  42 n  d2d4");
            window.Close();
            return true;
        });
        Assert.True(survived);
    }
}
