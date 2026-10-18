using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Engine.Uci;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

/// <summary>
/// The controller against engines that misbehave - an illegal reply, an engine that dies mid-game, an
/// engine match whose engines will not start or stop answering - played by scripted engines
/// (<see cref="ScriptedEngine"/>) standing in for external executables, and the controller's smaller
/// paths that need no engine at all.
/// </summary>
public class GameControllerEngineFailureTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A controller whose external engines are the scripts registered by path; the clients it starts are
    /// collected so that a test can wait for one to die.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        private readonly Dictionary<string, (Func<string, IEnumerable<string>?> Script, bool DieAfterHandshake)> scripts = [];

        public Rig()
        {
            Controller = new GameController
            {
                LimitsFor = _ => SearchLimits.ForDepth(1),
                StartExternalEngine = path =>
                {
                    if (!scripts.TryGetValue(path, out (Func<string, IEnumerable<string>?> Script, bool DieAfterHandshake) entry))
                    {
                        throw new InvalidOperationException($"could not start '{path}'");
                    }
                    UciEngineClient client = ScriptedEngine.Start(path, entry.Script, entry.DieAfterHandshake);
                    Clients[path] = client;
                    return client;
                },
            };
            Controller.EngineFailed += (_, message) => { lock (Failures) { Failures.Add(message); } };
            Controller.EngineIllegalMove += (_, move) => { lock (IllegalMoves) { IllegalMoves.Add(move); } };
        }

        public GameController Controller { get; }

        public Dictionary<string, UciEngineClient> Clients { get; } = [];

        public List<string> Failures { get; } = [];

        public List<string> IllegalMoves { get; } = [];

        public string Register(string path, Func<string, IEnumerable<string>?> script, bool dieAfterHandshake = false)
        {
            scripts[path] = (script, dieAfterHandshake);
            return path;
        }

        public async Task WaitForExitAsync(string path)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (!Clients[path].HasExited)
            {
                Assert.True(DateTime.UtcNow < deadline, $"{path} never exited");
                await Task.Delay(5);
            }
        }

        public async Task WaitForAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "the condition never held");
                await Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
                await Task.Delay(5);
            }
        }

        public void Dispose() => Controller.Dispose();
    }

    [Fact]
    public async Task AnIllegalReplyIsReportedAndNotPlayed()
    {
        using var rig = new Rig();
        string path = rig.Register("illegal", ScriptedEngine.Plays("a1a8"));
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        Assert.Equal("illegal", rig.Controller.EngineName);
        Assert.Equal(path, rig.Controller.EnginePath);
        rig.Controller.NewGame(Color.Black);
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Equal(["a1a8"], rig.IllegalMoves);
        Assert.Empty(rig.Controller.Game.Moves);
        Assert.Empty(rig.Failures);
    }

    [Fact]
    public async Task AnEngineWithNoMoveMakesNoComplaint()
    {
        using var rig = new Rig();
        string path = rig.Register("resigns", ScriptedEngine.Plays("0000"));
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.NewGame(Color.Black);
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Empty(rig.IllegalMoves);
        Assert.Empty(rig.Controller.Game.Moves);
        Assert.False(rig.Controller.IsThinking);
    }

    [Fact]
    public async Task AnEngineThatDiesWhileThinkingIsDropped()
    {
        using var rig = new Rig();
        string path = rig.Register("dies on go", command => command.StartsWith("go", StringComparison.Ordinal) ? null : []);
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.NewGame(Color.Black);
        await rig.WaitForAsync(() => !rig.Controller.HasEngine);
        Assert.Single(rig.Failures);
        Assert.Contains("stopped responding", rig.Failures[0], StringComparison.Ordinal);
        Assert.Null(rig.Controller.EngineName);
        Assert.False(rig.Controller.IsThinking);
    }

    [Fact]
    public async Task AnEngineFoundDeadWhenItIsToMoveIsDropped()
    {
        using var rig = new Rig();
        string path = rig.Register("dies on new game", command => command == "ucinewgame" ? null : []);
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.NewGame(Color.White);
        await rig.WaitForExitAsync(path);
        Assert.True(rig.Controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        Assert.False(rig.Controller.HasEngine);
        Assert.Single(rig.Failures);
        Assert.Contains("stopped responding", rig.Failures[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewGameNoticesADeadEngine()
    {
        using var rig = new Rig();
        string path = rig.Register("dies on new game", command => command == "ucinewgame" ? null : []);
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.NewGame(Color.White);
        await rig.WaitForExitAsync(path);
        rig.Controller.NewGame(Color.White);
        Assert.False(rig.Controller.HasEngine);
        Assert.Single(rig.Failures);
    }

    [Fact]
    public async Task AMatchWhoseWhiteEngineWillNotStartFallsBackToTheBuiltInEngine()
    {
        using var rig = new Rig();
        Assert.False(await rig.Controller.StartMatchAsync("missing", null).WaitAsync(Timeout));
        Assert.Single(rig.Failures);
        Assert.StartsWith("White's engine could not be started", rig.Failures[0], StringComparison.Ordinal);
        Assert.False(rig.Controller.IsMatch);
        Assert.True(rig.Controller.HasEngine);
        Assert.Null(rig.Controller.EnginePath);
    }

    [Fact]
    public async Task AMatchWhoseBlackEngineWillNotStartFallsBackToTheBuiltInEngine()
    {
        using var rig = new Rig();
        Assert.False(await rig.Controller.StartMatchAsync(null, "missing").WaitAsync(Timeout));
        Assert.Single(rig.Failures);
        Assert.StartsWith("Black's engine could not be started", rig.Failures[0], StringComparison.Ordinal);
        Assert.False(rig.Controller.IsMatch);
        Assert.True(rig.Controller.HasEngine);
    }

    [Fact]
    public async Task AMatchOpponentThatDiesIsDroppedAndNamed()
    {
        using var rig = new Rig();
        string black = rig.Register("black dies", command => command.StartsWith("go", StringComparison.Ordinal) ? null : []);
        Assert.True(await rig.Controller.StartMatchAsync(null, black).WaitAsync(Timeout));
        Assert.Equal("black dies", rig.Controller.OpponentName);
        await rig.WaitForAsync(() => !rig.Controller.IsMatch);
        Assert.Single(rig.Failures);
        Assert.Equal("black dies stopped responding: the engine has exited", rig.Failures[0]);
        Assert.True(rig.Controller.HasEngine);         // White's engine plays on
    }

    [Fact]
    public async Task ARematchShrugsOffADeadOpponentUntilItHasToMove()
    {
        using var rig = new Rig();
        // White never finds a move, so the game stands still; black dies as soon as it has started. The
        // controller is single-threaded and the test thread has no synchronization context, so each step
        // waits until no engine reply is pending.
        string white = rig.Register("white", ScriptedEngine.Plays("0000"));
        string black = rig.Register("black", _ => [], dieAfterHandshake: true);
        Assert.True(await rig.Controller.StartMatchAsync(white, black).WaitAsync(Timeout));
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        await rig.WaitForExitAsync(black);
        rig.Controller.NewGame(Color.None);             // A rematch: the dead opponent's ucinewgame fails quietly
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.True(rig.Controller.IsMatch);
        Assert.Empty(rig.Failures);
        Assert.Empty(rig.Controller.Game.Moves);
    }

    [Fact]
    public async Task AnEngineCannotBeStartedWhileAnotherIsStarting()
    {
        // The first engine's start is held inside its launch until the other two attempts have been refused
        using var launching = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var controller = new GameController
        {
            StartExternalEngine = path =>
            {
                launching.Set();
                Assert.True(release.Wait(Timeout));
                return ScriptedEngine.Start(path, _ => []);
            },
        };
        Task<bool> first = Task.Run(() => controller.StartEngineAsync("slow"));
        Assert.True(launching.Wait(Timeout));
        Assert.False(await controller.StartEngineAsync(null));
        Assert.False(await controller.StartMatchAsync(null, null));
        release.Set();
        Assert.True(await first.WaitAsync(Timeout));
        Assert.Equal("slow", controller.EngineName);
    }

    [Fact]
    public async Task EngineInfoReachesTheSubscriberAndKeepsTheLastVariation()
    {
        using var rig = new Rig();
        string path = rig.Register("talks", command => command.StartsWith("go", StringComparison.Ordinal)
            ? ["info string thinking", "info depth 1 score cp 12 nodes 20 pv e2e4", "bestmove e2e4"]
            : []);
        var lines = new List<string>();
        rig.Controller.EngineInfo += (_, line) => { lock (lines) { lines.Add(line); } };
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.NewGame(Color.Black);
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Single(rig.Controller.Game.Moves);
        Assert.Equal("info depth 1 score cp 12 nodes 20 pv e2e4", rig.Controller.LastEngineInfo);
        lock (lines)
        {
            Assert.Equal(2, lines.Count);
        }
    }

    [Fact]
    public async Task TheEngineIsToldTheMovesToGoInASession()
    {
        var commands = new List<string>();
        using var rig = new Rig();
        string path = rig.Register("counts", command =>
        {
            lock (commands)
            {
                commands.Add(command);
            }
            return command.StartsWith("go", StringComparison.Ordinal) ? ["bestmove e7e5"] : [];
        });
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.SetTimeControl(TimeControl.Standard(40, 5));
        rig.Controller.NewGame(Color.White);
        Assert.True(rig.Controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        lock (commands)
        {
            Assert.Contains("go wtime 300000 btime 300000 winc 0 binc 0 movestogo 40", commands);
        }
    }

    [Fact]
    public void WithoutAnEngineNothingThinks()
    {
        using var controller = new GameController();
        controller.NewGame(Color.Black);
        Assert.True(controller.IsEngineTurn);
        Assert.False(controller.IsThinking);
        Assert.False(controller.HasEngine);
        Assert.Equal("", controller.LastEngineInfo);
    }

    [Fact]
    public void EditingSmallPaths()
    {
        using var controller = new GameController();
        controller.NewGame(Color.White);
        controller.SetSideToMove(Color.White);              // Already white to move: nothing happens
        Assert.False(controller.IsEditing);
        controller.ClearBoard();
        Assert.True(controller.IsEditing);
        Assert.Equal((Color.None, Piece.None), controller.PieceAt(Square.E1));
        controller.EditSquare(Square.E1, (Color.White, Piece.King));
        Assert.Equal((Color.White, Piece.King), controller.PieceAt(Square.E1));
        Assert.Equal("8/8/8/8/8/8/8/4K3 w - - 0 1", controller.CurrentFen);
        controller.Undo();                                  // Nothing to undo while editing
        Assert.True(controller.IsEditing);
        controller.SetPosition(Position.FromFen("4k3/8/8/8/8/8/8/4K3 b - - 0 1"));
        Assert.False(controller.IsEditing);
        Assert.Equal(Color.Black, controller.SideToMove);
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 b - - 0 1", controller.CurrentFen);
        Assert.Throws<ArgumentNullException>(() => controller.SetPosition(null!));
    }

    [Fact]
    public void SwitchingSidesDoesNothingInAnEngineGame()
    {
        using var controller = new GameController();
        controller.NewGame(Color.None);
        controller.SwitchSides();
        Assert.Equal(Color.None, controller.HumanColor);
    }

    [Fact]
    public async Task SwitchingSidesWhileTheEngineThinksHandsTheMoveToTheHuman()
    {
        using var controller = new GameController { LimitsFor = _ => new SearchLimits { Infinite = true } };
        Assert.True(await controller.StartEngineAsync(null).WaitAsync(Timeout));
        controller.NewGame(Color.White);
        Assert.True(controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        Assert.True(controller.IsThinking);
        controller.SwitchSides();
        Assert.Equal(Color.Black, controller.HumanColor);
        await controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Single(controller.Game.Moves);               // The engine's reply was discarded
        Assert.True(controller.IsHumanTurn);
    }

    [Fact]
    public void AnUntimedClockCountsUpForBlackToo()
    {
        using var controller = new GameController();
        controller.SetPosition(Position.FromFen("4k3/8/8/8/8/8/4P3/4K3 b - - 0 1"));
        controller.Tick(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), controller.BlackClock);
        Assert.Equal(TimeSpan.Zero, controller.WhiteClock);
    }

    [Fact]
    public void BlackRunningOutOfTimeLosesTheGame()
    {
        using var controller = new GameController();
        controller.SetTimeControl(new TimeControl(TimeSpan.FromSeconds(1), TimeSpan.Zero));
        controller.SetPosition(Position.FromFen("4k3/8/8/8/8/8/8/4K2R b - - 0 1"));
        controller.Tick(TimeSpan.FromSeconds(5));
        Assert.Equal(Color.Black, controller.TimedOut);
        Assert.Equal(TimeSpan.Zero, controller.BlackClock);
    }
}
