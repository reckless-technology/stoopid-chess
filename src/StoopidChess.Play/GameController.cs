using System.ComponentModel;
using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Play;

/// <summary>
/// Everything a chess GUI needs except the drawing: the game, whose turn it is, the one engine that
/// plays the other side (the built-in engine by default, or any UCI engine started from its path),
/// position editing, the move clocks and saving. The GUI calls the methods and redraws on
/// <see cref="Changed"/>.
///
/// The controller is the teaching example of how a GUI holds an engine. It never searches itself: it
/// sends the game's moves to a <see cref="UciEngineClient"/> ("position ... moves ..."), asks it to
/// think ("go"), and when the answer ("bestmove") arrives it plays that move on its <see cref="Game"/>.
/// While the engine thinks the controller keeps answering the GUI, so a search never freezes the window.
///
/// Not thread safe: call it from one thread. Engine replies arrive through async continuations,
/// which run on the <see cref="SynchronizationContext"/> of that thread: call it from a thread with a
/// single-threaded one (the GUI's UI thread; the tests run it on one of their own), or a reply is handled
/// on another thread while the caller goes on. <see cref="WaitUntilIdleAsync"/> completes once the engine
/// has stopped thinking and its last reply has been handled.
/// </summary>
public sealed class GameController : IDisposable
{
    private static readonly TimeSpan EngineStartTimeout = TimeSpan.FromSeconds(10);

    private UciEngineClient? _engine;
    private bool _isStartingEngine;
    private CancellationTokenSource? _thinking;
    private Task? _thinkingTask;
    private int _searchGeneration; // Bumped whenever a pending engine reply must be discarded
    private volatile string _lastEngineInfo = "";
    private bool _isDisposed;

    /// <summary>
    /// The game being played: the position, the moves so far and how it stands. Replaced by a new game, a loaded
    /// one, or a finished position edit; read it, but make moves through the controller so the engine and the clocks
    /// follow.
    /// </summary>
    public Game Game { get; private set; } = new();

    /// <summary>
    /// The board being edited while play cannot yet proceed from it; null otherwise.
    /// </summary>
    public BoardSetup? Editing { get; private set; }

    /// <summary>
    /// The side the human plays; the engine plays the other.
    /// </summary>
    public Color HumanColor { get; private set; } = Color.White;

    /// <summary>
    /// The difficulty: how deep the engine may search for each move. It takes effect from the next search.
    /// </summary>
    public Level Level { get; set; } = Level.Easy;

    /// <summary>
    /// Display preference carried with saved games; the controller does not use it.
    /// </summary>
    public bool IsBoardFlipped { get; set; }

    /// <summary>
    /// The clock setting for games. Every game is played against the clock: the clock of the side to move
    /// counts down, the mover gains the increment after each move, and a side that reaches zero loses on
    /// time (<see cref="TimedOut"/>).
    /// </summary>
    public TimeControl TimeControl { get; private set; } = TimeControl.Default;

    /// <summary>
    /// Time white has left: it runs down while it is white's turn.
    /// </summary>
    public TimeSpan WhiteClock { get; private set; } = TimeControl.Default.Base;

    /// <summary>
    /// Time black has left: it runs down while it is black's turn.
    /// </summary>
    public TimeSpan BlackClock { get; private set; } = TimeControl.Default.Base;

    /// <summary>
    /// Path of the external UCI engine in use, or null for the built-in engine.
    /// </summary>
    public string? EnginePath { get; private set; }

    /// <summary>
    /// The engine's name as it reported it, or null while no engine is connected.
    /// </summary>
    public string? EngineName { get; private set; }

    /// <summary>
    /// The side that ran out of time, or null while nobody has.
    /// </summary>
    public Color? TimedOut { get; private set; }

    /// <summary>
    /// Whether play has ended, over the board (checkmate or a draw) or on the clock.
    /// </summary>
    public bool IsGameOver => Game.IsOver || TimedOut is not null;

    /// <summary>
    /// Whether an engine is connected. It is not while one is starting, nor after one has died and the controller has
    /// given up on it.
    /// </summary>
    public bool HasEngine => _engine is not null;

    /// <summary>
    /// Whether a search is under way, from the moment "go" is sent until the engine's reply has been handled.
    /// </summary>
    public bool IsThinking => _thinking is not null;

    /// <summary>
    /// The last "info" line with a principal variation from the engine, for display.
    /// </summary>
    public string LastEngineInfo => _lastEngineInfo;

    /// <summary>
    /// Adjusts the limits sent with every "go"; replaceable for tests, which make the engine think until told to stop.
    /// </summary>
    internal Func<SearchLimits, SearchLimits> LimitsFor { get; set; } = limits => limits;

