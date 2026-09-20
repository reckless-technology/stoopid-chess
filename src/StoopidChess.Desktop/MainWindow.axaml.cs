using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using StoopidChess.Core;
using StoopidChess.Play;
using Color = StoopidChess.Core.Color;   // The chess color, not Avalonia.Media.Color

namespace StoopidChess.Desktop;

/// <summary>
/// The main window: menus, status bar and the wiring between the board control and the game. All
/// game and engine logic lives in <see cref="GameController"/>; the window redraws whenever it
/// raises <see cref="GameController.Changed"/>. A port of the WinForms <c>MainForm</c>.
/// </summary>
public sealed partial class MainWindow : Window, IDisposable
{
    private const string HelpText =
        "To make a move, click the piece you wish to move, drag it to the target square and drop it. "
        + "Stoopid Chess will not allow illegal moves.\n\n"
        + "To make a castling move, move the king to its new location.\n\n"
        + "To edit the position, right click the square you wish to edit then select the new piece to be "
        + "placed there. Editing a position in this way may affect castling rights and en passant status, "
        + "and will erase the move undo list.\n\n"
        + "Each side must have strictly one king on the board before play is allowed to resume following "
        + "a position edit.";

    private static readonly FilePickerFileType ChessFileType = new("chess") { Patterns = ["*.chess"] };

    private readonly GameController controller = new();
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly MenuFlyout editMenu;
    private Square editSquare = Square.None;      // Square the edit context menu was opened on

