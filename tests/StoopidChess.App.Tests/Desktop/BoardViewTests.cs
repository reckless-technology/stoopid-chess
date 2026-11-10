using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The board and the capture bin, drawn to a bitmap in every state (a drag with its hints, a check, the last move,
/// flipped, editing) and the board's pointer input turned into the squares the main view acts on.
/// </summary>
public class BoardViewTests
{
    private const int Size = 404; // 400 of squares and the frame: a square is 50

    /// <summary>
    /// A board alone in a window, with the events it raised recorded.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        public Rig(GameController controller)
        {
            Controller = controller;
            Board = new BoardView { Controller = controller };
            Window = new ViewHarness(Board, Size, Size);
            Board.MoveDropped += (from, to) => Dropped.Add((from, to));
            Board.EditRequested += (square, _) => EditRequests.Add(square);
            Board.EditingIncomplete += () => IncompleteEdits++;
        }

        public GameController Controller { get; }

        public BoardView Board { get; }

        public ViewHarness Window { get; }

        public List<(Square From, Square To)> Dropped { get; } = [];

        public List<Square> EditRequests { get; } = [];

        public int IncompleteEdits { get; private set; }

        public SKPoint Centre(string square) => Board.SquareCentre(Square.Parse(square));

        public SKColor ColorOf(string square)
        {
            SKPoint origin = Board.SquareOrigin(Square.Parse(square));
            return Window.ColorAt(origin.X + 3, origin.Y + 20); // Clear of the piece and the coordinates
        }

        public void Drag(string from, string to, PointerButton button = PointerButton.Left) =>
            Window.Drag(Centre(from), Centre(to), button);

        public void Dispose() => Window.Dispose();
    }

    private static GameController Game(string fen, Color human = Color.White)
    {
        var controller = new GameController();
        Assert.True(controller.Load(new SavedGame { StartFen = fen, HumanColor = human }));
        return controller;
    }

    [Fact]
    public void TheSquaresAreTheSeriesColorsInAFrame()
    {
        using GameController controller = Game(Position.StartFen);
        using var rig = new Rig(controller);
        Assert.Equal(50, rig.Board.SquareSize);
        Assert.Equal(Theme.LightSquare, rig.ColorOf("a8"));
        Assert.Equal(Theme.DarkSquare, rig.ColorOf("a1"));
        Assert.Equal(Theme.BoardEdge, rig.Window.ColorAt(0, 200));
        Assert.Equal(Square.Parse("a1"), rig.Board.SquareAt(new SKPoint(Edge() + 1, Edge() + 399)));
        Assert.Equal(Square.None, rig.Board.SquareAt(new SKPoint(-10, 500))); // Beyond the squares: none
        Assert.Equal(Square.None, rig.Board.SquareAt(new SKPoint(1, 1))); // The frame is no square
    }

    private static float Edge() => BoardView.Edge;

    [Fact]
    public void ADragReleasedOffTheBoardIsCancelled()
    {
        // A rook dragged off the left edge along its rank used to land on a1 and be played
        using GameController controller = Game("7k/8/8/8/8/8/8/4R2K w - - 0 1");
        using var rig = new Rig(controller);
        rig.Window.Move(rig.Centre("e1"));
        rig.Window.Press(rig.Centre("e1"));
        SKPoint offBoard = new(-30, rig.Centre("e1").Y);
        rig.Window.Move(offBoard);
        rig.Window.Release(offBoard);
        Assert.Empty(rig.Dropped);
        Assert.Equal(Square.None, rig.Board.DragFrom);
        Assert.Empty(controller.Game.Moves);
    }

    [Fact]
    public void DraggingAPieceDropsItOnTheTargetSquare()
    {
        using GameController controller = Game(Position.StartFen);
        using var rig = new Rig(controller);
        rig.Window.Move(rig.Centre("e2"));
        rig.Window.Press(rig.Centre("e2"));
        Assert.Equal(Square.Parse("e2"), rig.Board.DragFrom);
        Assert.Equal(Bitboard.Of(Square.Parse("e3")) | Bitboard.Of(Square.Parse("e4")), rig.Board.DragTargets);
        rig.Window.Move(rig.Centre("e4"));
        rig.Window.Render();
        rig.Window.Release(rig.Centre("e4"));
        Assert.Equal([(Square.Parse("e2"), Square.Parse("e4"))], rig.Dropped);
        Assert.Equal(Square.None, rig.Board.DragFrom);
    }

    [Fact]
    public void ACaptureAndAnEmptySquareAreBothHintedAndTheLastMoveIsLit()
    {
        using GameController controller = Game("4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1");
        using var rig = new Rig(controller);
        rig.Drag("e4", "d5");
        Assert.Equal([(Square.Parse("e4"), Square.Parse("d5"))], rig.Dropped);
        Assert.True(controller.TryHumanMove(Square.Parse("e4"), Square.Parse("d5")));
        Assert.Equal(Theme.LastMoveLight, rig.ColorOf("e4"));
        Assert.Equal(Theme.LastMoveLight, rig.ColorOf("d5"));
        controller.Undo();
        Assert.Equal(Theme.LightSquare, rig.ColorOf("e4"));
    }

    [Fact]
    public void TheFlippedBoardMapsTheSquaresTheOtherWayRound()
    {
        using var controller = new GameController();
        Assert.True(
            controller.Load(
                new SavedGame
                {
                    Moves = ["e2e4"],
                    HumanColor = Color.Black,
                    IsBoardFlipped = true,
                }
            )
        );
        using var rig = new Rig(controller);
        Assert.True(rig.Centre("e7").Y > rig.Centre("e5").Y);
        rig.Drag("e7", "e5");
        Assert.Equal([(Square.Parse("e7"), Square.Parse("e5"))], rig.Dropped);
    }

    [Fact]
    public void NothingIsDroppedWithoutAMovableOwnPiece()
    {
        using GameController controller = Game(Position.StartFen);
        using var rig = new Rig(controller);
        rig.Drag("e7", "e5"); // Not the human's piece
        rig.Drag("e4", "e5"); // An empty square
        rig.Drag("e2", "e2"); // Back where it started
        rig.Drag("e2", "e4", PointerButton.Middle);
        Assert.True(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        rig.Drag("d2", "d4"); // Not the human's turn
        Assert.Empty(rig.Dropped);
    }

    [Fact]
    public void ACheckIsDrawnAndARightClickAsksToEditTheSquare()
    {
        using GameController controller = Game("4k3/8/8/8/8/8/8/r3K3 w - - 0 1");
        using var rig = new Rig(controller);
        SKPoint king = rig.Centre("e1");
        Assert.NotEqual(Theme.DarkSquare, rig.Window.ColorAt(king.X - 22, king.Y)); // The glow, not the square
        rig.Window.Click(rig.Centre("c3"), PointerButton.Right);
        Assert.Equal([Square.Parse("c3")], rig.EditRequests);
    }

    [Fact]
    public void NoEditIsOfferedOnTheEnginesTurn()
    {
        using GameController controller = Game(Position.StartFen, Color.Black);
        using var rig = new Rig(controller);
        rig.Window.Click(rig.Centre("c3"), PointerButton.Right);
        Assert.Empty(rig.EditRequests);
    }

    [Fact]
    public void ALeftClickOnAnUnfinishedEditSaysSo()
    {
        using GameController controller = Game(Position.StartFen);
        controller.ClearBoard();
        using var rig = new Rig(controller);
        rig.Window.Render();
        rig.Drag("e2", "e4");
        Assert.Equal(1, rig.IncompleteEdits);
        Assert.Empty(rig.Dropped);
    }

    [Fact]
    public void ABoardWithNoGameOrNoRoomDrawsNothingAndTakesNoInput()
    {
        using var controller = new GameController();
        using var rig = new Rig(controller);
        rig.Board.Controller = null;
        rig.Window.Render();
        rig.Drag("e2", "e4");
        rig.Board.Controller = controller;
        rig.Board.SetBounds(SKRect.Create(4, 4));
        rig.Window.Render();
        Assert.Empty(rig.Dropped);
    }

    [Fact]
    public void TheCaptureBinDrawsBothSidesLosses()
    {
        using var controller = new GameController();
        Assert.True(
            controller.Load(new SavedGame { Moves = ["e2e4", "d7d5", "e4d5", "d8d5", "b1c3", "d5a2", "a1a2"] })
        );
        var bin = new CaptureBinView { Controller = controller };
        using var window = new ViewHarness(bin, 400, 68);
        Assert.Equal(25, bin.PieceSize);
        Assert.NotEqual(Theme.Window, window.ColorAt(12, 12)); // White's lost pawn
        Assert.NotEqual(Theme.Window, window.ColorAt(12, 37)); // Black's lost queen
        bin.SetBounds(SKRect.Create(4, 1)); // No room for a piece
        window.Render();
        bin.SetBounds(SKRect.Create(400, 68));
        controller.ClearBoard(); // Editing: nothing to show
        Assert.Equal(Theme.Window, window.ColorAt(12, 12));
        bin.Controller = null;
        window.Render();
    }
}
