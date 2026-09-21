using System.ComponentModel;
using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Engine.Uci;

namespace StoopidChess.Play;

/// <summary>
/// Everything a chess GUI needs except the drawing: the game, whose turn it is, the engine that
/// plays the other side (any UCI engine, the built-in one by default), position editing, clocks and
/// saving. The GUI calls the methods and redraws on <see cref="Changed"/>.
///
/// Not thread safe: call it from one thread. Engine replies arrive through async continuations,
/// which run on the <see cref="SynchronizationContext"/> of that thread when it has one (a Windows
/// Forms UI thread does); otherwise await <see cref="WaitUntilIdleAsync"/> before reading state.
/// </summary>
public sealed class GameController : IDisposable
{
    private static readonly TimeSpan EngineStartTimeout = TimeSpan.FromSeconds(10);

    private UciEngineClient? engine;
    private UciEngineClient? opponent;      // Plays black in an engine match; null outside one
    private bool startingEngine;
    private CancellationTokenSource? thinking;
    private Task? thinkingTask;
    private int searchGeneration;               // Bumped whenever a pending engine reply must be discarded
    private volatile string lastEngineInfo = "";
    private bool disposed;

    public Game Game { get; private set; } = new();

    /// <summary>
    /// The position being edited while it is not yet legal; null otherwise.
    /// </summary>
    public Position? Editing { get; private set; }

    /// <summary>
    /// The side the human plays; <see cref="Color.None"/> when the engine plays itself.
    /// </summary>
    public Color HumanColor { get; private set; } = Color.White;

    public Level Level { get; set; } = Level.Easy;

    /// <summary>
    /// The chosen clock for games, or null for untimed play. With a time control the clocks count
    /// down, a side that reaches zero loses on time (<see cref="TimedOut"/>), and the engine
    /// budgets its thinking from the clocks instead of <see cref="Level"/>. A game in progress
    /// finishes under the clock it started with (<see cref="ActiveClock"/>).
    /// </summary>
    public TimeControl? TimeControl { get; private set; }

    /// <summary>
    /// The clock the current game is being played under; null when it is untimed.
    /// </summary>
    public TimeControl? ActiveClock { get; private set; }

    /// <summary>
    /// Display preference carried with saved games; the controller does not use it.
    /// </summary>
    public bool IsBoardFlipped { get; set; }

    /// <summary>
    /// Time remaining (with a time control) or time used (without) for white.
    /// </summary>
    public TimeSpan WhiteClock { get; private set; }

    /// <summary>
    /// Time remaining (with a time control) or time used (without) for black.
    /// </summary>
    public TimeSpan BlackClock { get; private set; }

    /// <summary>
    /// The side that ran out of clock, or <see cref="Color.None"/> while nobody has.
    /// </summary>
    public Color TimedOut { get; private set; } = Color.None;

    /// <summary>
    /// Whether play has ended, over the board or on the clock.
    /// </summary>
    public bool IsGameOver => Game.IsOver || TimedOut != Color.None;

    /// <summary>
    /// Path of the external UCI engine in use, or null for the built-in engine.
    /// </summary>
    public string? EnginePath { get; private set; }

    /// <summary>
    /// The engine's name as it reported it, or null while no engine is connected.
    /// </summary>
    public string? EngineName { get; private set; }

    /// <summary>
    /// The name of the engine playing black in an engine match, or null outside a match. The main
    /// engine (<see cref="EngineName"/>) plays white.
    /// </summary>
    public string? OpponentName { get; private set; }

    /// <summary>
    /// Whether two engines are playing each other (started by <see cref="StartMatchAsync"/>).
    /// </summary>
    public bool IsMatch => opponent is not null;

    public bool HasEngine => engine is not null;

    public bool IsThinking => thinking is not null;

    /// <summary>
    /// The last "info" line with a principal variation from the engine, for display.
    /// </summary>
    public string LastEngineInfo => lastEngineInfo;

    /// <summary>
    /// How the engine's search is limited for each level; replaceable for tests.
    /// </summary>
    public Func<Level, SearchLimits> LimitsFor { get; set; } = Levels.Limits;

    public bool IsEditing => Editing is not null;

    public bool IsEngineTurn => !IsEditing && !IsGameOver && (HumanColor == Color.None || Game.SideToMove != HumanColor);

