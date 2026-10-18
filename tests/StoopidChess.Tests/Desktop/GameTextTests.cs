using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Play;
using StoopidChess.Tests.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// What the main window shows for the controller's state - its title, the move list, the status bar,
/// the analysis lines and an exported PGN's names and result - with no window.
/// </summary>
public class GameTextTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly string NL = Environment.NewLine;

    private static GameController Loaded(string fen = Position.StartFen, params string[] moves)
    {
        var controller = new GameController();
        Assert.True(controller.Load(new SavedGame { StartFen = fen, Moves = moves, HumanColor = Color.White }));
        return controller;
    }

    /// <summary>
    /// A controller whose engines are scripted ones named by their paths, which never find a move. The
    /// test thread has no synchronization context, so a test waits for the engine to be idle before it
    /// touches the controller again (GameController is single-threaded).
    /// </summary>
    private static GameController WithScriptedEngines() => new()
    {
        StartExternalEngine = path => ScriptedEngine.Start(path, ScriptedEngine.Plays("0000")),
    };

    [Fact]
    public async Task TheTitleNamesTheEngineOrTheMatch()
    {
        using GameController controller = WithScriptedEngines();
        Assert.Equal("Stoopid Chess", GameText.Title(controller));
        Assert.True(await controller.StartEngineAsync("Alpha").WaitAsync(Timeout));
        Assert.Equal("Stoopid Chess - Alpha", GameText.Title(controller));
        Assert.True(await controller.StartMatchAsync("Alpha", "Beta").WaitAsync(Timeout));
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal("Stoopid Chess - Alpha vs Beta", GameText.Title(controller));
        Assert.Equal(("Alpha", "Beta"), GameText.PlayerNames(controller));
        Assert.Equal("Nobody (engine vs engine)", GameText.HumanColor(controller));
        controller.NewGame(Color.None);                 // A rematch between the same two engines
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal("Stoopid Chess - Alpha vs Beta", GameText.Title(controller));
    }

    [Fact]
    public async Task PlayerNamesPutTheHumanOnItsSide()
    {
        using GameController controller = WithScriptedEngines();
        Assert.Equal(("Human", "Engine"), GameText.PlayerNames(controller));
        Assert.Equal("White", GameText.HumanColor(controller));
        Assert.True(await controller.StartEngineAsync("Alpha").WaitAsync(Timeout));
        controller.NewGame(Color.Black);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal(("Alpha", "Human"), GameText.PlayerNames(controller));
        controller.NewGame(Color.None);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal(("Alpha", "Alpha"), GameText.PlayerNames(controller));
    }

    [Fact]
    public void TheModeIsTheRunningClockOrTheLevelWithAPendingChangeMarked()
    {
        using var controller = new GameController();
        Assert.Equal("   Difficulty:", GameText.ModeAnnotation(controller));
        Assert.Equal("Easy", GameText.Mode(controller));
        controller.SetTimeControl(TimeControl.OfMinutes(5, 3));
        Assert.Equal("   Clock:", GameText.ModeAnnotation(controller));
        Assert.Equal("5+3", GameText.Mode(controller));
        Assert.True(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        controller.SetTimeControl(null);
        Assert.Equal("5+3 (next game: untimed)", GameText.Mode(controller));
        controller.SetTimeControl(TimeControl.Standard(40, 5));
        Assert.Equal("5+3 (next game: 40/5)", GameText.Mode(controller));
    }

    [Fact]
    public void ClocksReadInHoursMinutesAndSeconds()
    {
        Assert.Equal("01:02:03", GameText.Clock(new TimeSpan(1, 2, 3)));
        Assert.Equal("00:00:00", GameText.Clock(TimeSpan.Zero));
    }

    [Fact]
    public void TheMoveListNumbersTwoMovesToALine()
    {
        using GameController controller = Loaded(Position.StartFen, "e2e4", "e7e5", "g1f3");
        Assert.Equal($" 1. e4       e5       {NL} 2. Nf3      {NL}", GameText.MoveList(controller, showEngineOutput: false));
        using GameController empty = Loaded();
        Assert.Equal("", GameText.MoveList(empty, showEngineOutput: true));
    }

    [Fact]
    public void AGameStartingWithBlackToMoveOpensWithAnEllipsis()
    {
        using GameController controller = Loaded("4k3/8/8/8/8/8/4P3/4K3 b - - 0 7", "e8d8", "e2e4");
        Assert.Equal($" 7. ...      Kd8      {NL} 8. e4       {NL}", GameText.MoveList(controller, showEngineOutput: false));
    }

    [Theory]
    [InlineData("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3", "CHECKMATE: Black wins", "0-1")]
    [InlineData("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", "GAME OVER: DRAW\nStalemate", "1/2-1/2")]
    [InlineData("4k3/8/8/8/8/8/8/R3K3 w - - 100 80", "GAME OVER: DRAW\nDraw by the fifty-move rule", "1/2-1/2")]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", "GAME OVER: DRAW\nDraw by insufficient material", "1/2-1/2")]
    [InlineData("4k3/8/8/8/8/8/8/4RK2 b - - 0 1", "CHECK", "*")]
    public void TheMoveListEndsWithHowTheGameStands(string fen, string ending, string result)
    {
        using GameController controller = Loaded(fen);
        Assert.Equal($"Position edited by user{NL}{ending}", GameText.MoveList(controller, showEngineOutput: false));
        Assert.Equal(result, GameText.Result(controller));
    }

    [Fact]
    public void ARepetitionAndAMateAgainstBlackAreScored()
    {
        using GameController repeated = Loaded(Position.StartFen, "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8");
        Assert.EndsWith($"{NL}GAME OVER: DRAW\nDraw by threefold repetition", GameText.MoveList(repeated, showEngineOutput: false), StringComparison.Ordinal);
        Assert.Equal("1/2-1/2", GameText.Result(repeated));
        using GameController mated = Loaded("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1", "d1d8");
        Assert.EndsWith("CHECKMATE: White wins", GameText.MoveList(mated, showEngineOutput: false), StringComparison.Ordinal);
        Assert.Equal("1-0", GameText.Result(mated));
        using GameController going = Loaded();
        Assert.Equal("*", GameText.Result(going));
    }

    [Fact]
    public void LosingOnTimeIsReportedAndScored()
    {
        using var controller = new GameController();
        controller.SetTimeControl(new TimeControl(TimeSpan.FromSeconds(1), TimeSpan.Zero));
        controller.NewGame(Color.White);
        controller.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal("GAME OVER: White lost on time", GameText.MoveList(controller, showEngineOutput: false));
        Assert.Equal("0-1", GameText.Result(controller));
        controller.SetPosition(Position.FromFen("4k3/8/8/8/8/8/8/4K2R b - - 0 1"));
        controller.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal("1-0", GameText.Result(controller));
    }

    [Fact]
    public void AnEditedPositionSaysSo()
    {
        using var controller = new GameController();
        controller.ClearBoard();
        Assert.Equal("Position edited by user: place both kings to continue", GameText.MoveList(controller, showEngineOutput: true));
    }

    [Fact]
    public async Task TheEnginesLastReportIsShownWhenAsked()
    {
        using var controller = new GameController
        {
            StartExternalEngine = path => ScriptedEngine.Start(path, command => command.StartsWith("go", StringComparison.Ordinal)
                ? ["info depth 1 score cp 25 nodes 30 pv e2e4", "bestmove e2e4"]
                : []),
        };
        Assert.True(await controller.StartEngineAsync("Talker").WaitAsync(Timeout));
        controller.NewGame(Color.Black);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal($" 1. e4       {NL}", GameText.MoveList(controller, showEngineOutput: false));
        Assert.Equal($" 1. e4       {NL}{NL}info depth 1 score cp 25 nodes 30 pv e2e4", GameText.MoveList(controller, showEngineOutput: true));
    }

    [Theory]
    [InlineData("info depth 12 seldepth 18 score cp 34 nodes 1234 nps 5000 pv e2e4 e7e5",
        "d 12     +0.34           1,234 n   e2e4 e7e5")]
    [InlineData("info depth 3 score cp -150 nodes 99 pv d2d4",
        "d  3     -1.50              99 n   d2d4")]
    [InlineData("info depth 7 score mate 3 pv a1a8",
        "d  7        #3                 n   a1a8")]
    [InlineData("info depth 1 score cp 0 nodes 1 pv a2a3 a7a6 b2b3 b7b6 c2c3 c7c6 d2d3 d7d6 e2e3 e7e6 f2f3 f7f6 g2g3 g7g6",
        "d  1      0.00               1 n   a2a3 a7a6 b2b3 b7b6 c2c3 c7c6 d2d3 d7d6 e2e3 e7e6 f2f3 f7f6")]
    public void AnInfoReportWithAVariationIsOneAnalysisLine(string info, string line)
    {
        Assert.Equal(line, GameText.InfoLine(info));
    }

    [Theory]
    [InlineData("info string hello")]
    [InlineData("info depth 5 score cp 10 nodes 100")]
    [InlineData("info depth 5 pv e2e4")]
    [InlineData("info score cp 10 pv e2e4")]
    [InlineData("info depth 5 score cp x pv e2e4")]
    public void AnyOtherReportIsNoLine(string info)
    {
        Assert.Null(GameText.InfoLine(info));
    }
}
