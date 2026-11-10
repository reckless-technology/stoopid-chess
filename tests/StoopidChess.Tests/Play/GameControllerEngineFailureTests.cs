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
    public Task AnIllegalReplyIsReportedAndNotPlayed() => ControllerThread.Run(async () =>
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
    });

    [Fact]
    public Task AnEngineThatDiesJustAsANewGameStartsIsDroppedWithoutAThrow() => ControllerThread.Run(async () =>
    {
        DeafEngine? deaf = null;
        using var controller = new GameController
        {
            LimitsFor = _ => SearchLimits.ForDepth(1),
            StartExternalEngine = path => (deaf = DeafEngine.StartAnswering(path, TimeSpan.FromMilliseconds(500))).Client,
        };
        var failures = new List<string>();
        controller.EngineFailed += (_, message) => failures.Add(message);
        Assert.True(await controller.StartEngineAsync("deaf").WaitAsync(Timeout));
        using DeafEngine engine = deaf!;
        engine.WaitUntilDeaf(Timeout);              // Dead, and the client cannot know yet
        controller.NewGame(Color.White);            // "ucinewgame" fails; dropping the engine threw IOException
        Assert.False(controller.HasEngine);
        Assert.Null(controller.EngineName);
        Assert.Equal(["The engine stopped responding: the engine has exited"], failures);
        Assert.True(engine.IsCommandPipeReleased);
        Assert.True(engine.IsReplyPipeReleased);
    });

    [Fact]
    public Task AnEngineWithNoMoveMakesNoComplaint() => ControllerThread.Run(async () =>
    {
        using var rig = new Rig();
        string path = rig.Register("resigns", ScriptedEngine.Plays("0000"));
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.NewGame(Color.Black);
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        Assert.Empty(rig.IllegalMoves);
        Assert.Empty(rig.Controller.Game.Moves);
        Assert.False(rig.Controller.IsThinking);
    });

    [Fact]
    public Task AnEngineThatDiesWhileThinkingIsDropped() => ControllerThread.Run(async () =>
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
    });

    [Fact]
    public Task AnEngineFoundDeadWhenItIsToMoveIsDropped() => ControllerThread.Run(async () =>
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
    });

    [Fact]
    public Task ANewGameNoticesADeadEngine() => ControllerThread.Run(async () =>
    {
        using var rig = new Rig();
        string path = rig.Register("dies on new game", command => command == "ucinewgame" ? null : []);
        Assert.True(await rig.Controller.StartEngineAsync(path).WaitAsync(Timeout));
        rig.Controller.NewGame(Color.White);
        await rig.WaitForExitAsync(path);
        rig.Controller.NewGame(Color.White);
        Assert.False(rig.Controller.HasEngine);
        Assert.Single(rig.Failures);
    });

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
        release.Set();
        Assert.True(await first.WaitAsync(Timeout));
        Assert.Equal("slow", controller.EngineName);
    }

    [Fact]
    public Task EngineInfoReachesTheSubscriberAndKeepsTheLastVariation() => ControllerThread.Run(async () =>
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
    });

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
        Assert.Equal(Piece.None, controller.PieceAt(Square.E1).Type);
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
    public Task SwitchingSidesWhileTheEngineThinksHandsTheMoveToTheHuman() => ControllerThread.Run(async () =>
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
    });

    [Fact]
    public void TheClockRunsDownForBlackToo()
    {
        using var controller = new GameController();
        controller.SetPosition(Position.FromFen("4k3/8/8/8/8/8/4P3/4K3 b - - 0 1"));
        controller.Tick(TimeSpan.FromSeconds(3));
        Assert.Equal(controller.TimeControl.Base - TimeSpan.FromSeconds(3), controller.BlackClock);
        Assert.Equal(controller.TimeControl.Base, controller.WhiteClock);
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

    [Fact]
    public Task TheEngineIsGivenTheClocksAndTheLevelsDepth() => ControllerThread.Run(async () =>
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
        rig.Controller.SetTimeControl(TimeControl.OfMinutes(5, 3));
        rig.Controller.NewGame(Color.White);
        Assert.True(rig.Controller.TryHumanMove(Square.Parse("e2"), Square.Parse("e4")));
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        lock (commands)
        {
            Assert.Contains(commands, command => command.StartsWith("go depth ", StringComparison.Ordinal)
                && command.EndsWith("wtime 303000 btime 300000 winc 3000 binc 3000", StringComparison.Ordinal));
        }
    });
}
