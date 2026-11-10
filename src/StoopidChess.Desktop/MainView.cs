using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop.Dialogs;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Desktop;

/// <summary>
/// The main window's content: the menu bar across the top, the board with the capture bin under it on the left, the
/// move list with "Move now!" under it in a column at the right, and the status bar along the foot - and the wiring
/// between them and the game. All game and engine logic lives in <see cref="GameController"/>; the view redraws
/// whenever it raises <see cref="GameController.Changed"/>, its text from <see cref="GameText"/>. What it needs of the window system - file dialogs, the clipboard, the
/// title, another window - it asks of an <see cref="IDesktopPlatform"/>. It owns the controller and disposes it.
/// </summary>
internal sealed class MainView : View, IDisposable
{
    /// <summary>The window's default size.</summary>
    public static readonly SKSizeI DefaultSize = new(860, 660);

    /// <summary>The window's least size.</summary>
    public static readonly SKSizeI MinimumSize = new(680, 540);

    /// <summary>The right column's least width: it takes all the width the board leaves, and never less.</summary>
    public const float ColumnWidth = 290.0f;

    /// <summary>The capture bin's height.</summary>
    public const float CaptureBinHeight = 68.0f;

    /// <summary>The margin round the panels, and between them.</summary>
    public const float Margin = 4.0f;

    private const string HelpText =
        "To make a move, click the piece you wish to move, drag it to the target square and drop it. "
        + "Stoopid Chess will not allow illegal moves.\n\n"
        + "To make a castling move, move the king to its new location.\n\n"
        + "To edit the position, right click the square you wish to edit then select the new piece to be "
        + "placed there. Editing a position in this way may affect castling rights and en passant status, "
        + "and will erase the move undo list.\n\n"
        + "Each side must have strictly one king on the board before play is allowed to resume following "
        + "a position edit.";

    private static readonly FileFilter ChessFiles = new("Stoopid Chess games", "chess");
    private static readonly FileFilter PgnFiles = new("PGN", "pgn");

    private static readonly TimeControl[] TimeControls =
    [
        TimeControl.OfMinutes(1, 0),
        TimeControl.OfMinutes(3, 2),
        TimeControl.OfMinutes(5, 3),
        TimeControl.OfMinutes(10, 0),
        TimeControl.OfMinutes(15, 10),
        TimeControl.OfMinutes(30, 0),
    ];

    private static readonly (string Header, Level Level)[] LevelChoices =
    [
        ("Beginner", Level.Beginner),
        ("Easy", Level.Easy),
        ("Medium", Level.Medium),
        ("Hard", Level.Hard),
        ("Best (slow)", Level.Best),
    ];

    private readonly IDesktopPlatform platform;
    private readonly GameController controller;
    private readonly SynchronizationContext? uiContext = SynchronizationContext.Current;
    private readonly List<(MenuItem Item, TimeControl Clock)> timeControlItems = [];
    private readonly List<(MenuItem Item, Level Level)> levelItems = [];
    private readonly MenuItem engineMenu = new("Engine");
    private readonly MenuItem builtInEngineItem;
    private readonly MenuItem showEngineOutputItem;
    private readonly List<MenuItem> editMenu;
    private Square editSquare = Square.None;      // Square the edit menu was opened on
    private string? shownTitle;
    private IDesktopWindow? analysisWindow;

    public MainView(IDesktopPlatform platform, GameController controller)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(controller);
        this.platform = platform;
        this.controller = controller;
        builtInEngineItem = new MenuItem("Use built-in engine", () => _ = StartEngineSafelyAsync(null));
        showEngineOutputItem = new MenuItem("Show engine output in move list", () =>
        {
            showEngineOutputItem!.IsChecked = !showEngineOutputItem.IsChecked;
            UpdateMoveList();
        });
        BuildMenus();
        editMenu = BuildEditMenu();

        Board.Controller = controller;
        CaptureBin.Controller = controller;
        Board.MoveDropped += (from, to) => _ = OnMoveDroppedAsync(from, to);
        Board.EditRequested += OpenEditMenu;
        Board.EditingIncomplete += () => _ = ShowMessageAsync("Editing position",
            "The position is not legal yet. Each side needs exactly one king, no pawns on the first or "
            + "last rank, and the side that is not to move may not be in check.");
        MoveNowButton.Clicked += controller.MoveNow;
        controller.Changed += (_, _) => Refresh();
        controller.EngineFailed += (_, message) => _ = ShowMessageAsync("Engine",
            message + "\n\nChoose an engine from the Engine menu to continue.");
        controller.EngineIllegalMove += (_, move) => _ = ShowMessageAsync("Engine", $"The engine played an illegal move: {move}");
        controller.EngineInfo += (_, line) => OnUiThread(() =>
        {
            if (Analysis is { } analysis && GameText.InfoLine(line) is { } formatted)
            {
                analysis.Append(formatted);
            }
        });