    public bool IsHumanTurn => !IsEditing && !IsGameOver && Game.SideToMove == HumanColor && !IsThinking;

    public Color SideToMove => Editing?.SideToMove ?? Game.SideToMove;

    public (Color Color, Piece Type) PieceAt(Square square) => Editing is null ? Game.Position[square] : Editing[square];

    /// <summary>
    /// Raised after any change to the game, the editor, the engine or the thinking state.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised with a message when the engine could not be started or stopped responding. The
    /// controller has already fallen back to the built-in engine, or to no engine at all.
    /// </summary>
    public event EventHandler<string>? EngineFailed;

    /// <summary>
    /// Raised with the move text when the engine answered with a move that is not legal.
    /// </summary>
    public event EventHandler<string>? EngineIllegalMove;

    /// <summary>
    /// Raised for every "info" line an engine sends, on the engine's reader thread: the subscriber
    /// must marshal to its own thread before touching anything.
    /// </summary>
    public event EventHandler<string>? EngineInfo;

    // ---------------------------------------------------------------------------------------------
    // Engine
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Connect to an engine, replacing any current one (and ending any engine match): an external
    /// executable, or the built-in engine when <paramref name="path"/> is null. The options are set
    /// after the UCI handshake. If an external engine fails to start the built-in one is used
    /// instead and <see cref="EngineFailed"/> is raised. Returns whether the requested engine is
    /// now in use.
    /// </summary>
    public async Task<bool> StartEngineAsync(string? path, IReadOnlyDictionary<string, string>? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (startingEngine)
        {
            return false;
        }
        startingEngine = true;
        try
        {
            CancelThinking(discardResult: true);
            await WaitUntilIdleAsync();
            DropOpponent();
            DropEngine();
            EnginePath = path;
            RaiseChanged();

            (UciEngineClient? client, string? failure) = await ConnectAsync(path, options);
            if (client is null)
            {
                if (disposed)
                {
                    return false;
                }
                EngineFailed?.Invoke(this, $"The engine could not be started: {failure}");
                if (path is not null)
                {
                    startingEngine = false;
                    await StartEngineAsync(null);
                }
                return false;
            }
            engine = client;
            EngineName = client.Name ?? Path.GetFileName(path) ?? "engine";
        }
        finally
        {
            startingEngine = false;
        }
        Resume();
        return true;
    }

    /// <summary>
    /// Start an engine match: white's engine against black's, each null for the built-in engine,
    /// with each engine's UCI options set after its handshake. A fresh game begins under the chosen
    /// time control. If either engine fails to start, <see cref="EngineFailed"/> is raised and the
    /// controller falls back to the built-in engine with no match running.
    /// </summary>
    public async Task<bool> StartMatchAsync(
        string? whitePath, string? blackPath,
        IReadOnlyDictionary<string, string>? whiteOptions = null, IReadOnlyDictionary<string, string>? blackOptions = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (startingEngine)
        {
            return false;
        }
        startingEngine = true;
        try
        {
            CancelThinking(discardResult: true);
            await WaitUntilIdleAsync();
            DropOpponent();
            DropEngine();
            EnginePath = whitePath;
            RaiseChanged();

            (UciEngineClient? white, string? whiteFailure) = await ConnectAsync(whitePath, whiteOptions);
            if (white is null)
            {
                startingEngine = false;
                await FailMatchAsync($"White's engine could not be started: {whiteFailure}");
                return false;
            }
            engine = white;
            EngineName = white.Name ?? Path.GetFileName(whitePath) ?? "engine";

            (UciEngineClient? black, string? blackFailure) = await ConnectAsync(blackPath, blackOptions);
            if (black is null)
            {
                startingEngine = false;
                await FailMatchAsync($"Black's engine could not be started: {blackFailure}");
                return false;
            }
            opponent = black;
            OpponentName = black.Name ?? Path.GetFileName(blackPath) ?? "engine";

            Game = new Game();
            Editing = null;
            HumanColor = Color.None;
            ResetClocks();
            TryEngine(() => engine?.NewGame());
            try
            {
                opponent?.NewGame();
            }
            catch (InvalidOperationException)
            {
                // The reply handler notices a dead opponent when it first has to think
            }
        }
        finally
        {
            startingEngine = false;
        }
        Resume();
        return true;
    }

