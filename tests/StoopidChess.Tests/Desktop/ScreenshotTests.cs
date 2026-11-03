using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Desktop.Dialogs;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The window drawn in its main states through the raster path the SDL host presents: the game, a menu open, a
/// dialog open, a drag with its hints, and the Analysis window. Each must draw; with the environment variable
/// STOOPID_CHESS_SCREENSHOTS naming a folder, each is also written there as a PNG, for a person to look at.
/// </summary>
public class ScreenshotTests
{
    private static readonly string[] OpeningMoves =
        ["e2e4", "e7e5", "g1f3", "b8c6", "f1b5", "a7a6", "b5c6", "d7c6", "e1g1", "f7f6", "d2d4", "e5d4", "f3d4", "c6c5", "d4e2", "d8d1", "f1d1"];

    private static (ViewHarness Window, MainView View, GameController Controller) MainWindow()
    {
        var controller = new GameController();
        Assert.True(controller.Load(new SavedGame { Moves = [.. OpeningMoves], HumanColor = Color.Black }));
        controller.SetTimeControl(TimeControl.Standard(40, 5));
        var view = new MainView(new FakePlatform(), controller, new EngineRoster(), Path.Combine(Path.GetTempPath(), "unused-engines.json"));
        return (new ViewHarness(view), view, controller);
    }

    private static void Save(ViewHarness window, string name)
    {
        SKBitmap bitmap = window.Render();
        if (Environment.GetEnvironmentVariable("STOOPID_CHESS_SCREENSHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream file = File.Create(Path.Combine(folder, $"{name}.png"));
            png.SaveTo(file);
        }
    }

    [Fact]
    public void TheMainWindowDraws()
    {
        (ViewHarness window, MainView view, GameController controller) = MainWindow();
        using (window)
        using (view)
        {
            Save(window, "main-window");
            Assert.Equal(new SKColor(0xff1b1a1d), window.Bitmap.GetPixel(400, 5));     // The menu bar
            window.Root.Resize(680, 540);
            Save(window, "main-window-minimum");
            window.Root.Resize(1400, 600);
            Save(window, "main-window-wide");
            window.Root.Resize(860, 660);
        }
    }

    [Fact]
    public void TheWindowDrawsWithAMenuOpen()
    {
        (ViewHarness window, MainView view, _) = MainWindow();
        using (window)
        using (view)
        {
            window.Click(MenuTitle(view, 2));
            MenuPopup popup = view.MenuBar.Popup!;
            window.Move(popup.RowCentre(0, 0));
            window.Move(popup.RowCentre(1, 0));
            Save(window, "menu-open");
            Assert.Equal(2, popup.LevelCount);
        }
    }

    [Fact]
    public void TheWindowDrawsWithADialogOpen()
    {
        (ViewHarness window, MainView view, _) = MainWindow();
        using (window)
        using (view)
        {
            window.Key(Key.N, KeyModifiers.Primary);
            Assert.IsType<NewGameDialog>(window.Root.Overlays.Single());
            Save(window, "new-game-dialog");
            window.Key(Key.Escape);
            _ = new CustomTimeControlDialog(TimeControl.OfMinutes(15, 10)).ShowAsync(window.Root);
            Save(window, "custom-time-control-dialog");
        }
    }

    [Fact]
    public void TheWindowDrawsADragWithItsHints()
    {
        var controller = new GameController();
        Assert.True(controller.Load(new SavedGame { Moves = ["e2e4", "d7d5"], HumanColor = Color.White }));
        using var view = new MainView(new FakePlatform(), controller, new EngineRoster(), Path.Combine(Path.GetTempPath(), "unused-engines.json"));
        using var window = new ViewHarness(view);
        SKPoint from = view.Board.ToRoot(view.Board.SquareCentre(Square.Parse("e4")));
        window.Move(from);
        window.Press(from);
        window.Move(new SKPoint(from.X - 20, from.Y - 40));
        Save(window, "drag-hints");
        Assert.Equal(Square.Parse("e4"), view.Board.DragFrom);
        window.Release(from);
    }

    [Fact]
    public void TheAnalysisWindowDraws()
    {
        var analysis = new AnalysisView();
        using var window = new ViewHarness(analysis, 720, 400);
        string[] reports =
        [
            "info depth 10 seldepth 14 score cp 34 nodes 123456 nps 900000 pv e2e4 e7e5 g1f3 b8c6 f1b5 a7a6",
            "info depth 11 seldepth 16 score cp 29 nodes 287654 nps 910000 pv e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6",
            "info depth 12 seldepth 18 score cp 31 nodes 612345 nps 905000 pv e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 c1e3 e7e5",
        ];
        foreach (string report in reports)
        {
            analysis.Append(GameText.InfoLine(report)!);
        }
        Save(window, "analysis-window");
        Assert.Equal(3, analysis.Output.Lines.Count);
    }

    private static SKPoint MenuTitle(MainView view, int index)
    {
        SKRect title = view.MenuBar.TitleBounds(index);
        return view.MenuBar.ToRoot(new SKPoint(title.MidX, title.MidY));
    }
}
