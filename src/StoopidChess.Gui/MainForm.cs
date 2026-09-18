using System.Globalization;
using System.Text;
using StoopidChess.Core;
using StoopidChess.Play;
using Timer = System.Windows.Forms.Timer;

namespace StoopidChess.Gui;

/// <summary>
/// The main window: drawing, mouse input and menus. All game and engine logic lives in
/// <see cref="GameController"/>; the form redraws whenever it raises <see cref="GameController.Changed"/>.
/// </summary>
public partial class MainForm : Form
{
    private static readonly Pen LastMovePen = new(Color.DarkGoldenrod, 2.5f);

    private readonly GameController controller = new();
    private readonly PieceImages images = new();
    private readonly Timer clockTimer = new() { Interval = 100 };

    private int dragFrom = Squares.None;        // Square of the piece being dragged, or None
    private Point dragPosition;
    private Point contextMenuPosition;
    private bool showEngineOutput;

    public MainForm()
    {
        InitializeComponent();
        moveListTextBox.SelectionTabs = [40, 80, 120, 160, 200];
        controller.Changed += (_, _) => RefreshAll();
        controller.EngineFailed += (_, message) => MessageBox.Show(this, message + "\n\nChoose an engine from the Engine menu to continue.", "Engine", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        controller.EngineIllegalMove += (_, move) => MessageBox.Show(this, $"The engine played an illegal move: {move}", "Engine", MessageBoxButtons.OK, MessageBoxIcon.Error);
        clockTimer.Tick += OnClockTick;
        BuildEngineMenu();
        BuildSideToMoveMenu();
        stopThinkingButton.Enabled = false;
        RefreshAll();
        clockTimer.Start();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await controller.StartEngineAsync(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An async void handler must not let an exception escape, or the process dies
            MessageBox.Show(this, $"The engine could not be started:\n{ex.Message}", "Engine", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        clockTimer.Dispose();
        controller.Dispose();
        base.OnFormClosed(e);
    }

    // ---------------------------------------------------------------------------------------------
    // Display
    // ---------------------------------------------------------------------------------------------

    private void RefreshAll()
    {
        if (IsDisposed)
        {
            return;
        }
        Text = controller.EngineName is null ? "Stoopid Chess" : $"Stoopid Chess - {controller.EngineName}";
        stopThinkingButton.Enabled = controller.IsThinking;
        UpdateMoveList();
        colourToMoveLabel.Text = controller.SideToMove.ToString();
        humanColourLabel.Text = controller.HumanColour == Colour.None ? "Nobody (engine vs engine)" : controller.HumanColour.ToString();
        difficultyLabel.Text = controller.Level.ToString();
        UpdateClockLabels();
        UpdateEngineMenu();
        chessBoardPictureBox.Invalidate();
        captureBinPictureBox.Invalidate();
    }

    private void UpdateMoveList()
    {
        if (controller.IsEditing)
        {
            moveListTextBox.Text = "Position edited by user: place both kings to continue";
            return;
        }
        Game game = controller.Game;
        var text = new StringBuilder();
        Board start = Board.FromFen(game.StartFen);
        Colour mover = start.SideToMove;
        int moveNumber = start.FullMoveNumber;
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
        if (game.Moves.Count == 0 && game.StartFen != Board.StartFen)
        {
            text.AppendLine("Position edited by user");
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
        if (showEngineOutput && controller.LastEngineInfo.Length != 0)
        {
            text.AppendLine().Append(controller.LastEngineInfo);
        }
        moveListTextBox.Text = text.ToString();
        moveListTextBox.SelectionStart = moveListTextBox.Text.Length;
        moveListTextBox.ScrollToCaret();
    }

    private void UpdateClockLabels()
    {
        whiteClockLabel.Text = controller.WhiteClock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        blackClockLabel.Text = controller.BlackClock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        controller.Tick(TimeSpan.FromMilliseconds(clockTimer.Interval));
        UpdateClockLabels();
    }

    // ---------------------------------------------------------------------------------------------
    // Board drawing and mouse input
    // ---------------------------------------------------------------------------------------------

    private int SquareSize => Math.Min(chessBoardPictureBox.Width, chessBoardPictureBox.Height) / 8;

    private Point SquareOrigin(int square)
    {
        int file = Squares.FileOf(square);
        int rank = Squares.RankOf(square);
        int x = controller.IsBoardFlipped ? 7 - file : file;
        int y = controller.IsBoardFlipped ? rank : 7 - rank;
        return new Point(x * SquareSize, y * SquareSize);
    }

    private int SquareAt(Point location)
    {
        int size = SquareSize;
        int x = Math.Clamp(location.X / size, 0, 7);
        int y = Math.Clamp(location.Y / size, 0, 7);
        int file = controller.IsBoardFlipped ? 7 - x : x;
        int rank = controller.IsBoardFlipped ? y : 7 - y;
        return Squares.Index(file, rank);
    }

    private void OnBoardPaint(object? sender, PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        int size = SquareSize;
        foreach (int square in Squares.All)
        {
            Point origin = SquareOrigin(square);
            graphics.FillRectangle(Board.IsLightSquare(square) ? Brushes.Ivory : Brushes.Gray, origin.X, origin.Y, size, size);
        }
        foreach (int square in Squares.All)
        {
            Piece piece = controller.PieceAt(square);
            if (!piece.IsNone && square != dragFrom)
            {
                Point origin = SquareOrigin(square);
                graphics.DrawImage(images[piece], origin.X, origin.Y, size, size);
            }
        }
        if (!controller.IsEditing && controller.Game.Board.LastMove is Move last && !last.IsNone)
        {
            Point fromOrigin = SquareOrigin(last.From);
            Point toOrigin = SquareOrigin(last.To);
            graphics.DrawRectangle(LastMovePen, fromOrigin.X, fromOrigin.Y, size, size);
            graphics.DrawRectangle(LastMovePen, toOrigin.X, toOrigin.Y, size, size);
        }
        if (dragFrom != Squares.None && !controller.PieceAt(dragFrom).IsNone)
        {
            graphics.DrawImage(images[controller.PieceAt(dragFrom)], dragPosition.X - (size / 2), dragPosition.Y - (size / 2), size, size);
        }
    }

    private void OnCaptureBinPaint(object? sender, PaintEventArgs e)
    {
        if (controller.IsEditing)
        {
            return;
        }
        int size = Math.Min(captureBinPictureBox.Width / 16, captureBinPictureBox.Height / 2);
        foreach (Colour colour in new[] { Colour.White, Colour.Black })
        {
            int y = colour == Colour.White ? 0 : size;
            int x = 0;
            foreach (Piece piece in controller.Game.CapturedPieces(colour))
            {
                e.Graphics.DrawImage(images[piece], x, y, size, size);
                x += size;
            }
        }
    }

    private void OnBoardMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if (controller.IsEditing)
        {
            MessageBox.Show(this, "The position is not legal yet. Each side needs exactly one king, no pawns on the first or last rank, and the side that is not to move may not be in check.", "Editing position", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!controller.IsHumanTurn)
        {
            return;
        }
        int square = SquareAt(e.Location);
        if (controller.PieceAt(square).Colour == controller.HumanColour)
        {
            dragFrom = square;
            dragPosition = e.Location;
            chessBoardPictureBox.Invalidate();
        }
    }

    private void OnBoardMouseMove(object? sender, MouseEventArgs e)
    {
        if (dragFrom != Squares.None)
        {
            dragPosition = e.Location;
            chessBoardPictureBox.Invalidate();
        }
    }

    private void OnBoardMouseUp(object? sender, MouseEventArgs e)
    {
        if (dragFrom == Squares.None || e.Button != MouseButtons.Left)
        {
            return;
        }
        int from = dragFrom;
        int to = SquareAt(e.Location);
        dragFrom = Squares.None;
        chessBoardPictureBox.Invalidate();
        if (to == from)
        {
            return;
        }
        IReadOnlyList<Move> candidates = controller.HumanMoveCandidates(from, to);
        if (candidates.Count == 0)
        {
            if (controller.IsHumanTurn)
            {
                MessageBox.Show(this, "That move is not permitted.", "Illegal move", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }
        PieceType promotion = PieceType.None;
        if (candidates.Count > 1)
        {
            using var dialog = new PawnPromotionForm(controller.HumanColour, images);
            dialog.ShowDialog(this);
            promotion = dialog.Choice;
        }
        controller.TryHumanMove(from, to, promotion);
    }

    private void OnBoardMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || controller.IsThinking || (controller.IsEngineTurn && controller.HumanColour != Colour.None))
        {
            return;
        }
        contextMenuPosition = e.Location;
        contextMenuStrip.Show(chessBoardPictureBox, e.Location);
    }

    private void OnBoardSizeChanged(object? sender, EventArgs e) => chessBoardPictureBox.Invalidate();

    // ---------------------------------------------------------------------------------------------
    // Menus
    // ---------------------------------------------------------------------------------------------

    private void BuildEngineMenu()
    {
        var builtIn = new ToolStripMenuItem("Use &built-in engine") { Name = "builtInEngineMenuItem", Checked = true };
        var choose = new ToolStripMenuItem("&Choose UCI engine...") { Name = "chooseEngineMenuItem" };
        var output = new ToolStripMenuItem("Show engine &output in move list") { CheckOnClick = true };
        builtIn.Click += async (_, _) => await controller.StartEngineAsync(null);
        choose.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Choose a UCI engine", Filter = "Programs|*.exe|All files|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                await controller.StartEngineAsync(dialog.FileName);
            }
        };
        output.CheckedChanged += (_, _) =>
        {
            showEngineOutput = output.Checked;
            UpdateMoveList();
        };
        engineToolStripMenuItem.DropDownItems.Insert(0, builtIn);
        engineToolStripMenuItem.DropDownItems.Insert(1, choose);
        engineToolStripMenuItem.DropDownItems.Insert(2, output);
        engineToolStripMenuItem.DropDownItems.Insert(3, new ToolStripSeparator());
    }

    private void UpdateEngineMenu()
    {
        if (engineToolStripMenuItem.DropDownItems["builtInEngineMenuItem"] is ToolStripMenuItem builtIn)
        {
            builtIn.Checked = controller.EnginePath is null;
        }
        if (engineToolStripMenuItem.DropDownItems["chooseEngineMenuItem"] is ToolStripMenuItem choose)
        {
            choose.Checked = controller.EnginePath is not null;
        }
    }

    private void BuildSideToMoveMenu()
    {
        var whiteToMove = new ToolStripMenuItem("&White to move");
        var blackToMove = new ToolStripMenuItem("&Black to move");
        whiteToMove.Click += (_, _) => controller.SetSideToMove(Colour.White);
        blackToMove.Click += (_, _) => controller.SetSideToMove(Colour.Black);
        contextMenuStrip.Items.Add(new ToolStripSeparator());
        contextMenuStrip.Items.Add(whiteToMove);
        contextMenuStrip.Items.Add(blackToMove);
    }

    private void EditSquare(Piece piece) => controller.EditSquare(SquareAt(contextMenuPosition), piece);

    private void OnSetWhitePawn(object? sender, EventArgs e) => EditSquare(Piece.WhitePawn);
    private void OnSetWhiteKnight(object? sender, EventArgs e) => EditSquare(Piece.WhiteKnight);
    private void OnSetWhiteBishop(object? sender, EventArgs e) => EditSquare(Piece.WhiteBishop);
    private void OnSetWhiteRook(object? sender, EventArgs e) => EditSquare(Piece.WhiteRook);
    private void OnSetWhiteQueen(object? sender, EventArgs e) => EditSquare(Piece.WhiteQueen);
    private void OnSetWhiteKing(object? sender, EventArgs e) => EditSquare(Piece.WhiteKing);
    private void OnSetBlackPawn(object? sender, EventArgs e) => EditSquare(Piece.BlackPawn);
    private void OnSetBlackKnight(object? sender, EventArgs e) => EditSquare(Piece.BlackKnight);
    private void OnSetBlackBishop(object? sender, EventArgs e) => EditSquare(Piece.BlackBishop);
    private void OnSetBlackRook(object? sender, EventArgs e) => EditSquare(Piece.BlackRook);
    private void OnSetBlackQueen(object? sender, EventArgs e) => EditSquare(Piece.BlackQueen);
    private void OnSetBlackKing(object? sender, EventArgs e) => EditSquare(Piece.BlackKing);
    private void OnClearSquare(object? sender, EventArgs e) => EditSquare(Piece.None);
    private void OnClearBoard(object? sender, EventArgs e) => controller.ClearBoard();

    private void OnSaveGame(object? sender, EventArgs e)
    {
        SavedGame? saved = controller.Save();
        if (saved is null)
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
            saved.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Unable to save the game:\n{ex.Message}", "Save game", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnLoadGame(object? sender, EventArgs e)
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
        if (saved is null || !controller.Load(saved))
        {
            MessageBox.Show(this, "This file does not contain a game in Stoopid Chess format.", "Load game", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnExit(object? sender, EventArgs e) => Close();
    private void OnNewGameWhite(object? sender, EventArgs e) => controller.NewGame(Colour.White);
    private void OnNewGameBlack(object? sender, EventArgs e) => controller.NewGame(Colour.Black);
    private void OnEngineVersusEngine(object? sender, EventArgs e) => controller.NewGame(Colour.None);
    private void OnUndoMove(object? sender, EventArgs e) => controller.Undo();
    private void OnSwitchSides(object? sender, EventArgs e) => controller.SwitchSides();
    private void OnMoveNow(object? sender, EventArgs e) => controller.MoveNow();

    private void OnFlipBoard(object? sender, EventArgs e)
    {
        controller.IsBoardFlipped = !controller.IsBoardFlipped;
        chessBoardPictureBox.Invalidate();
    }

    private void SetLevel(Level level)
    {
        controller.Level = level;
        difficultyLabel.Text = level.ToString();
    }

    private void OnLevelBeginner(object? sender, EventArgs e) => SetLevel(Level.Beginner);
    private void OnLevelEasy(object? sender, EventArgs e) => SetLevel(Level.Easy);
    private void OnLevelMedium(object? sender, EventArgs e) => SetLevel(Level.Medium);
    private void OnLevelHard(object? sender, EventArgs e) => SetLevel(Level.Hard);
    private void OnLevelBest(object? sender, EventArgs e) => SetLevel(Level.Best);

    private void OnHelp(object? sender, EventArgs e)
    {
        using var help = new HelpForm();
        help.ShowDialog(this);
    }
}
