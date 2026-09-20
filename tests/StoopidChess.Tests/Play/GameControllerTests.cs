using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

/// <summary>
/// The game state machine behind the GUI, driven against the in-process engine exactly as the
/// window drives it. Tests await <see cref="GameController.WaitUntilIdleAsync"/> before reading
/// state because the test thread has no synchronization context.
/// </summary>
public class GameControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static async Task<GameController> StartAsync()
    {
        var controller = new GameController { LimitsFor = _ => SearchLimits.ForDepth(2) };
        Assert.True(await controller.StartEngineAsync(null).WaitAsync(Timeout));
        Assert.True(controller.HasEngine);
        Assert.StartsWith("Stoopid Chess", controller.EngineName, StringComparison.Ordinal);
        return controller;
    }

    [Fact]
    public async Task EnginePlaysWhiteWhenTheHumanIsBlack()
    {
        using GameController controller = await StartAsync();
        int changes = 0;
        controller.Changed += (_, _) => Interlocked.Increment(ref changes);
        controller.NewGame(Color.Black);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Single(controller.Game.Moves);
        Assert.Equal(Color.Black, controller.Game.SideToMove);
        Assert.True(controller.IsHumanTurn);
        Assert.False(controller.IsThinking);
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task HumanMoveIsAnsweredByTheEngine()
    {
        using GameController controller = await StartAsync();
        controller.NewGame(Color.White);
        Assert.False(controller.IsThinking);
        Assert.False(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e5")));
        Assert.True(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        Assert.False(controller.IsHumanTurn);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal(2, controller.Game.Moves.Count);
        Assert.True(controller.IsHumanTurn);
        Assert.False(controller.TryHumanMove(Square.Parse("e4"), Square.Parse("e5")) && controller.Game.Moves.Count != 3);
    }

    [Fact]
    public async Task PromotionNeedsAChoice()
    {
        using GameController controller = await StartAsync();
        controller.Load(new SavedGame { StartFen = "4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", HumanColor = Color.White });
        var from = Square.Parse("b7");
        var to = Square.Parse("b8");
        Assert.Equal(4, controller.HumanMoveCandidates(from, to).Count);
        Assert.False(controller.TryHumanMove(from, to));
        Assert.True(controller.TryHumanMove(from, to, Piece.Knight));
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal((Color.White, Piece.Knight), controller.Game.Position[to]);
    }

    [Fact]
    public async Task UndoDuringTheEngineSearchDiscardsItsReply()
    {
        using GameController controller = await StartAsync();
        controller.LimitsFor = _ => new SearchLimits { Infinite = true };
        controller.NewGame(Color.White);
        controller.TryHumanMove(Square.Parse("d2"), Square.Parse("d4"));
        Assert.True(controller.IsThinking);
        controller.Undo();
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Empty(controller.Game.Moves);
        Assert.True(controller.IsHumanTurn);
        Assert.False(controller.IsThinking);
    }

    [Fact]
    public async Task UndoTakesBackBothMovesSoItIsTheHumansTurn()
    {
        using GameController controller = await StartAsync();
        controller.NewGame(Color.White);
        controller.TryHumanMove(Square.Parse("d2"), Square.Parse("d4"));
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal(2, controller.Game.Moves.Count);
        controller.Undo();
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Empty(controller.Game.Moves);
        Assert.True(controller.IsHumanTurn);
    }

    [Fact]
    public async Task MoveNowPlaysTheEnginesCurrentBestMove()
    {
        using GameController controller = await StartAsync();
        controller.LimitsFor = _ => new SearchLimits { Infinite = true };
        controller.NewGame(Color.Black);
        await Task.Delay(200);
        Assert.True(controller.IsThinking);
        controller.MoveNow();
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Single(controller.Game.Moves);
    }

    [Fact]
    public async Task SwitchSidesHandsTheMoveToTheEngine()
    {
        using GameController controller = await StartAsync();
        controller.NewGame(Color.White);
        Assert.True(controller.IsHumanTurn);
        controller.SwitchSides();
        Assert.Equal(Color.Black, controller.HumanColor);
        Assert.True(controller.IsBoardFlipped);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Single(controller.Game.Moves);
        Assert.True(controller.IsHumanTurn);
    }

    [Fact]
    public async Task EngineVersusEnginePlaysOn()
    {
        using GameController controller = await StartAsync();
        controller.NewGame(Color.None);
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (controller.Game.Moves.Count < 4 && DateTime.UtcNow < deadline)
        {
            await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
            await Task.Delay(10);
        }
        Assert.True(controller.Game.Moves.Count >= 4);
        Assert.False(controller.IsHumanTurn);
        controller.Dispose();
        Assert.False(controller.HasEngine);
    }

    [Fact]
    public async Task EditingIntoALegalPositionStartsTheEngineIfItIsItsTurn()
    {
        using GameController controller = await StartAsync();
        controller.NewGame(Color.White);
        controller.ClearBoard();
        Assert.True(controller.IsEditing);
        Assert.Null(controller.Save());
        Assert.Empty(controller.HumanMoveCandidates(Square.E1, Square.Parse("e2")));
        controller.EditSquare(Square.E1, (Color.White, Piece.King));
        controller.EditSquare(Square.E8, (Color.Black, Piece.King));
        controller.EditSquare(Square.Parse("a7"), (Color.White, Piece.Rook));
        Assert.False(controller.IsEditing);
        controller.SetSideToMove(Color.Black);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Single(controller.Game.Moves);     // Black (the engine) has moved
        Assert.Equal(Color.White, controller.Game.SideToMove);
    }

    [Fact]
    public async Task SaveAndLoadRoundTrip()
    {
        using GameController controller = await StartAsync();
        controller.Level = Level.Hard;
        controller.NewGame(Color.Black);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        SavedGame saved = controller.Save()!;
        using var stream = new MemoryStream();
        saved.Save(stream);
        stream.Position = 0;
        SavedGame loaded = SavedGame.Load(stream)!;
        controller.NewGame(Color.White);
        Assert.True(controller.Load(loaded));
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal(Color.Black, controller.HumanColor);
        Assert.Equal(Level.Hard, controller.Level);
        Assert.Single(controller.Game.Moves);
        Assert.False(controller.Load(new SavedGame { StartFen = "not a fen" }));
    }

    [Fact]
    public async Task ClocksRunForTheSideToMoveAndStopAtTheEnd()
    {
        using GameController controller = await StartAsync();
        controller.Load(new SavedGame { StartFen = "6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1", HumanColor = Color.White });
        controller.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), controller.WhiteClock);
        Assert.Equal(TimeSpan.Zero, controller.BlackClock);
        Assert.True(controller.TryHumanMove(Square.Parse("d1"), Square.Parse("d8")));
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal(GameStatus.Checkmate, controller.Game.Status);
        controller.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, controller.BlackClock);
        controller.ClearBoard();
        controller.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), controller.WhiteClock);
    }

    [Fact]
    public void TimeControlCountsDownAndCreditsTheIncrementToTheMover()
    {
        using var controller = new GameController();
        controller.SetTimeControl(TimeControl.OfMinutes(5, 3));
        controller.NewGame(Color.White);
        Assert.Equal(TimeSpan.FromMinutes(5), controller.WhiteClock);
        Assert.Equal(TimeSpan.FromMinutes(5), controller.BlackClock);
        controller.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(2), controller.WhiteClock);
        Assert.Equal(TimeSpan.FromMinutes(5), controller.BlackClock);
        Assert.True(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1), controller.WhiteClock);
    }

    [Fact]
    public void RunningOutOfTimeLosesTheGame()
    {
        using var controller = new GameController();
        controller.SetTimeControl(new TimeControl(TimeSpan.FromSeconds(1), TimeSpan.Zero));
        controller.NewGame(Color.White);
        controller.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(Color.White, controller.TimedOut);
        Assert.Equal(TimeSpan.Zero, controller.WhiteClock);
        Assert.True(controller.IsGameOver);
        Assert.False(controller.IsHumanTurn);
        Assert.False(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        controller.NewGame(Color.White);
        Assert.Equal(Color.None, controller.TimedOut);
        Assert.False(controller.IsGameOver);
    }

    [Fact]
    public void ChangingTheTimeControlLeavesARunningGameOnItsOldClock()
    {
        using var controller = new GameController();
        controller.SetTimeControl(TimeControl.OfMinutes(5, 3));
        controller.NewGame(Color.White);
        Assert.True(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        controller.SetTimeControl(null);
        Assert.Equal(TimeControl.OfMinutes(5, 3), controller.ActiveClock);
        controller.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1), controller.BlackClock);
        controller.NewGame(Color.White);
        Assert.Null(controller.ActiveClock);
        Assert.Equal(TimeSpan.Zero, controller.WhiteClock);
    }

    [Fact]
    public async Task TheEnginePlaysOnTheClockWhenATimeControlIsSet()
    {
        using GameController controller = await StartAsync();
        controller.SetTimeControl(new TimeControl(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100)));
        controller.NewGame(Color.Black);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Single(controller.Game.Moves);
        // No timer ticked, so white's clock is its base plus the increment for the move it made
        Assert.Equal(TimeSpan.FromSeconds(2.1), controller.WhiteClock);
    }

    [Fact]
    public async Task AMissingExternalEngineFallsBackToTheBuiltInOne()
    {
        using var controller = new GameController { LimitsFor = _ => SearchLimits.ForDepth(1) };
        var failures = new List<string>();
        controller.EngineFailed += (_, message) => failures.Add(message);
        Assert.False(await controller.StartEngineAsync(Path.Combine(Path.GetTempPath(), "no-such-engine.exe")).WaitAsync(Timeout));
        Assert.Single(failures);
        Assert.True(controller.HasEngine);
        Assert.Null(controller.EnginePath);
        Assert.StartsWith("Stoopid Chess", controller.EngineName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeStopsEverything()
    {
        GameController controller = await StartAsync();
        controller.LimitsFor = _ => new SearchLimits { Infinite = true };
        controller.NewGame(Color.Black);
        controller.Dispose();
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.False(controller.HasEngine);
        Assert.Throws<ObjectDisposedException>(() => controller.NewGame(Color.White));
    }
}