    /// <summary>
    /// How an external engine is started from its path; replaceable for tests, which play against
    /// scripted engines.
    /// </summary>
    internal Func<string, UciEngineClient> StartExternalEngine { get; set; } = UciEngineClient.StartProcess;

    /// <summary>
    /// Whether the board is being edited into a position that is not yet legal.
    /// </summary>
    public bool IsEditing => Editing is not null;

    /// <summary>
    /// Whether the engine is to move: the game is on, and it is not the human's side that has the move.
    /// </summary>
    public bool IsEngineTurn => !IsEditing && !IsGameOver && Game.SideToMove != HumanColor;

    /// <summary>
    /// Whether the human may move now: the game is on, it is their side's turn and the engine is not mid-search.
    /// </summary>
    public bool IsHumanTurn => !IsEditing && !IsGameOver && Game.SideToMove == HumanColor && !IsThinking;

    /// <summary>
    /// The side to move on the board as shown (the edited position, while one is being edited).
    /// </summary>
    public Color SideToMove => Editing?.SideToMove ?? Game.SideToMove;

    /// <summary>
    /// The piece on a square of the board as shown; the default tuple is an empty square.
    /// </summary>
    public (Color Color, Piece Type) PieceAt(Square square) =>
        Editing is null ? Game.Position[square] : Editing[square];

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
    /// Connect to an engine, replacing any current one: an external UCI executable, or the built-in
    /// engine when <paramref name="path"/> is null. If an external engine fails to start the built-in
    /// one is used instead and <see cref="EngineFailed"/> is raised. Returns whether the requested
    /// engine is now in use.
    /// </summary>
    public async Task<bool> StartEngineAsync(string? path)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isStartingEngine)
        {
            return false;
        }
        _isStartingEngine = true;
        try
        {
            // Throw away any search in progress and wait for it to unwind before the old engine goes
            CancelThinking(shouldDiscardResult: true);
            await WaitUntilIdleAsync();
            DropEngine();
            EnginePath = path;
            RaiseChanged();

            (UciEngineClient? client, string? failure) = await ConnectAsync(path);
            if (client is null)
            {
                if (_isDisposed)
                {
                    return false;
                }
                EngineFailed?.Invoke(this, $"The engine could not be started: {failure}");
                if (path is not null)
                {
                    _isStartingEngine = false;
                    await StartEngineAsync(null);
                }
                return false;
            }
            _engine = client;
            EngineName = client.Name ?? Path.GetFileName(path) ?? "engine";
        }
        finally
        {
            _isStartingEngine = false;
        }
        Resume();
        return true;
    }

    /// <summary>
    /// Start the client (the built-in engine in this process, or an external executable) and complete
    /// the UCI handshake, returning null and the reason on failure.
    /// </summary>
    private async Task<(UciEngineClient? Client, string? Failure)> ConnectAsync(string? path)
    {
        UciEngineClient? client = null;
        try
        {
            client = path is null ? UciEngineClient.StartInProcess() : StartExternalEngine(path);
            client.InfoReceived += OnEngineInfo;
            using var timeout = new CancellationTokenSource(EngineStartTimeout);
            await client.InitialiseAsync(timeout.Token);
            return (client, null);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or OperationCanceledException or IOException or Win32Exception)
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
            _lastEngineInfo = line;
        }
        EngineInfo?.Invoke(this, line);
    }

    /// <summary>
    /// If it is the engine's turn and it is not already thinking, send it the game and start a search. The reply is
    /// handled by <see cref="ThinkAsync"/>.
    /// </summary>
    private void StartThinkingIfEngineTurn()
    {
        if (_thinking is not null || !IsEngineTurn || _isDisposed)
        {
            return;
        }
        UciEngineClient? client = _engine;
        if (client is null)
        {
            return;
        }
        try
        {
            // The whole game goes with every search ("position startpos moves e2e4 ..."): the engine
            // is told the game afresh each time rather than "and now this move"
            client.SetPosition(Game.StartFen, Game.UciMoves);
        }
        catch (InvalidOperationException ex)
        {
            LoseEngine(ex.Message);
            return;
        }
        _thinking = new CancellationTokenSource();
        _thinkingTask = ThinkAsync(client, _thinking, _searchGeneration);
        RaiseChanged();
    }

    /// <summary>
    /// Wait for the engine's "bestmove" and play it. This is the half of a search that runs after the engine has
    /// answered, back on the caller's thread; the stale-reply and failure cases are decided here.
    /// </summary>
    private async Task ThinkAsync(UciEngineClient client, CancellationTokenSource cancellation, int generation)
    {
        string bestMove = "0000";
        string? failure = null;
        try
        {
            // Always yield before handling the reply. An engine can answer before the await is reached
            // (the reply is already in when "go" has been written), and continuing synchronously would
            // play the move and start the next search inside StartThinkingIfEngineTurn, before it has
            // stored this task: it would then store this finished task over the running one, and
            // WaitUntilIdleAsync would report idle while that search ran on.
            bestMove = await client
                .GoAsync(CurrentLimits(), cancellation.Token)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);
        }
        catch (InvalidOperationException ex)
        {
            failure = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_thinking, cancellation))
            {
                _thinking.Dispose();
                _thinking = null;
            }
        }
        if (_isDisposed)
        {
            return;
        }
        if (failure is not null)
        {
            if (ReferenceEquals(_engine, client))
            {
                LoseEngine(failure); // Otherwise the engine was replaced deliberately
            }
            RaiseChanged();
            return;
        }
        if (generation != _searchGeneration)
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
    /// How the engine's search is limited: it plays on the clocks ("go wtime ... btime ... winc ... binc ... movestogo ...",
    /// the last when the control has periods), and at <see cref="Level.Easy"/> never searches deeper than the level allows.
    /// </summary>
    internal SearchLimits CurrentLimits()
    {
        // The difficulty is a handicap on the engine's planning, not on the clocks. Both clocks run in real time and a
        // flag still falls at zero, but the engine plans each move from a clock shrunk by the level's ratio (100 at Easy,
        // 1 at Best), so it spends about 1/ratio of the time a full-clock engine would and replies faster and weaker. The
        // engine spends from the clock it is told, so telling it less is all that is needed. The increments shrink with
        // the clocks, because the engine adds a share of the increment to its budget; movestogo stays, it is a count.
        int ratio = Level.Setting().ClockRatio;
        SearchLimits limits = new()
        {
            Depth = Level.Setting().MaxDepth,
            WhiteTime = WhiteClock / ratio,
            BlackTime = BlackClock / ratio,
            WhiteIncrement = TimeControl.Increment / ratio,
            BlackIncrement = TimeControl.Increment / ratio,
            MovesToGo = TimeControl.MovesPerPeriod is int period
                ? period - (MovesMade(Game.SideToMove) % period)
                : null,
        };
        return LimitsFor(limits);
    }

    /// <summary>
    /// The moves a side has completed in this game (it may have started with black to move).
    /// </summary>
    private int MovesMade(Color side) => (Game.Moves.Count + (Game.StartSideToMove == side ? 1 : 0)) / 2;

    /// <summary>
    /// Complete when the engine is not thinking (any pending reply has been handled).
    /// </summary>
    public async Task WaitUntilIdleAsync()
    {
        while (_thinkingTask is { IsCompleted: false } task)
        {
            await task;
        }
    }

    /// <summary>
    /// Tell the engine to stop. With <paramref name="shouldDiscardResult"/> its reply is thrown away when it comes (the game
    /// has moved on); without, the best move it has found so far is played (<see cref="MoveNow"/>).
    /// </summary>
    private void CancelThinking(bool shouldDiscardResult)
    {
        if (shouldDiscardResult)
        {
            _searchGeneration++;
        }
        _thinking?.Cancel();
    }

    /// <summary>
    /// Send the engine a command, treating one that can no longer be sent (the engine has died) as the loss of the engine.
    /// </summary>
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

    /// <summary>
    /// The engine has stopped responding: drop it and tell the GUI.
    /// </summary>
    private void LoseEngine(string message)
    {
        DropEngine();
        EngineFailed?.Invoke(this, $"The engine stopped responding: {message}");
    }

    private void DropEngine()
    {
        _engine?.Dispose();
        _engine = null;
        EngineName = null;
    }

    // ---------------------------------------------------------------------------------------------
    // Playing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Start a fresh game with the human playing <paramref name="human"/>; the engine plays the other side.
    /// </summary>
    public void NewGame(Color human)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        CancelThinking(shouldDiscardResult: true);
        Game = new Game();
        Editing = null;
        HumanColor = human;
        IsBoardFlipped = human == Color.Black;
        ResetClocks();
        TryEngine(() => _engine?.NewGame());
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

    /// <summary>
    /// Play a move on the game, for either side, give the mover the increment, and carry on.
    /// </summary>
    private void MakeMove(Move move)
    {
        Color mover = Game.SideToMove;
        Game.MakeMove(move);
        // The mover gains the increment, and the period's time again when it completes a period's moves
        TimeSpan gained = TimeControl.Increment;
        if (TimeControl.MovesPerPeriod is int period && MovesMade(mover) % period == 0)
        {
            gained += TimeControl.Base;
        }
        if (mover == Color.White)
        {
            WhiteClock += gained;
        }
        else
        {
            BlackClock += gained;
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
        CancelThinking(shouldDiscardResult: true);
        Game.TryUndoMove();
        if (Game.SideToMove != HumanColor)
        {
            Game.TryUndoMove();
        }
        if (TimedOut is Color flagged)
        {
            // Taking back moves after a loss on time reopens the game: the side that flagged gets its base
            // time again. (The clocks are not otherwise wound back by an undo.)
            TimedOut = null;
            if (flagged == Color.White)
            {
                WhiteClock = TimeControl.Base;
            }
            else
            {
                BlackClock = TimeControl.Base;
            }
        }
        Resume();
    }

    /// <summary>
    /// The human takes over the other color.
    /// </summary>
    public void SwitchSides()
    {
        HumanColor = HumanColor.Opponent();
        IsBoardFlipped = HumanColor == Color.Black;
        if (IsEngineTurn)
        {
            Resume();
        }
        else
        {
            CancelThinking(shouldDiscardResult: true);
            RaiseChanged();
        }
    }

    /// <summary>
    /// Stop the engine's search; it plays the best move it has found so far.
    /// </summary>
    public void MoveNow() => CancelThinking(shouldDiscardResult: false);

    // ---------------------------------------------------------------------------------------------
    // Position editing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Put a piece on a square (the default tuple clears it), starting an edit if none is under way. The edit
    /// becomes a new game as soon as play can proceed from it.
    /// </summary>
    public void EditSquare(Square square, (Color Color, Piece Type) piece)
    {
        BoardSetup editing = Editing ?? BoardSetup.From(Game.Position);
        editing.Place(square, piece);
        ApplyEdit(editing);
    }

    /// <summary>
    /// Say whose move it is in the position being edited.
    /// </summary>
    public void SetSideToMove(Color color)
    {
        if (SideToMove == color)
        {
            return;
        }
        BoardSetup editing = Editing ?? BoardSetup.From(Game.Position);
        editing.SideToMove = color;
        ApplyEdit(editing);
    }

    /// <summary>
    /// Take every piece off the board, to start an edit from nothing.
    /// </summary>
    public void ClearBoard()
    {
        ApplyEdit(BoardSetup.Empty(SideToMove));
    }

    /// <summary>
    /// Make the given position the game, exactly as finishing a board edit does: a position play
    /// can proceed from starts a fresh game from it (with fresh clocks); any other appears on the
    /// board for editing.
    /// </summary>
    public void SetPosition(Position position)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(position);
        if (position.IsValid)
        {
            StartFrom(position);
        }
        else
        {
            ApplyEdit(BoardSetup.From(position));
        }
    }

    /// <summary>
    /// The FEN of what the board shows: the position being edited, or the game's.
    /// </summary>
    public string CurrentFen => Editing?.ToFen() ?? Game.Position.ToFen();

    private void ApplyEdit(BoardSetup editing)
    {
        if (editing.ToPosition() is Position position)
        {
            StartFrom(position);
            return;
        }
        CancelThinking(shouldDiscardResult: true);
        Editing = editing;
        Resume();
    }

    private void StartFrom(Position position)
    {
        CancelThinking(shouldDiscardResult: true);
        Game = new Game(position);
        Editing = null;
        ResetClocks(); // A new position is a new game as far as the clocks are concerned
        Resume();
    }

    // ---------------------------------------------------------------------------------------------
    // Clocks, saving and loading
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Choose the clock setting. The clocks start again under it, and the game goes on from where it stands.
    /// </summary>
    public void SetTimeControl(TimeControl timeControl)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(timeControl);
        TimeControl = timeControl;
        CancelThinking(shouldDiscardResult: true); // A search under the old clock is restarted under the new
        ResetClocks();
        Resume();
    }

    /// <summary>
    /// Run down the clock of the side to move. Call regularly from a timer; nothing happens while editing
    /// or after the game has ended. A clock reaching zero ends the game: that side loses on time.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        if (IsEditing || IsGameOver)
        {
            return;
        }
        Color side = Game.SideToMove;
        TimeSpan remaining = (side == Color.White ? WhiteClock : BlackClock) - elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
            TimedOut = side;
            CancelThinking(shouldDiscardResult: true);
        }
        if (side == Color.White)
        {
            WhiteClock = remaining;
        }
        else
        {
            BlackClock = remaining;
        }
        if (TimedOut is not null)
        {
            RaiseChanged();
        }
    }

    private void ResetClocks()
    {
        TimedOut = null;
        WhiteClock = TimeControl.Base;
        BlackClock = TimeControl.Base;
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
    public bool TryLoad(SavedGame saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var loaded = saved.ToGame();
        if (loaded is null)
        {
            return false;
        }
        CancelThinking(shouldDiscardResult: true);
        Game = loaded;
        Editing = null;
        HumanColor = saved.HumanColor;
        IsBoardFlipped = saved.IsBoardFlipped;
        Level = saved.Level;
        ResetClocks();
        TryEngine(() => _engine?.NewGame());
        Resume();
        return true;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        CancelThinking(shouldDiscardResult: true);
        DropEngine();
        _thinking?.Dispose();
    }
}
