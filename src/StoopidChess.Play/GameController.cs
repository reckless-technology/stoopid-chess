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
    public PositionBuilder? Editing { get; private set; }

    /// <summary>
    /// The side the human plays; <see cref="Colour.None"/> when the engine plays itself.
    /// </summary>
    public Colour HumanColour { get; private set; } = Colour.White;

    public Level Level { get; set; } = Level.Easy;

    /// <summary>
    /// Display preference carried with saved games; the controller does not use it.
    /// </summary>
    public bool IsBoardFlipped { get; set; }

    public TimeSpan WhiteClock { get; private set; }

    public TimeSpan BlackClock { get; private set; }

    /// <summary>
    /// Path of the external UCI engine in use, or null for the built-in engine.
    /// </summary>
    public string? EnginePath { get; private set; }

    /// <summary>
    /// The engine's name as it reported it, or null while no engine is connected.
    /// </summary>
    public string? EngineName { get; private set; }

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

    public bool IsEngineTurn => !IsEditing && !Game.IsOver && (HumanColour == Colour.None || Game.SideToMove != HumanColour);

    public bool IsHumanTurn => !IsEditing && !Game.IsOver && Game.SideToMove == HumanColour && !IsThinking;

    public Colour SideToMove => Editing?.SideToMove ?? Game.SideToMove;

    public Piece PieceAt(int square) => Editing is null ? Game.Board[square] : Editing[square];

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

    // ---------------------------------------------------------------------------------------------
    // Engine
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Connect to an engine, replacing any current one: an external executable, or the built-in
    /// engine when <paramref name="path"/> is null. If an external engine fails to start the
    /// built-in one is used instead and <see cref="EngineFailed"/> is raised. Returns whether the
    /// requested engine is now in use.
    /// </summary>
    public async Task<bool> StartEngineAsync(string? path)
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
            DropEngine();
            EnginePath = path;
            RaiseChanged();

            UciEngineClient? client = null;
            try
            {
                client = path is null ? UciEngineClient.StartInProcess() : UciEngineClient.StartProcess(path);
                client.InfoReceived += OnEngineInfo;
                using var timeout = new CancellationTokenSource(EngineStartTimeout);
                await client.InitialiseAsync(timeout.Token);
                engine = client;
                EngineName = client.Name ?? Path.GetFileName(path) ?? "engine";
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException or Win32Exception)
            {
                client?.Dispose();
                if (disposed)
                {
                    return false;
                }
                EngineFailed?.Invoke(this, $"The engine could not be started: {ex.Message}");
                if (path is not null)
                {
                    startingEngine = false;
                    await StartEngineAsync(null);
                }
                return false;
            }
        }
        finally
        {
            startingEngine = false;
        }
        Resume();
        return true;
    }

    private void OnEngineInfo(object? sender, string line)
    {
        // Raised on the engine reader thread; a volatile string reference needs no marshalling
        if (line.Contains(" pv ", StringComparison.Ordinal))
        {
            lastEngineInfo = line;
        }
    }

    private void StartThinkingIfEngineTurn()
    {
        if (engine is null || thinking is not null || !IsEngineTurn || disposed)
        {
            return;
        }
        UciEngineClient client = engine;
        if (!TryEngine(() => client.SetPosition(Game.StartFen, Game.UciMoves)))
        {
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
            bestMove = await client.GoAsync(LimitsFor(Level), cancellation.Token);
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
            if (ReferenceEquals(engine, client))
            {
                LoseEngine(failure);        // Otherwise the engine was replaced deliberately
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

    private void DropEngine()
    {
        engine?.Dispose();
        engine = null;
        EngineName = null;
    }

    // ---------------------------------------------------------------------------------------------
    // Playing
    // ---------------------------------------------------------------------------------------------

    public void NewGame(Colour human)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CancelThinking(discardResult: true);
        Game = new Game();
        Editing = null;
        HumanColour = human;
        IsBoardFlipped = human == Colour.Black;
        ResetClocks();
        TryEngine(() => engine?.NewGame());
        Resume();
    }

    /// <summary>
    /// The legal moves for the human between two squares: none, one, or the four promotions.
    /// </summary>
    public IReadOnlyList<Move> HumanMoveCandidates(int from, int to)
    {
        return IsHumanTurn ? Game.LegalMovesBetween(from, to).ToList() : [];
    }

    /// <summary>
    /// Make the human's move if it is legal and it is the human's turn.
    /// </summary>
    public bool TryHumanMove(int from, int to, PieceType promotion = PieceType.None)
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
        Game.MakeMove(move);
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
        if (HumanColour != Colour.None && Game.SideToMove != HumanColour)
        {
            Game.UndoMove();
        }
        Resume();
    }

    /// <summary>
    /// The human takes over the other colour. Nothing happens in an engine-versus-engine game.
    /// </summary>
    public void SwitchSides()
    {
        if (HumanColour == Colour.None)
        {
            return;
        }
        HumanColour = HumanColour.Opponent();
        IsBoardFlipped = HumanColour == Colour.Black;
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

    public void EditSquare(int square, Piece piece)
    {
        PositionBuilder builder = Editing ?? PositionBuilder.From(Game.Board);
        builder[square] = piece;
        ApplyEdit(builder);
    }

    public void SetSideToMove(Colour colour)
    {
        if (SideToMove == colour)
        {
            return;
        }
        PositionBuilder builder = Editing ?? PositionBuilder.From(Game.Board);
        builder.SideToMove = colour;
        ApplyEdit(builder);
    }

    public void ClearBoard()
    {
        ApplyEdit(new PositionBuilder { SideToMove = SideToMove });
    }

    private void ApplyEdit(PositionBuilder builder)
    {
        CancelThinking(discardResult: true);
        if (builder.TryBuild() is Board board)
        {
            Game = new Game(board);
            Editing = null;
        }
        else
        {
            Editing = builder;
        }
        Resume();
    }

    // ---------------------------------------------------------------------------------------------
    // Clocks, saving and loading
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Advance the clock of the side to move. Call regularly from a timer; nothing happens while
    /// editing or after the game has ended.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        if (IsEditing || Game.IsOver)
        {
            return;
        }
        if (Game.SideToMove == Colour.White)
        {
            WhiteClock += elapsed;
        }
        else
        {
            BlackClock += elapsed;
        }
    }

    private void ResetClocks()
    {
        WhiteClock = TimeSpan.Zero;
        BlackClock = TimeSpan.Zero;
    }

    /// <summary>
    /// The game and settings for saving. Not available while editing an illegal position.
    /// </summary>
    public SavedGame? Save()
    {
        return IsEditing ? null : SavedGame.FromGame(Game, HumanColour, IsBoardFlipped, Level);
    }

    /// <summary>
    /// Replace the game with a saved one. Returns false, changing nothing, if it is not valid.
    /// </summary>
    public bool Load(SavedGame saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Game? loaded = saved.ToGame();
        if (loaded is null)
        {
            return false;
        }
        CancelThinking(discardResult: true);
        Game = loaded;
        Editing = null;
        HumanColour = saved.HumanColour;
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
        DropEngine();
        thinking?.Dispose();
    }
}
