using System.ComponentModel;
using System.Globalization;
using System.Text;
using StoopidChess.Core;
using StoopidChess.Engine.Uci;
using Timer = System.Windows.Forms.Timer;

namespace StoopidChess.Gui;

/// <summary>
/// The main window. The rules of the game come from <see cref="Game"/>; the computer's moves come
/// from a UCI engine (the built-in one by default) through <see cref="UciEngineClient"/>, so the form
/// knows nothing about how the engine works.
/// </summary>
public partial class MainForm : Form
{
    private static readonly Pen LastMovePen = new(Color.DarkGoldenrod, 2.5f);
    private static readonly TimeSpan EngineStartTimeout = TimeSpan.FromSeconds(10);

    private readonly PieceImages images = new();
    private readonly Timer clockTimer = new() { Interval = 100 };

    private Game game = new();
    private Board? editedBoard;                 // Set while the user is editing a position that is not yet legal
    private Colour humanColour = Colour.White;  // Colour.None when the engine plays itself
    private bool isBoardFlipped;
    private Level level = Level.Easy;
    private TimeSpan whiteClock;
    private TimeSpan blackClock;
    private (int From, int To)? lastMove;

    private int dragFrom = Squares.None;        // Square of the piece being dragged, or None
    private Point dragPosition;
    private Point contextMenuPosition;

    private UciEngineClient? engine;
    private string? enginePath;                 // Null for the built-in engine
    private CancellationTokenSource? thinking;  // Non-null while the engine is searching
    private int searchGeneration;               // Bumped whenever a pending search result must be discarded
    private string lastEngineInfo = "";
    private bool showEngineOutput;

    public MainForm()
    {
        InitializeComponent();
        moveListTextBox.SelectionTabs = [40, 80, 120, 160, 200];
        clockTimer.Tick += ClockTick;
        BuildEngineMenu();
        BuildSideToMoveMenu();
        SetLevel(Level.Easy);
        stopThinkingButton.Enabled = false;
        UpdateStatus();
        clockTimer.Start();
    }

    private Board DisplayBoard => editedBoard ?? game.Board;

    private bool IsEditing => editedBoard is not null;

    private bool IsEngineTurn => !IsEditing && !game.IsOver && (humanColour == Colour.None || game.SideToMove != humanColour);

