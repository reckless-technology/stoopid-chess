using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The board and the capture bin under the headless platform: drawn in every state (a drag with its
/// hints, a check, the last move, flipped, editing), and the board's pointer input turned into the
/// squares the main window acts on. The board is 400 pixels square, so a square is 50.
/// </summary>
[Collection(HeadlessGui.Name)]
public class BoardControlTests(HeadlessSessionFixture fixture)
{
    private const int Size = 400;

    private Task<T> OnUiThread<T>(Func<T> test) => fixture.Session.Dispatch(test, CancellationToken.None);

    /// <summary>
    /// A board on a shown window, laid out, with the events it raised recorded.
    /// </summary>
    private sealed class Rig
    {
        public Rig(GameController controller)
        {
            Controller = controller;
            Board = new BoardControl { Controller = controller };
            Window = new Window { Width = Size, Height = Size, Content = Board };
            Board.MoveDropped += (from, to) => Dropped.Add((from, to));
            Board.EditRequested += square => EditRequests.Add(square);
            Board.EditingIncomplete += () => IncompleteEdits++;
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public GameController Controller { get; }

        public BoardControl Board { get; }

        public Window Window { get; }

        public List<(Square From, Square To)> Dropped { get; } = [];

        public List<Square> EditRequests { get; } = [];

        public int IncompleteEdits { get; private set; }

        /// <summary>
        /// The centre of a square in the board's pixels, as the board is oriented.
        /// </summary>
        public Point Centre(string square)
        {
            var s = Square.Parse(square);
            bool flipped = Controller.IsBoardFlipped;
            int x = flipped ? 7 - s.File : s.File;
            int y = flipped ? s.Rank : 7 - s.Rank;
            return new Point((x * 50) + 25, (y * 50) + 25);
        }

        public void Drag(string from, string to, MouseButton button = MouseButton.Left)
        {
            Window.MouseDown(Centre(from), button);
            Window.MouseMove(Centre(to));
            Render();
            Window.MouseUp(Centre(to), button);
        }

        public void Render()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size));
            bitmap.Render(Board);
        }

        public void Close() => Window.Close();
    }

    private static GameController Game(string fen, Color human = Color.White)
    {
        var controller = new GameController();
        Assert.True(controller.Load(new SavedGame { StartFen = fen, HumanColor = human }));
        return controller;
    }

    [Fact]
    public async Task DraggingAPieceDropsItOnTheTargetSquare()
    {
        List<(Square, Square)> dropped = await OnUiThread(() =>
        {
            using GameController controller = Game(Position.StartFen);
            var rig = new Rig(controller);
            Assert.Equal(new Rect(0, 0, Size, Size), rig.Board.Bounds);
            rig.Drag("e2", "e4");
            rig.Close();
            return rig.Dropped;
        });
        Assert.Equal([(Square.Parse("e2"), Square.Parse("e4"))], dropped);
    }

    [Fact]
    public async Task ACaptureAndAnEmptySquareAreBothHintedAndTheLastMoveIsDrawn()
    {
        List<(Square, Square)> dropped = await OnUiThread(() =>
        {
            using GameController controller = Game("4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1");
            var rig = new Rig(controller);
            rig.Drag("e4", "d5");
            Assert.True(controller.TryHumanMove(Square.Parse("e4"), Square.Parse("d5")));
            rig.Render();
            rig.Close();
            return rig.Dropped;
        });
        Assert.Equal([(Square.Parse("e4"), Square.Parse("d5"))], dropped);
    }

    [Fact]
    public async Task TheFlippedBoardMapsTheSquaresTheOtherWayRound()
    {
        List<(Square, Square)> dropped = await OnUiThread(() =>
        {
            using var controller = new GameController();
            Assert.True(controller.Load(new SavedGame { Moves = ["e2e4"], HumanColor = Color.Black, IsBoardFlipped = true }));
            var rig = new Rig(controller);
            rig.Drag("e7", "e5");
            rig.Close();
            return rig.Dropped;
        });
        Assert.Equal([(Square.Parse("e7"), Square.Parse("e5"))], dropped);
    }

    [Fact]
    public async Task NothingIsDroppedWithoutAMovableOwnPiece()
    {
        List<(Square, Square)> dropped = await OnUiThread(() =>
        {
            using GameController controller = Game(Position.StartFen);
            var rig = new Rig(controller);
            rig.Drag("e7", "e5");                   // Not the human's piece
            rig.Drag("e4", "e5");                   // An empty square
            rig.Drag("e2", "e2");                   // Back where it started
            Assert.True(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
            rig.Drag("d2", "d4");                   // Not the human's turn
            rig.Close();
            return rig.Dropped;
        });
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task ACheckIsDrawnAndARightClickAsksToEditTheSquare()
    {
        List<Square> edits = await OnUiThread(() =>
        {
            using GameController controller = Game("4k3/8/8/8/8/8/8/r3K3 w - - 0 1");
            var rig = new Rig(controller);
            rig.Render();
            rig.Drag("c3", "c3", MouseButton.Right);
            rig.Close();
            return rig.EditRequests;
        });
        Assert.Equal([Square.Parse("c3")], edits);
    }

    [Fact]
    public async Task NoEditIsOfferedOnTheEnginesTurn()
    {
        List<Square> edits = await OnUiThread(() =>
        {
            using GameController controller = Game(Position.StartFen, Color.Black);
            var rig = new Rig(controller);
            rig.Drag("c3", "c3", MouseButton.Right);
            rig.Close();
            return rig.EditRequests;
        });
        Assert.Empty(edits);
    }

    [Fact]
    public async Task ALeftClickOnAnUnfinishedEditSaysSo()
    {
        (int incomplete, List<(Square, Square)> dropped) = await OnUiThread(() =>
        {
            using GameController controller = Game(Position.StartFen);
            controller.ClearBoard();
            var rig = new Rig(controller);
            rig.Render();
            rig.Drag("e2", "e4");
            rig.Close();
            return (rig.IncompleteEdits, rig.Dropped);
        });
        Assert.Equal(1, incomplete);
        Assert.Empty(dropped);
    }

    [Fact]
    public async Task ABoardWithNoGameOrNoRoomDrawsNothingAndTakesNoInput()
    {
        int dropped = await OnUiThread(() =>
        {
            var rig = new Rig(new GameController());
            rig.Board.Controller = null;
            rig.Render();
            rig.Drag("e2", "e4");
            using var unplaced = new GameController();
            var small = new BoardControl { Controller = unplaced, Width = 4, Height = 4 };
            var tiny = new Window { Width = Size, Height = Size, Content = small };
            tiny.Show();
            Dispatcher.UIThread.RunJobs();
            using var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size));
            bitmap.Render(small);
            tiny.Close();
            rig.Close();
            return rig.Dropped.Count;
        });
        Assert.Equal(0, dropped);
    }

    [Fact]
    public async Task TheCaptureBinDrawsBothSidesLosses()
    {
        bool drawn = await OnUiThread(() =>
        {
            using var controller = new GameController();
            Assert.True(controller.Load(new SavedGame { Moves = ["e2e4", "d7d5", "e4d5", "d8d5", "b1c3", "d5a2", "a1a2"] }));
            var bin = new CaptureBinControl { Controller = controller };
            var window = new Window { Width = Size, Height = 68, Content = bin };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var bitmap = new RenderTargetBitmap(new PixelSize(Size, 68));
            bitmap.Render(bin);
            bin.Width = 4;                              // No room for a piece
            bin.Height = 1;
            Dispatcher.UIThread.RunJobs();
            bitmap.Render(bin);
            controller.ClearBoard();                    // Editing: nothing to show
            bitmap.Render(bin);
            bin.Controller = null;
            bitmap.Render(bin);
            window.Close();
            return controller.IsEditing;
        });
        Assert.True(drawn);
    }
}