    public MainWindow()
    {
        InitializeComponent();
        board.Controller = controller;
        captureBin.Controller = controller;
        board.MoveDropped += OnMoveDropped;
        board.EditRequested += OnEditRequested;
        board.EditingIncomplete += () => _ = MessageDialog.ShowAsync(this, "Editing position",
            "The position is not legal yet. Each side needs exactly one king, no pawns on the first or "
            + "last rank, and the side that is not to move may not be in check.");
        editMenu = BuildEditMenu();
        controller.Changed += (_, _) => RefreshAll();
        controller.EngineFailed += (_, message) => _ = MessageDialog.ShowAsync(this, "Engine",
            message + "\n\nChoose an engine from the Engine menu to continue.");
        controller.EngineIllegalMove += (_, move) => _ = MessageDialog.ShowAsync(this, "Engine",
            $"The engine played an illegal move: {move}");
        clockTimer.Tick += OnClockTick;
        stopThinkingButton.IsEnabled = false;
        RefreshAll();
        clockTimer.Start();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        try
        {
            await controller.StartEngineAsync(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An async void handler must not let an exception escape, or the process dies
            await MessageDialog.ShowAsync(this, "Engine", $"The engine could not be started:\n{ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        clockTimer.Stop();
        controller.Dispose();
    }

    // ---------------------------------------------------------------------------------------------
    // Display
    // ---------------------------------------------------------------------------------------------

    private void RefreshAll()
    {
        Title = controller.EngineName is null ? "Stoopid Chess" : $"Stoopid Chess - {controller.EngineName}";
        stopThinkingButton.IsEnabled = controller.IsThinking;
        UpdateMoveList();
        colorToMoveLabel.Text = controller.SideToMove.ToString();
        humanColorLabel.Text = controller.HumanColor == Color.None ? "Nobody (engine vs engine)" : controller.HumanColor.ToString();
        difficultyLabel.Text = controller.Level.ToString();
        UpdateClockLabels();
        builtInEngineMenuItem.IsChecked = controller.EnginePath is null;
        chooseEngineMenuItem.IsChecked = controller.EnginePath is not null;
        board.InvalidateVisual();
        captureBin.InvalidateVisual();
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
        Color mover = game.StartSideToMove;
        int moveNumber = game.StartMoveNumber;
        bool lineOpen = false;
        if (mover == Color.Black && game.Moves.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{moveNumber,2}. {"...",-9}");
            lineOpen = true;
        }
        foreach (Move move in game.Moves)
        {
            if (mover == Color.White)
            {
                text.Append(CultureInfo.InvariantCulture, $"{moveNumber,2}. ");
                lineOpen = true;
            }
            text.Append(CultureInfo.InvariantCulture, $"{move.ToLongAlgebraic(),-9}");
            if (mover == Color.Black)
            {
                text.AppendLine();
                moveNumber++;
                lineOpen = false;
            }
            mover = mover.Opponent();
        }
        if (lineOpen)
        {
            text.AppendLine();
        }
        if (game.Moves.Count == 0 && game.StartFen != Core.Position.StartFen)
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
        if (showEngineOutputMenuItem.IsChecked && controller.LastEngineInfo.Length != 0)
        {
            text.AppendLine().Append(controller.LastEngineInfo);
        }
        moveListTextBox.Text = text.ToString();
        moveListTextBox.CaretIndex = moveListTextBox.Text?.Length ?? 0;
    }

    private void UpdateClockLabels()
    {
        whiteClockLabel.Text = controller.WhiteClock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        blackClockLabel.Text = controller.BlackClock.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        controller.Tick(clockTimer.Interval);
        UpdateClockLabels();
    }

    // ---------------------------------------------------------------------------------------------
    // Board input
    // ---------------------------------------------------------------------------------------------

    private async void OnMoveDropped(Square from, Square to)
    {
        IReadOnlyList<Move> candidates = controller.HumanMoveCandidates(from, to);
        if (candidates.Count == 0)
        {
            if (controller.IsHumanTurn)
            {
                await MessageDialog.ShowAsync(this, "Illegal move", "That move is not permitted.");
            }
            return;
        }
        Piece promotion = Piece.None;
        if (candidates.Count > 1)
        {
            var dialog = new PromotionDialog(controller.HumanColor);
            await dialog.ShowDialog(this);
            promotion = dialog.Choice;
        }
        controller.TryHumanMove(from, to, promotion);
    }

    private void OnEditRequested(Square square)
    {
        editSquare = square;
        editMenu.ShowAt(board, showAtPointer: true);
    }

    private MenuFlyout BuildEditMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(EditMenuItem("Clear square", () => controller.EditSquare(editSquare, (Color.None, Piece.None))));
        menu.Items.Add(PieceSubmenu(Color.White));
        menu.Items.Add(PieceSubmenu(Color.Black));
        menu.Items.Add(EditMenuItem("Clear entire position", controller.ClearBoard));
        menu.Items.Add(new Separator());
        menu.Items.Add(EditMenuItem("White to move", () => controller.SetSideToMove(Color.White)));
        menu.Items.Add(EditMenuItem("Black to move", () => controller.SetSideToMove(Color.Black)));
        return menu;
    }

    private MenuItem PieceSubmenu(Color color)
    {
        var submenu = new MenuItem { Header = color.ToString() };
        foreach (Piece type in new[] { Piece.Pawn, Piece.Knight, Piece.Bishop, Piece.Rook, Piece.Queen, Piece.King })
        {
            submenu.Items.Add(EditMenuItem(type.ToString(), () => controller.EditSquare(editSquare, (color, type))));
        }
        return submenu;
    }

    private static MenuItem EditMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // ---------------------------------------------------------------------------------------------
    // Menus
    // ---------------------------------------------------------------------------------------------

    private async void OnUseBuiltInEngine(object? sender, RoutedEventArgs e)
    {
        try
        {
            await controller.StartEngineAsync(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await MessageDialog.ShowAsync(this, "Engine", $"The engine could not be started:\n{ex.Message}");
        }
    }

    private async void OnChooseEngine(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a UCI engine",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.All],
        });
        RefreshAll();   // The click ticked the checkbox; put it back until an engine really starts
        if (files.Count == 1 && files[0].TryGetLocalPath() is string path)
        {
            try
            {
                await controller.StartEngineAsync(path);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                await MessageDialog.ShowAsync(this, "Engine", $"The engine could not be started:\n{ex.Message}");
            }
        }
    }

    private void OnShowEngineOutputToggled(object? sender, RoutedEventArgs e) => UpdateMoveList();

    private async void OnSaveGame(object? sender, RoutedEventArgs e)
    {
        SavedGame? saved = controller.Save();
        if (saved is null)
        {
            await MessageDialog.ShowAsync(this, "Save game", "Finish editing the position before saving.");
            return;
        }
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save game",
            DefaultExtension = "chess",
            SuggestedFileName = "game.chess",
            FileTypeChoices = [ChessFileType],
        });
        if (file is null)
        {
            return;
        }
        try
        {
            Stream stream = await file.OpenWriteAsync();
            await using (stream.ConfigureAwait(true))
            {
                saved.Save(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Save game", $"Unable to save the game:\n{ex.Message}");
        }
    }

    private async void OnLoadGame(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load game",
            AllowMultiple = false,
            FileTypeFilter = [ChessFileType, FilePickerFileTypes.All],
        });
        if (files.Count != 1)
        {
            return;
        }
        SavedGame? saved;
        try
        {
            Stream stream = await files[0].OpenReadAsync();
            await using (stream.ConfigureAwait(true))
            {
                saved = SavedGame.Load(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Load game", $"Unable to load the game:\n{ex.Message}");
            return;
        }
        if (saved is null || !controller.Load(saved))
        {
            await MessageDialog.ShowAsync(this, "Load game", "This file does not contain a game in Stoopid Chess format.");
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
    private void OnNewGameWhite(object? sender, RoutedEventArgs e) => controller.NewGame(Color.White);
    private void OnNewGameBlack(object? sender, RoutedEventArgs e) => controller.NewGame(Color.Black);
    private void OnEngineVersusEngine(object? sender, RoutedEventArgs e) => controller.NewGame(Color.None);
    private void OnUndoMove(object? sender, RoutedEventArgs e) => controller.Undo();
    private void OnSwitchSides(object? sender, RoutedEventArgs e) => controller.SwitchSides();
    private void OnMoveNow(object? sender, RoutedEventArgs e) => controller.MoveNow();

    private void OnFlipBoard(object? sender, RoutedEventArgs e)
    {
        controller.IsBoardFlipped = !controller.IsBoardFlipped;
        board.InvalidateVisual();
    }

    private void SetLevel(Level level)
    {
        controller.Level = level;
        difficultyLabel.Text = level.ToString();
    }

    private void OnLevelBeginner(object? sender, RoutedEventArgs e) => SetLevel(Level.Beginner);
    private void OnLevelEasy(object? sender, RoutedEventArgs e) => SetLevel(Level.Easy);
    private void OnLevelMedium(object? sender, RoutedEventArgs e) => SetLevel(Level.Medium);
    private void OnLevelHard(object? sender, RoutedEventArgs e) => SetLevel(Level.Hard);
    private void OnLevelBest(object? sender, RoutedEventArgs e) => SetLevel(Level.Best);

    private async void OnHelp(object? sender, RoutedEventArgs e)
    {
        string version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        await MessageDialog.ShowAsync(this, $"Stoopid Chess {version}".TrimEnd(), HelpText);
    }
}