    private async Task FailMatchAsync(string message)
    {
        if (disposed)
        {
            return;
        }
        EngineFailed?.Invoke(this, message);
        await StartEngineAsync(null);
    }

    /// <summary>
    /// Create, initialise and configure a client, returning null and the reason on failure.
    /// </summary>
    private async Task<(UciEngineClient? Client, string? Failure)> ConnectAsync(string? path, IReadOnlyDictionary<string, string>? options)
    {
        UciEngineClient? client = null;
        try
        {
            client = path is null ? UciEngineClient.StartInProcess() : UciEngineClient.StartProcess(path);
            client.InfoReceived += OnEngineInfo;
            using var timeout = new CancellationTokenSource(EngineStartTimeout);
            await client.InitialiseAsync(timeout.Token);
            if (options is not null)
            {
                foreach ((string name, string value) in options)
                {
                    client.SetOption(name, value);
                }
            }
            return (client, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException or Win32Exception)
        {
            client?.Dispose();
            return (null, ex.Message);
        }
    }

    private void OnEngineInfo(object? sender, string line)
    {
        // Raised on the engine reader thread; a volatile string reference needs no marshalling
        if (line.Contains(" pv ", StringComparison.Ordinal))
        {
            lastEngineInfo = line;
        }
        EngineInfo?.Invoke(this, line);
    }

    private void StartThinkingIfEngineTurn()
    {
        if (thinking is not null || !IsEngineTurn || disposed)
        {
            return;
        }
        // In a match the main engine plays white and the opponent black; otherwise the main engine
        // plays every engine turn
        UciEngineClient? client = opponent is not null && Game.SideToMove == Color.Black ? opponent : engine;
        if (client is null)
        {
            return;
        }
        try
        {
            client.SetPosition(Game.StartFen, Game.UciMoves);
        }
        catch (InvalidOperationException ex)
        {
            LoseClient(client, ex.Message);
            return;
        }
        thinking = new CancellationTokenSource();
        thinkingTask = ThinkAsync(client, thinking, searchGeneration);
        RaiseChanged();
    }

    private async Task ThinkAsync(UciEngineClient client, CancellationTokenSource cancellation, int generation)
    {
        string bestMove = "0000";
        string? failure = null;
        try
        {
            bestMove = await client.GoAsync(CurrentLimits(), cancellation.Token);
        }
        catch (InvalidOperationException ex)
        {
            failure = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(thinking, cancellation))
            {
                thinking.Dispose();
                thinking = null;
            }
        }
        if (disposed)
        {
            return;
        }
        if (failure is not null)
        {
            if (ReferenceEquals(engine, client) || ReferenceEquals(opponent, client))
            {
                LoseClient(client, failure);        // Otherwise the engine was replaced deliberately
            }
            RaiseChanged();
            return;
        }
        if (generation != searchGeneration)
        {
            // The game changed while the engine was thinking; its answer is for an old position
            Resume();
            return;
        }
        Move? move = Game.FindMove(bestMove);
        if (move is null)
        {
            RaiseChanged();
            if (bestMove != "0000")
            {
                EngineIllegalMove?.Invoke(this, bestMove);
            }
            return;
        }
        MakeMove(move.Value);
    }

    /// <summary>
    /// With a time control the engine plays on the clocks; otherwise the level decides.
    /// </summary>
    private SearchLimits CurrentLimits()
    {
        if (ActiveClock is { } clock)
        {
            return new SearchLimits
            {
                WhiteTime = WhiteClock,
                BlackTime = BlackClock,
                WhiteIncrement = clock.Increment,
                BlackIncrement = clock.Increment,
                MovesToGo = clock.MovesPerSession is int session
                    ? session - (MovesMade(Game.SideToMove) % session)
                    : null,
            };
        }
        return LimitsFor(Level);
    }

    /// <summary>
    /// Moves the side has completed in this game (the game may have started with black to move).
    /// </summary>
    private int MovesMade(Color side)
    {
        return (Game.Moves.Count + (Game.StartSideToMove == side ? 1 : 0)) / 2;
    }

    /// <summary>
    /// Complete when the engine is not thinking (any pending reply has been handled).
    /// </summary>
    public async Task WaitUntilIdleAsync()
    {
        while (thinkingTask is { IsCompleted: false } task)
        {
            await task;
        }
    }