    private bool IsHumanTurn => !IsEditing && !game.IsOver && game.SideToMove == humanColour;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await StartEngineAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        clockTimer.Dispose();
        engine?.Dispose();
        thinking?.Dispose();
        base.OnFormClosed(e);
    }

    // ---------------------------------------------------------------------------------------------
    // Engine
    // ---------------------------------------------------------------------------------------------

    private async Task StartEngineAsync()
    {
        CancelThinking(discardResult: true);
        engine?.Dispose();
        engine = null;
        Text = "Stoopid Chess";
        try
        {
            UciEngineClient client = enginePath is null ? UciEngineClient.StartInProcess() : UciEngineClient.StartProcess(enginePath);
            client.InfoReceived += OnEngineInfo;
            using var timeout = new CancellationTokenSource(EngineStartTimeout);
            await client.InitialiseAsync(timeout.Token);
            engine = client;
            Text = $"Stoopid Chess - {client.Name ?? Path.GetFileName(enginePath)}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException or Win32Exception)
        {
            MessageBox.Show(this, $"The engine could not be started:\n{ex.Message}\n\nThe built-in engine will be used instead.", "Engine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (enginePath is not null)
            {
                enginePath = null;
                UpdateEngineMenu();
                await StartEngineAsync();
                return;
            }
        }
        if (IsEngineTurn)
        {
            StartThinking();
        }
    }

    private void OnEngineInfo(object? sender, string line)
    {
        // Raised on the engine reader thread
        if (line.Contains(" pv ", StringComparison.Ordinal) && !IsDisposed)
        {
            BeginInvoke(() => lastEngineInfo = line);
        }
    }

    private void StartThinking()
    {
        if (engine is null || thinking is not null || !IsEngineTurn)
        {
            return;
        }
        thinking = new CancellationTokenSource();
        SetThinkingUi(true);
        engine.SetPosition(game.StartFen, game.UciMoves);
        _ = ThinkAsync(engine, thinking, searchGeneration);
    }

    private async Task ThinkAsync(UciEngineClient client, CancellationTokenSource cancellation, int generation)
    {
        string bestMove = "0000";
        try
        {
            bestMove = await client.GoAsync(level.Limits(), cancellation.Token);
        }
        catch (InvalidOperationException)
        {
            // The engine has gone away; the move stays "0000" and the user can restart it from the menu
        }
        finally
        {
            if (ReferenceEquals(thinking, cancellation))
            {
                thinking.Dispose();
                thinking = null;
                SetThinkingUi(false);
            }
        }
        if (generation != searchGeneration)
        {
            // The game changed while the engine was thinking; its answer is for an old position
            if (IsEngineTurn)
            {
                StartThinking();
            }
            return;
        }
        Move? move = game.FindMove(bestMove);
        if (move is null)
        {
            if (bestMove != "0000")
            {
                MessageBox.Show(this, $"The engine played an illegal move: {bestMove}", "Engine", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }
        if (showEngineOutput && lastEngineInfo.Length != 0)
        {
            moveListTextBox.AppendText(Environment.NewLine + lastEngineInfo);
        }
        MakeMove(move.Value);
    }

    /// <summary>
    /// Stop the engine. With <paramref name="discardResult"/> the move it returns is ignored;
    /// otherwise it is played (the "stop thinking" button).
    /// </summary>
    private void CancelThinking(bool discardResult)
    {
        if (discardResult)
        {
            searchGeneration++;
        }
        thinking?.Cancel();
    }

    private void SetThinkingUi(bool isThinking)
    {
        stopThinkingButton.Enabled = isThinking;
        mainMenuStrip.Enabled = !isThinking;
        UseWaitCursor = isThinking;
    }

    // ---------------------------------------------------------------------------------------------
    // Game flow
    // ---------------------------------------------------------------------------------------------

    private void NewGame(Colour human)
    {
        CancelThinking(discardResult: true);
        game = new Game();
        editedBoard = null;
        humanColour = human;
        isBoardFlipped = human == Colour.Black;
        lastMove = null;
        dragFrom = Squares.None;
        ResetClocks();
        engine?.NewGame();
        RefreshAll();
        if (IsEngineTurn)
        {
            StartThinking();
        }
    }

    private void MakeMove(Move move)
    {
        game.MakeMove(move);
        lastMove = (move.From, move.To);
        dragFrom = Squares.None;
        RefreshAll();
        Activate();
        if (game.IsOver)
        {
            clockTimer.Stop();
        }
        else if (IsEngineTurn)
        {
            StartThinking();
        }
    }

    private void RefreshAll()
    {
        UpdateMoveList();
        UpdateStatus();
        chessBoardPictureBox.Invalidate();
        captureBinPictureBox.Invalidate();
    }

    private void UpdateMoveList()
    {
        if (IsEditing)
        {
            moveListTextBox.Text = "Position edited by user: place both kings to continue";
            return;
        }
        var text = new StringBuilder();
        Colour mover = Board.FromFen(game.StartFen).SideToMove;
        int moveNumber = Board.FromFen(game.StartFen).FullMoveNumber;
        bool lineOpen = false;
        if (mover == Colour.Black && game.Moves.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{moveNumber,2}.\t...\t");
            lineOpen = true;
        }
        foreach (Move move in game.Moves)
        {
            if (mover == Colour.White)
            {
                text.Append(CultureInfo.InvariantCulture, $"{moveNumber,2}.\t");
                lineOpen = true;
            }
            text.Append(CultureInfo.InvariantCulture, $"{MoveNotation.ToLongAlgebraic(move),-9}");
            if (mover == Colour.Black)
            {
                text.AppendLine();
                moveNumber++;
                lineOpen = false;
            }
            else
            {
                text.Append('\t');
            }
            mover = mover.Opponent();
        }
        if (lineOpen)
        {
            text.AppendLine();
        }
        switch (game.Status)
        {
            case GameStatus.Checkmate:
                text.Append(CultureInfo.InvariantCulture, $"CHECKMATE: {game.SideToMove.Opponent()} wins");
                break;
            case GameStatus.Stalemate:
                text.Append("GAME OVER: DRAW\nStalemate");
                break;
            case GameStatus.DrawByRepetition:
                text.Append("GAME OVER: DRAW\nDraw by threefold repetition");
                break;
            case GameStatus.DrawByFiftyMoveRule:
                text.Append("GAME OVER: DRAW\nDraw by the fifty-move rule");
                break;
            case GameStatus.DrawByInsufficientMaterial:
                text.Append("GAME OVER: DRAW\nDraw by insufficient material");
                break;
            default:
                if (game.IsInCheck)
                {
                    text.Append("CHECK");
                }
                break;
        }
        moveListTextBox.Text = text.ToString();
        moveListTextBox.SelectionStart = moveListTextBox.Text.Length;
        moveListTextBox.ScrollToCaret();
    }

    private void UpdateStatus()
    {
        colourToMoveLabel.Text = DisplayBoard.SideToMove.ToString();
        humanColourLabel.Text = humanColour == Colour.None ? "Nobody (CPU vs CPU)" : humanColour.ToString();
        difficultyLabel.Text = level.ToString();
        UpdateClockLabels();
    }

    private void ResetClocks()
    {
        whiteClock = TimeSpan.Zero;
        blackClock = TimeSpan.Zero;
        UpdateClockLabels();
        clockTimer.Start();
    }

    private void UpdateClockLabels()
    {
        whiteClockLabel.Text = whiteClock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        blackClockLabel.Text = blackClock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private void ClockTick(object? sender, EventArgs e)
    {
        if (IsEditing || game.IsOver)
        {
            return;
        }
        TimeSpan tick = TimeSpan.FromMilliseconds(clockTimer.Interval);
        if (game.SideToMove == Colour.White)
        {
            whiteClock += tick;
        }
        else
        {
            blackClock += tick;
        }
        UpdateClockLabels();
    }

    private void SetLevel(Level newLevel)
    {
        level = newLevel;
        difficultyLabel.Text = newLevel.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Board display and mouse input
    // ---------------------------------------------------------------------------------------------

    private int SquareSize => Math.Min(chessBoardPictureBox.Width, chessBoardPictureBox.Height) / 8;

    private Point SquareOrigin(int square)
    {
        int file = Squares.FileOf(square);
        int rank = Squares.RankOf(square);
        int x = isBoardFlipped ? 7 - file : file;
        int y = isBoardFlipped ? rank : 7 - rank;
        return new Point(x * SquareSize, y * SquareSize);
    }

    private int SquareAt(Point location)
    {
        int size = SquareSize;
        int x = Math.Clamp(location.X / size, 0, 7);
        int y = Math.Clamp(location.Y / size, 0, 7);
        int file = isBoardFlipped ? 7 - x : x;
        int rank = isBoardFlipped ? y : 7 - y;
        return Squares.Index(file, rank);
    }

    private void ChessBoardPictureBoxPaintHandler(object? sender, PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        int size = SquareSize;
        Board board = DisplayBoard;
        foreach (int square in Squares.All)
        {
            Point origin = SquareOrigin(square);
            graphics.FillRectangle(Board.IsLightSquare(square) ? Brushes.Ivory : Brushes.Gray, origin.X, origin.Y, size, size);
        }
        foreach (int square in Squares.All)
        {
            Piece piece = board[square];
            if (!piece.IsNone && square != dragFrom)
            {
                Point origin = SquareOrigin(square);
                graphics.DrawImage(images[piece], origin.X, origin.Y, size, size);
            }
        }
        if (lastMove is (int from, int to))
        {
            Point fromOrigin = SquareOrigin(from);
            Point toOrigin = SquareOrigin(to);
            graphics.DrawRectangle(LastMovePen, fromOrigin.X, fromOrigin.Y, size, size);
            graphics.DrawRectangle(LastMovePen, toOrigin.X, toOrigin.Y, size, size);
        }
        if (dragFrom != Squares.None)
        {
            graphics.DrawImage(images[board[dragFrom]], dragPosition.X - (size / 2), dragPosition.Y - (size / 2), size, size);
        }
    }

    private void CaptureBinPictureBoxPaintHandler(object? sender, PaintEventArgs e)
    {
        if (IsEditing)
        {
            return;
        }
        int size = Math.Min(captureBinPictureBox.Width / 16, captureBinPictureBox.Height / 2);
        foreach (Colour colour in new[] { Colour.White, Colour.Black })
        {
            int y = colour == Colour.White ? 0 : size;
            int x = 0;
            foreach (Piece piece in game.CapturedPieces(colour))
            {
                e.Graphics.DrawImage(images[piece], x, y, size, size);
                x += size;
            }
        }
    }

    private void ChessBoardPictureBoxMouseDownHandler(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if (IsEditing)
        {
            using var dialog = new IllegalPositionForm();
            dialog.ShowDialog(this);
            return;
        }
        if (!IsHumanTurn || thinking is not null)
        {
            return;
        }
        int square = SquareAt(e.Location);
        if (game.Board[square].Colour == humanColour)
        {
            dragFrom = square;
            dragPosition = e.Location;
            chessBoardPictureBox.Invalidate();
        }
    }

    private void ChessBoardPictureBoxMouseMoveHandler(object? sender, MouseEventArgs e)
    {
        if (dragFrom != Squares.None)
        {
            dragPosition = e.Location;
            chessBoardPictureBox.Invalidate();
        }
    }

    private void ChessBoardPictureBoxMouseUpHandler(object? sender, MouseEventArgs e)
    {
        if (dragFrom == Squares.None || e.Button != MouseButtons.Left)
        {
            return;
        }
        int from = dragFrom;
        int to = SquareAt(e.Location);
        dragFrom = Squares.None;
        chessBoardPictureBox.Invalidate();
        if (to == from || !IsHumanTurn)
        {
            return;
        }
        List<Move> candidates = game.LegalMovesBetween(from, to).ToList();
        if (candidates.Count == 0)
        {
            using var dialog = new MoveNotPermittedForm();
            dialog.ShowDialog(this);
            return;
        }
        Move move = candidates[0];
        if (candidates.Count > 1)
        {
            using var promotion = new PawnPromotionForm(humanColour, images);
            promotion.ShowDialog(this);
            move = candidates.Single(m => m.Promotion == promotion.Choice);
        }
        MakeMove(move);
    }

    private void ChessBoardMouseClickHandler(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || thinking is not null || (IsEngineTurn && humanColour != Colour.None))
        {
            return;
        }
        contextMenuPosition = e.Location;
        contextMenuStrip.Show(chessBoardPictureBox, e.Location);
    }

    private void ChessBoardSizeChangedHandler(object? sender, EventArgs e)
    {
        chessBoardPictureBox.Invalidate();
    }

    // ---------------------------------------------------------------------------------------------
    // Position editing
    // ---------------------------------------------------------------------------------------------

    private void EditSquare(Piece piece)
    {
        Board board = DisplayBoard.Clone();
        board.SetPiece(SquareAt(contextMenuPosition), piece);
        ApplyEditedBoard(board);
    }

    private void ApplyEditedBoard(Board board)
    {
        CancelThinking(discardResult: true);
        if (MoveGenerator.IsPositionLegal(board))
        {
            game = new Game(board);
            editedBoard = null;
        }
        else
        {
            editedBoard = board;
        }
        lastMove = null;
        dragFrom = Squares.None;
        RefreshAll();
        if (!IsEditing)
        {
            moveListTextBox.Text = "Position edited by user";
        }
    }

    private void BuildSideToMoveMenu()
    {
        var whiteToMove = new ToolStripMenuItem("&White to move");
        var blackToMove = new ToolStripMenuItem("&Black to move");
        whiteToMove.Click += (_, _) => SetSideToMove(Colour.White);
        blackToMove.Click += (_, _) => SetSideToMove(Colour.Black);
        contextMenuStrip.Items.Add(new ToolStripSeparator());
        contextMenuStrip.Items.Add(whiteToMove);
        contextMenuStrip.Items.Add(blackToMove);
    }

    private void SetSideToMove(Colour colour)
    {
        if (DisplayBoard.SideToMove == colour)
        {
            return;
        }
        Board board = DisplayBoard.Clone();
        board.SetSideToMove(colour);
        ApplyEditedBoard(board);
        if (IsEngineTurn)
        {
            StartThinking();
        }
    }

    private void HandlerSetSquareWhitePawn(object? sender, EventArgs e) => EditSquare(Piece.WhitePawn);
    private void HandlerSetSquareWhiteKnight(object? sender, EventArgs e) => EditSquare(Piece.WhiteKnight);
    private void HandlerSetSquareWhiteBishop(object? sender, EventArgs e) => EditSquare(Piece.WhiteBishop);
    private void HandlerSetSquareWhiteRook(object? sender, EventArgs e) => EditSquare(Piece.WhiteRook);
    private void HandlerSetSquareWhiteQueen(object? sender, EventArgs e) => EditSquare(Piece.WhiteQueen);
    private void HandlerSetSquareWhiteKing(object? sender, EventArgs e) => EditSquare(Piece.WhiteKing);
    private void HandlerSetSquareBlackPawn(object? sender, EventArgs e) => EditSquare(Piece.BlackPawn);
    private void HandlerSetSquareBlackKnight(object? sender, EventArgs e) => EditSquare(Piece.BlackKnight);
    private void HandlerSetSquareBlackBishop(object? sender, EventArgs e) => EditSquare(Piece.BlackBishop);
    private void HandlerSetSquareBlackRook(object? sender, EventArgs e) => EditSquare(Piece.BlackRook);
    private void HandlerSetSquareBlackQueen(object? sender, EventArgs e) => EditSquare(Piece.BlackQueen);
    private void HandlerSetSquareBlackKing(object? sender, EventArgs e) => EditSquare(Piece.BlackKing);
    private void ClearSquareHandler(object? sender, EventArgs e) => EditSquare(Piece.None);

    private void ClearBoardHandler(object? sender, EventArgs e)
    {
        Board board = Board.Empty();
        board.SetSideToMove(DisplayBoard.SideToMove);
        ApplyEditedBoard(board);
    }

    // ---------------------------------------------------------------------------------------------
    // Menus
    // ---------------------------------------------------------------------------------------------

    private void BuildEngineMenu()
    {
        debugToolStripMenuItem.Text = "&Engine";
        debugToolStripMenuItem.Visible = true;
        cPUVsCPUGameToolStripMenuItem.Text = "&Play engine against itself";
        var builtIn = new ToolStripMenuItem("Use &built-in engine") { Name = "builtInEngineMenuItem", Checked = true };
        var choose = new ToolStripMenuItem("&Choose UCI engine...") { Name = "chooseEngineMenuItem" };
        var output = new ToolStripMenuItem("Show engine &output in move list") { CheckOnClick = true };
        builtIn.Click += async (_, _) =>
        {
            enginePath = null;
            UpdateEngineMenu();
            await StartEngineAsync();
        };
        choose.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Choose a UCI engine", Filter = "Programs|*.exe|All files|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }
            enginePath = dialog.FileName;
            UpdateEngineMenu();
            await StartEngineAsync();
        };
        output.CheckedChanged += (_, _) => showEngineOutput = output.Checked;
        debugToolStripMenuItem.DropDownItems.Insert(0, builtIn);
        debugToolStripMenuItem.DropDownItems.Insert(1, choose);
        debugToolStripMenuItem.DropDownItems.Insert(2, output);
        debugToolStripMenuItem.DropDownItems.Insert(3, new ToolStripSeparator());
    }

    private void UpdateEngineMenu()
    {
        if (debugToolStripMenuItem.DropDownItems["builtInEngineMenuItem"] is ToolStripMenuItem builtIn)
        {
            builtIn.Checked = enginePath is null;
        }
        if (debugToolStripMenuItem.DropDownItems["chooseEngineMenuItem"] is ToolStripMenuItem choose)
        {
            choose.Checked = enginePath is not null;
        }
    }

    private void FileSaveGameHandler(object? sender, EventArgs e)
    {
        if (IsEditing)
        {
            MessageBox.Show(this, "Finish editing the position before saving.", "Save game", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            using Stream stream = saveFileDialog.OpenFile();
            SavedGame.FromGame(game, humanColour, isBoardFlipped, level).Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Unable to save the game:\n{ex.Message}", "Save game", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FileLoadGameHandler(object? sender, EventArgs e)
    {
        if (openFileDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        SavedGame? saved;
        try
        {
            using Stream stream = openFileDialog.OpenFile();
            saved = SavedGame.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Unable to load the game:\n{ex.Message}", "Load game", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Game? loaded = saved?.ToGame();
        if (saved is null || loaded is null)
        {
            MessageBox.Show(this, "This file does not contain a game in Stoopid Chess format.", "Load game", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        CancelThinking(discardResult: true);
        game = loaded;
        editedBoard = null;
        humanColour = saved.HumanColour;
        isBoardFlipped = saved.IsBoardFlipped;
        SetLevel(saved.Level);
        lastMove = game.Board.LastMove is Move last ? (last.From, last.To) : null;
        ResetClocks();
        engine?.NewGame();
        RefreshAll();
        if (IsEngineTurn)
        {
            StartThinking();
        }
    }

    private void FileExitHandler(object? sender, EventArgs e) => Close();

    private void NewGameWhiteHandler(object? sender, EventArgs e) => NewGame(Colour.White);

    private void NewGameBlackHandler(object? sender, EventArgs e) => NewGame(Colour.Black);

    private void TestGameHandler(object? sender, EventArgs e) => NewGame(Colour.None);

    private void UndoLastMoveHandler(object? sender, EventArgs e)
    {
        if (IsEditing)
        {
            return;
        }
        CancelThinking(discardResult: true);
        game.UndoMove();
        if (humanColour != Colour.None && game.SideToMove != humanColour)
        {
            game.UndoMove();    // Take back the engine's reply as well so that it is the human's turn
        }
        lastMove = game.Board.LastMove is Move last ? (last.From, last.To) : null;
        RefreshAll();
        clockTimer.Start();
        if (IsEngineTurn)
        {
            StartThinking();
        }
    }

    private void SwitchSidesHandler(object? sender, EventArgs e)
    {
        if (humanColour == Colour.None)
        {
            return;
        }
        humanColour = humanColour.Opponent();
        isBoardFlipped = humanColour == Colour.Black;
        UpdateStatus();
        chessBoardPictureBox.Invalidate();
        if (IsEngineTurn)
        {
            StartThinking();
        }
        else
        {
            CancelThinking(discardResult: true);
        }
    }

    private void ViewFlipBoardHandler(object? sender, EventArgs e)
    {
        isBoardFlipped = !isBoardFlipped;
        chessBoardPictureBox.Invalidate();
    }

    private void StopThinkingButtonHandler(object? sender, EventArgs e) => CancelThinking(discardResult: false);

    private void SetLevelBeginnerHandler(object? sender, EventArgs e) => SetLevel(Level.Beginner);

    private void SetLevelEasyHandler(object? sender, EventArgs e) => SetLevel(Level.Easy);

    private void SetLevelMediumHandler(object? sender, EventArgs e) => SetLevel(Level.Medium);

    private void SetLevelHardHandler(object? sender, EventArgs e) => SetLevel(Level.Hard);

    private void SetLevelBestHandler(object? sender, EventArgs e) => SetLevel(Level.Best);

    private void MenuHelpHandler(object? sender, EventArgs e)
    {
        using var help = new HelpForm();
        help.ShowDialog(this);
    }
}