        AddChild(MenuBar);
        AddChild(Board);
        AddChild(CaptureBin);
        AddChild(MoveList);
        AddChild(MoveNowButton);
        AddChild(StatusBar);
        Refresh();
    }

    /// <summary>The menu bar.</summary>
    public MenuBarView MenuBar { get; } = new();

    /// <summary>The board.</summary>
    public BoardView Board { get; } = new();

    /// <summary>The captured pieces.</summary>
    public CaptureBinView CaptureBin { get; } = new();

    /// <summary>The moves, in SAN.</summary>
    public TextListView MoveList { get; } = new();

    /// <summary>"Move now!": the engine stops thinking and plays its best move so far.</summary>
    public PlateButton MoveNowButton { get; } = new("Move now!");

    /// <summary>The status bar.</summary>
    public StatusBarView StatusBar { get; } = new();

    /// <summary>The Analysis window's content while it is open; null otherwise.</summary>
    public AnalysisView? Analysis { get; private set; }

    /// <summary>The engine menu's rows, as they stand.</summary>
    public IReadOnlyList<MenuItem> EngineMenuItems => engineMenu.Items;

    /// <summary>
    /// Start the built-in engine, as the window opens.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            await controller.StartEngineAsync(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ShowMessageAsync("Engine", $"The engine could not be started:\n{ex.Message}");
        }
    }

    /// <summary>
    /// The clock ticked: <paramref name="elapsed"/> has passed since the last tick.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        controller.Tick(elapsed);
        UpdateStatusBar();
    }

    /// <summary>
    /// Close the Analysis window, and stop the engine.
    /// </summary>
    public void Dispose()
    {
        analysisWindow?.Close();
        controller.Dispose();
    }

    /// <summary>
    /// The board as large as the room left of the right column's least width allows, square and a whole number of
    /// pixels per square, with the capture bin under it, as wide; the right column takes every pixel beside the board,
    /// so nothing stands empty between them. The column is never narrower than <see cref="ColumnWidth"/>.
    /// </summary>
    protected override void Layout()
    {
        MenuBar.SetBounds(SKRect.Create(0, 0, Width, MenuBarView.BarHeight));
        StatusBar.SetBounds(SKRect.Create(0, Height - StatusBarView.BarHeight, Width, StatusBarView.BarHeight));
        float top = MenuBarView.BarHeight + Margin;
        float bottom = Height - StatusBarView.BarHeight - Margin;
        float roomAcross = Width - ColumnWidth - (3 * Margin);
        float roomDown = bottom - top - CaptureBinHeight - Margin;
        float square = Math.Max(0, MathF.Floor((Math.Min(roomAcross, roomDown) - (2 * BoardView.Edge)) / 8));
        float side = (8 * square) + (2 * BoardView.Edge);
        Board.SetBounds(SKRect.Create(Margin, top, side, side));
        CaptureBin.SetBounds(SKRect.Create(Margin, Board.Bounds.Bottom + Margin, side, CaptureBinHeight));
        float columnLeft = Board.Bounds.Right + Margin;
        MoveList.SetBounds(new SKRect(columnLeft, top, Width - Margin, bottom - DialogView.ButtonHeight - Margin));
        MoveNowButton.SetBounds(new SKRect(columnLeft, bottom - DialogView.ButtonHeight, Width - Margin, bottom));
    }

    // ---------------------------------------------------------------------------------------------
    // Menus
    // ---------------------------------------------------------------------------------------------

    private static string Keys(Key key, KeyModifiers modifiers = KeyModifiers.Primary) => Shortcut.Text(key, modifiers);

    private void BuildMenus()
    {
        var file = new MenuItem("File");
        file.Items.AddRange(
        [
            new MenuItem("Save game", () => _ = SaveGameAsync()) { Shortcut = Keys(Key.S) },
            new MenuItem("Load game", () => _ = LoadGameAsync()) { Shortcut = Keys(Key.O) },
            MenuItem.Separator(),
            new MenuItem("Import PGN...", () => _ = ImportPgnAsync()),
            new MenuItem("Export PGN...", () => _ = ExportPgnAsync()),
            MenuItem.Separator(),
            new MenuItem("Exit", platform.Quit) { Shortcut = Keys(Key.Q) },
        ]);
        var edit = new MenuItem("Edit");
        edit.Items.AddRange(
        [
            new MenuItem("Copy FEN", CopyFen) { Shortcut = Keys(Key.C) },
            new MenuItem("Paste FEN", () => _ = PasteFenAsync()) { Shortcut = Keys(Key.V) },
        ]);
        var newGame = new MenuItem("New Game");
        newGame.Items.AddRange(
        [
            new MenuItem("White", () => controller.NewGame(Color.White)) { Shortcut = Keys(Key.N) },
            new MenuItem("Black", () => controller.NewGame(Color.Black)) { Shortcut = Keys(Key.N, KeyModifiers.Primary | KeyModifiers.Shift) },
        ]);
        var timeControl = new MenuItem("Time control");
        foreach (TimeControl clock in TimeControls)
        {
            var item = new MenuItem(clock.ToString(), () => controller.SetTimeControl(clock));
            timeControlItems.Add((item, clock));
            timeControl.Items.Add(item);
        }
        var level = new MenuItem("Set level");
        foreach ((string header, Level value) in LevelChoices)
        {
            var item = new MenuItem(header, () => SetLevel(value));
            levelItems.Add((item, value));
            level.Items.Add(item);
        }
        var game = new MenuItem("Game");
        game.Items.AddRange(
        [
            newGame,
            new MenuItem("Undo last move", controller.Undo) { Shortcut = Keys(Key.Z) },
            new MenuItem("Switch sides", controller.SwitchSides),
            timeControl,
            level,
        ]);
        var view = new MenuItem("View");
        view.Items.AddRange(
        [
            new MenuItem("Flip position", FlipBoard) { Shortcut = Keys(Key.F) },
            new MenuItem("Analysis window", ToggleAnalysis) { Shortcut = Keys(Key.A) },
        ]);
        BuildEngineMenu();
        MenuBar.Menus.AddRange([file, edit, game, view, engineMenu, new MenuItem("Help", () => _ = ShowHelpAsync())]);
    }

    private void BuildEngineMenu()
    {
        engineMenu.Items.AddRange(
        [
            builtInEngineItem,
            new MenuItem("Open UCI engine...", () => _ = OpenEngineAsync()),
            MenuItem.Separator(),
            showEngineOutputItem,
        ]);
    }

    private List<MenuItem> BuildEditMenu()
    {
        MenuItem PieceSubmenu(Color color)
        {
            var submenu = new MenuItem(color.ToString());
            foreach (Piece type in new[] { Piece.Pawn, Piece.Knight, Piece.Bishop, Piece.Rook, Piece.Queen, Piece.King })
            {
                submenu.Items.Add(new MenuItem(type.ToString(), () => controller.EditSquare(editSquare, (color, type))));
            }
            return submenu;
        }
        return
        [
            new MenuItem("Clear square", () => controller.EditSquare(editSquare, default)),
            PieceSubmenu(Color.White),
            PieceSubmenu(Color.Black),
            new MenuItem("Clear entire position", controller.ClearBoard),
            MenuItem.Separator(),
            new MenuItem("White to move", () => controller.SetSideToMove(Color.White)),
            new MenuItem("Black to move", () => controller.SetSideToMove(Color.Black)),
        ];
    }

    private void OpenEditMenu(Square square, SKPoint at)
    {
        editSquare = square;
        if (Root is { } root)
        {
            MenuPopup.Open(root, editMenu, at);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Display
    // ---------------------------------------------------------------------------------------------

    private void Refresh()
    {
        string title = GameText.Title(controller);
        if (title != shownTitle)
        {
            shownTitle = title;
            platform.SetTitle(title);
        }
        MoveNowButton.IsEnabled = controller.IsThinking;
        UpdateMoveList();
        UpdateStatusBar();
        builtInEngineItem.IsChecked = controller.EnginePath is null;
        foreach ((MenuItem item, TimeControl clock) in timeControlItems)
        {
            item.IsChecked = clock == controller.TimeControl;
        }
        foreach ((MenuItem item, Level level) in levelItems)
        {
            item.IsChecked = level == controller.Level;
        }
        Invalidate();
    }

    private void UpdateMoveList()
    {
        MoveList.SetText(GameText.MoveList(controller, showEngineOutputItem.IsChecked));
        if (!controller.IsEditing)
        {
            MoveList.ScrollToEnd();
        }
    }

    private void UpdateStatusBar()
    {
        bool isClockRunning = !controller.IsGameOver && !controller.IsEditing;
        StatusBar.Segments =
        [
            new StatusSegment("You are playing:", GameText.HumanColor(controller)),
            new StatusSegment("Color to move:", controller.SideToMove.ToString()),
            new StatusSegment("Difficulty:", GameText.Difficulty(controller)),
            new StatusSegment("White clock:", GameText.Clock(controller.WhiteClock), IsClock: true,
                IsRunning: isClockRunning && controller.SideToMove == Color.White),
            new StatusSegment("Black clock:", GameText.Clock(controller.BlackClock), IsClock: true,
                IsRunning: isClockRunning && controller.SideToMove == Color.Black),
        ];
    }

    // ---------------------------------------------------------------------------------------------
    // Keyboard
    // ---------------------------------------------------------------------------------------------

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Modifiers == KeyModifiers.Primary)
        {
            switch (e.Key)
            {
                case Key.C: CopyFen(); return true;
                case Key.V: _ = PasteFenAsync(); return true;
                case Key.N: controller.NewGame(Color.White); return true;
                case Key.Z: controller.Undo(); return true;
                case Key.S: _ = SaveGameAsync(); return true;
                case Key.O: _ = LoadGameAsync(); return true;
                case Key.F: FlipBoard(); return true;
                case Key.A: ToggleAnalysis(); return true;
                case Key.Q: platform.Quit(); return true;
                default: return false;
            }
        }
        if (e.Is(Key.N, KeyModifiers.Primary | KeyModifiers.Shift))
        {
            controller.NewGame(Color.Black);
            return true;
        }
        if (e.Modifiers == KeyModifiers.Alt && e.Key is >= Key.A and <= Key.Z)
        {
            return MenuBar.OpenByLetter((char)('A' + (e.Key - Key.A)));
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Board input
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The board says a piece was dropped from one square onto another. Ask the controller which legal moves that is:
    /// none is refused with a message, several (a pawn reaching the last rank) ask which piece to promote to, and one is
    /// played. The controller then does the rest: it plays the move, redraws us through <c>Changed</c> and starts the
    /// engine thinking.
    /// </summary>
    private async Task OnMoveDroppedAsync(Square from, Square to)
    {
        IReadOnlyList<Move> candidates = controller.HumanMoveCandidates(from, to);
        if (candidates.Count == 0)
        {
            if (controller.IsHumanTurn)
            {
                await ShowMessageAsync("Illegal move", "That move is not permitted.");
            }
            return;
        }
        Piece promotion = Piece.None;
        if (candidates.Count > 1)
        {
            var dialog = new PromotionDialog(controller.HumanColor);
            await ShowDialogAsync(dialog);
            promotion = dialog.Choice;
            if (promotion == Piece.None)
            {
                return;     // Escape cancels the move: nothing is promoted by default
            }
        }
        controller.TryHumanMove(from, to, promotion);
    }

    // ---------------------------------------------------------------------------------------------
    // Game and view actions
    // ---------------------------------------------------------------------------------------------

    private void SetLevel(Level level)
    {
        controller.Level = level;
        Refresh();
    }

    private void FlipBoard()
    {
        controller.IsBoardFlipped = !controller.IsBoardFlipped;
        Board.Invalidate();
    }

    private void ToggleAnalysis()
    {
        if (analysisWindow is { } open)
        {
            open.Activate();
            return;
        }
        var analysis = new AnalysisView();
        var root = new ViewRoot { Content = analysis };
        Analysis = analysis;
        analysisWindow = platform.OpenWindow("Analysis", 720, 400, root, () =>
        {
            analysisWindow = null;
            Analysis = null;
        });
    }

    private void CopyFen() => platform.SetClipboardText(controller.CurrentFen);

    private async Task PasteFenAsync()
    {
        string? text = platform.GetClipboardText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (Fen.TryParse(text.Trim()) is not { } position)
        {
            await ShowMessageAsync("Paste FEN", "The clipboard does not hold a valid FEN position.");
            return;
        }
        controller.SetPosition(position);
    }

    private Task ShowHelpAsync() => ShowMessageAsync($"Stoopid Chess {Program.Version}", HelpText);

    // ---------------------------------------------------------------------------------------------
    // Files
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="path"/> with <paramref name="extension"/> added where it has none: a save dialog does not add
    /// one on every platform.
    /// </summary>
    internal static string WithExtension(string path, string extension) =>
        Path.HasExtension(path) ? path : $"{path}.{extension}";

    private async Task SaveGameAsync()
    {
        SavedGame? saved = controller.Save();
        if (saved is null)
        {
            await ShowMessageAsync("Save game", "Finish editing the position before saving.");
            return;
        }
        if (await ChooseFileAsync(() => platform.ChooseFileToSaveAsync("Save game", "game.chess", [ChessFiles])) is not { } chosen)
        {
            return;
        }
        try
        {
            using FileStream stream = File.Create(WithExtension(chosen, "chess"));
            saved.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Save game", $"Unable to save the game:\n{ex.Message}");
        }
    }

    private async Task LoadGameAsync()
    {
        if (await ChooseFileAsync(() => platform.ChooseFileToOpenAsync("Load game", [ChessFiles, FileFilter.All])) is not { } path)
        {
            return;
        }
        SavedGame? saved;
        try
        {
            using FileStream stream = File.OpenRead(path);
            saved = SavedGame.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Load game", $"Unable to load the game:\n{ex.Message}");
            return;
        }
        if (saved is null || !controller.Load(saved))
        {
            await ShowMessageAsync("Load game", "This file does not contain a game in Stoopid Chess format.");
        }
    }

    private async Task ImportPgnAsync()
    {
        if (await ChooseFileAsync(() => platform.ChooseFileToOpenAsync("Import PGN", [PgnFiles, FileFilter.All])) is not { } path)
        {
            return;
        }
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Import PGN", $"Unable to read the file:\n{ex.Message}");
            return;
        }
        if (Pgn.Parse(text) is not { } game)
        {
            await ShowMessageAsync("Import PGN", "The file does not hold a readable PGN game.");
            return;
        }
        controller.Load(new SavedGame
        {
            StartFen = game.StartFen,
            Moves = [.. game.UciMoves],
            HumanColor = controller.HumanColor,
            IsBoardFlipped = controller.IsBoardFlipped,
            Level = controller.Level,
        });
    }

    private async Task ExportPgnAsync()
    {
        if (controller.IsEditing)
        {
            await ShowMessageAsync("Export PGN", "Finish editing the position before exporting.");
            return;
        }
        if (await ChooseFileAsync(() => platform.ChooseFileToSaveAsync("Export PGN", "game.pgn", [PgnFiles])) is not { } chosen)
        {
            return;
        }
        (string white, string black) = GameText.PlayerNames(controller);
        string pgn = Pgn.Write(controller.Game, white, black, GameText.Result(controller), DateOnly.FromDateTime(DateTime.Now));
        try
        {
            await File.WriteAllTextAsync(WithExtension(chosen, "pgn"), pgn);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Export PGN", $"Unable to save the file:\n{ex.Message}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The opponent engine
    // ---------------------------------------------------------------------------------------------

    private async Task<bool> StartEngineSafelyAsync(string? path)
    {
        try
        {
            return await controller.StartEngineAsync(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ShowMessageAsync("Engine", $"The engine could not be started:\n{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pick any UCI engine's executable and play against it instead of the built-in one, for this session.
    /// </summary>
    private async Task OpenEngineAsync()
    {
        if (await ChooseFileAsync(() => platform.ChooseFileToOpenAsync("Choose a UCI engine", [FileFilter.All])) is { } path)
        {
            await StartEngineSafelyAsync(path);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Dialogs and threads
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A file dialog's answer: the file chosen, or null when it was cancelled or could not open (which is said).
    /// </summary>
    private async Task<string?> ChooseFileAsync(Func<Task<string?>> ask)
    {
        try
        {
            return await ask();
        }
        catch (InvalidOperationException ex)
        {
            await ShowMessageAsync("Files", $"The file dialog could not be opened:\n{ex.Message}");
            return null;
        }
    }

    private Task ShowDialogAsync(DialogView dialog) => Root is { } root ? dialog.ShowAsync(root) : Task.CompletedTask;

    private Task ShowMessageAsync(string title, string message) => ShowDialogAsync(new MessageDialog(title, message));

    /// <summary>
    /// Run <paramref name="action"/> on the thread the view was made on (the engine reports from its own).
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (uiContext is null)
        {
            action();
        }
        else
        {
            uiContext.Post(_ => action(), null);
        }
    }
}