    private void CancelThinking(bool discardResult)
    {
        if (discardResult)
        {
            searchGeneration++;
        }
        thinking?.Cancel();
    }

    private bool TryEngine(Action command)
    {
        try
        {
            command();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            LoseEngine(ex.Message);
            return false;
        }
    }

    private void LoseEngine(string message)
    {
        DropEngine();
        EngineFailed?.Invoke(this, $"The engine stopped responding: {message}");
    }

    private void LoseClient(UciEngineClient client, string message)
    {
        if (ReferenceEquals(opponent, client))
        {
            string name = OpponentName ?? "the opponent engine";
            DropOpponent();
            EngineFailed?.Invoke(this, $"{name} stopped responding: {message}");
        }
        else
        {
            LoseEngine(message);
        }
    }

    private void DropEngine()
    {
        engine?.Dispose();
        engine = null;
        EngineName = null;
    }

    private void DropOpponent()
    {
        opponent?.Dispose();
        opponent = null;
        OpponentName = null;
    }

    // ---------------------------------------------------------------------------------------------
    // Playing
    // ---------------------------------------------------------------------------------------------

    public void NewGame(Color human)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelThinking(discardResult: true);
        if (human != Color.None)
        {
            DropOpponent();     // A human game ends any engine match; NewGame(None) is a rematch
        }
        Game = new Game();
        Editing = null;
        HumanColor = human;
        IsBoardFlipped = human == Color.Black;
        ResetClocks();
        TryEngine(() => engine?.NewGame());
        if (opponent is not null)
        {
            try
            {
                opponent.NewGame();
            }
            catch (InvalidOperationException)
            {
                // The reply handler notices a dead opponent when it first has to think
            }
        }
        Resume();
    }

    /// <summary>
    /// The legal moves for the human between two squares: none, one, or the four promotions.
    /// </summary>
    public IReadOnlyList<Move> HumanMoveCandidates(Square from, Square to)
    {
        return IsHumanTurn ? Game.LegalMovesBetween(from, to).ToList() : [];
    }

    /// <summary>
    /// Make the human's move if it is legal and it is the human's turn.
    /// </summary>
    public bool TryHumanMove(Square from, Square to, Piece promotion = Piece.None)
    {
        if (!IsHumanTurn)
        {
            return false;
        }
        Move? move = Game.FindMove(from, to, promotion);
        if (move is null)
        {
            return false;
        }
        MakeMove(move.Value);
        return true;
    }

    private void MakeMove(Move move)
    {
        Color mover = Game.SideToMove;
        Game.MakeMove(move);
        if (ActiveClock is { } clock)
        {
            // The mover gains the increment after completing any move, and the base time again
            // after completing a session's worth of moves ("40 moves in 5 minutes", repeating)
            TimeSpan gained = clock.Increment;
            if (clock.MovesPerSession is int session && MovesMade(mover) % session == 0)
            {
                gained += clock.Base;
            }
            if (mover == Color.White)
            {
                WhiteClock += gained;
            }
            else
            {
                BlackClock += gained;
            }
        }
        Resume();
    }

    /// <summary>
    /// Tell the GUI the state has changed and set the engine thinking if it is its turn.
    /// </summary>
    private void Resume()
    {
        RaiseChanged();
        StartThinkingIfEngineTurn();
    }

    /// <summary>
    /// Take back the last move, and the engine's reply before it if the human is playing, so that
    /// it is the human's turn again.
    /// </summary>
    public void Undo()
    {
        if (IsEditing)
        {
            return;
        }
        CancelThinking(discardResult: true);
        Game.UndoMove();
        if (HumanColor != Color.None && Game.SideToMove != HumanColor)
        {
            Game.UndoMove();
        }
        Resume();
    }

    /// <summary>
    /// The human takes over the other color. Nothing happens in an engine-versus-engine game.
    /// </summary>
    public void SwitchSides()
    {
        if (HumanColor == Color.None)
        {
            return;
        }
        HumanColor = HumanColor.Opponent();
        IsBoardFlipped = HumanColor == Color.Black;
        if (IsEngineTurn)
        {
            Resume();
        }
        else
        {
            CancelThinking(discardResult: true);
            RaiseChanged();
        }
    }

    /// <summary>
    /// Stop the engine's search; it plays the best move it has found so far.
    /// </summary>
    public void MoveNow() => CancelThinking(discardResult: false);

    // ---------------------------------------------------------------------------------------------
    // Position editing
    // ---------------------------------------------------------------------------------------------

    public void EditSquare(Square square, (Color Color, Piece Type) piece)
    {
        Position editing = Editing ?? Position.Setup(Game.Position);
        editing.Place(square, piece);
        ApplyEdit(editing);
    }

    public void SetSideToMove(Color color)
    {
        if (SideToMove == color)
        {
            return;
        }
        Position editing = Editing ?? Position.Setup(Game.Position);
        editing.SetSideToMove(color);
        ApplyEdit(editing);
    }

    public void ClearBoard()
    {
        Position empty = Position.Empty();
        empty.SetSideToMove(SideToMove);
        ApplyEdit(empty);
    }

    /// <summary>
    /// Make the given position the game, exactly as finishing a board edit does: a legal position
    /// starts a fresh game from it (with fresh clocks); one that is not yet legal appears on the
    /// board for editing.
    /// </summary>
    public void SetPosition(Position position)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(position);
        ApplyEdit(position);
    }

    /// <summary>
    /// The FEN of what the board shows: the position being edited, or the game's.
    /// </summary>
    public string CurrentFen => (Editing ?? Game.Position).ToFen();

    private void ApplyEdit(Position editing)
    {
        CancelThinking(discardResult: true);
        if (editing.IsLegal)
        {
            Game = new Game(editing);
            Editing = null;
            ResetClocks();      // A new position is a new game as far as the clocks are concerned
        }
        else
        {
            Editing = editing;
        }
        Resume();
    }

    // ---------------------------------------------------------------------------------------------
    // Clocks, saving and loading
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Choose the clock the next games are played under (null for untimed play). A game with no
    /// moves yet picks it up at once — a search already running for the first move is restarted on
    /// the new clock — while a game in progress finishes under its old clock.
    /// </summary>
    public void SetTimeControl(TimeControl? timeControl)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        TimeControl = timeControl;
        if (!IsEditing && Game.Moves.Count == 0)
        {
            CancelThinking(discardResult: true);
            ResetClocks();
            Resume();
            return;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Advance the clock of the side to move. Call regularly from a timer; nothing happens while
    /// editing or after the game has ended. Under a time control the clocks run down, and a side
    /// reaching zero loses on time.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        if (IsEditing || IsGameOver)
        {
            return;
        }
        Color side = Game.SideToMove;
        if (ActiveClock is null)
        {
            if (side == Color.White)
            {
                WhiteClock += elapsed;
            }
            else
            {
                BlackClock += elapsed;
            }
            return;
        }
        TimeSpan remaining = (side == Color.White ? WhiteClock : BlackClock) - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
            TimedOut = side;
            CancelThinking(discardResult: true);
        }
        if (side == Color.White)
        {
            WhiteClock = remaining;
        }
        else
        {
            BlackClock = remaining;
        }
        if (TimedOut != Color.None)
        {
            RaiseChanged();
        }
    }

    private void ResetClocks()
    {
        ActiveClock = TimeControl;
        TimedOut = Color.None;
        WhiteClock = ActiveClock?.Base ?? TimeSpan.Zero;
        BlackClock = ActiveClock?.Base ?? TimeSpan.Zero;
    }

    /// <summary>
    /// The game and settings for saving. Not available while editing an illegal position.
    /// </summary>
    public SavedGame? Save()
    {
        return IsEditing ? null : SavedGame.FromGame(Game, HumanColor, IsBoardFlipped, Level);
    }

    /// <summary>
    /// Replace the game with a saved one. Returns false, changing nothing, if it is not valid.
    /// </summary>
    public bool Load(SavedGame saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var loaded = saved.ToGame();
        if (loaded is null)
        {
            return false;
        }
        CancelThinking(discardResult: true);
        DropOpponent();
        Game = loaded;
        Editing = null;
        HumanColor = saved.HumanColor;
        IsBoardFlipped = saved.IsBoardFlipped;
        Level = saved.Level;
        ResetClocks();
        TryEngine(() => engine?.NewGame());
        Resume();
        return true;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        CancelThinking(discardResult: true);
        DropOpponent();
        DropEngine();
        thinking?.Dispose();
    }
}
