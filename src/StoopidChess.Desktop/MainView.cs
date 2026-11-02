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
/// whenever it raises <see cref="GameController.Changed"/>, its text from <see cref="GameText"/>, the engine list's
/// file through <see cref="EngineRosterFile"/>. What it needs of the window system - file dialogs, the clipboard, the
/// title, another window - it asks of an <see cref="IDesktopPlatform"/>. It owns the controller and disposes it.
/// </summary>
internal sealed class MainView : View, IDisposable
{
    /// <summary>The window's default size.</summary>
    public static readonly SKSizeI DefaultSize = new(860, 660);

    /// <summary>The window's least size.</summary>
    public static readonly SKSizeI MinimumSize = new(680, 540);

    /// <summary>The right column's width.</summary>
    public const float ColumnWidth = 290.0f;

    /// <summary>The capture bin's height.</summary>
    public const float CaptureBinHeight = 68.0f;

    private const float Margin = 4.0f;

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

    private static readonly (string Header, TimeControl? Clock)[] TimeControls =
    [
        ("Untimed (play by level)", null),
        ("Standard: 40 moves in 40 minutes", TimeControl.Standard(40, 40)),
        ("Standard: 40 moves in 5 minutes", TimeControl.Standard(40, 5)),
        ("Standard: 40 moves in 1 minute", TimeControl.Standard(40, 1)),
        ("Incremental: 5 minutes + 5 seconds per move", TimeControl.OfMinutes(5, 5)),
    ];

    private static readonly (string Header, Level Level)[] Levels =
    [
        ("Beginner", Level.Beginner),
        ("Easy", Level.Easy),
        ("Medium", Level.Medium),
        ("Hard", Level.Hard),
        ("Best (slow)", Level.Best),
    ];

    private readonly IDesktopPlatform platform;
    private readonly GameController controller;
    private readonly EngineRoster roster;
    private readonly string rosterPath;
    private readonly SynchronizationContext? uiContext = SynchronizationContext.Current;
    private readonly List<(MenuItem Item, TimeControl? Clock)> timeControlItems = [];
    private readonly List<(MenuItem Item, Level Level)> levelItems = [];
    private readonly List<(MenuItem Item, string Path)> rosterItems = [];
    private readonly MenuItem engineMenu = new("Engine");
    private readonly MenuItem builtInEngineItem;
    private readonly MenuItem engineOptionsItem;
    private readonly MenuItem removeEngineItem;
    private readonly MenuItem showEngineOutputItem;
    private readonly List<MenuItem> editMenu;
    private Square editSquare = Square.None;      // Square the edit menu was opened on
    private string? shownTitle;
    private IDesktopWindow? analysisWindow;

    public MainView(IDesktopPlatform platform, GameController controller, EngineRoster roster, string rosterPath)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(roster);
        this.platform = platform;
        this.controller = controller;
        this.roster = roster;
        this.rosterPath = rosterPath;
        builtInEngineItem = new MenuItem("Use built-in engine", () => _ = StartEngineSafelyAsync(null));
        engineOptionsItem = new MenuItem("Engine options (Hash, Threads)...", () => _ = EditEngineOptionsAsync());
        removeEngineItem = new MenuItem("Remove current engine from the list", () => _ = RemoveCurrentEngineAsync());
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

    protected override void Layout()
    {
        MenuBar.SetBounds(SKRect.Create(0, 0, Width, MenuBarView.BarHeight));
        StatusBar.SetBounds(SKRect.Create(0, Height - StatusBarView.BarHeight, Width, StatusBarView.BarHeight));
        float top = MenuBarView.BarHeight + Margin;
        float bottom = Height - StatusBarView.BarHeight - Margin;
        float columnLeft = Width - Margin - ColumnWidth;
        MoveList.SetBounds(new SKRect(columnLeft, top, Width - Margin, bottom - DialogView.ButtonHeight - Margin));
        MoveNowButton.SetBounds(new SKRect(columnLeft, bottom - DialogView.ButtonHeight, Width - Margin, bottom));
        Board.SetBounds(new SKRect(Margin, top, columnLeft - Margin, bottom - CaptureBinHeight - Margin));
        CaptureBin.SetBounds(new SKRect(Margin, bottom - CaptureBinHeight, columnLeft - Margin, bottom));
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
            new MenuItem("White", () => _ = NewGameWithClockAsync(Color.White)) { Shortcut = Keys(Key.N) },
            new MenuItem("Black", () => _ = NewGameWithClockAsync(Color.Black)) { Shortcut = Keys(Key.N, KeyModifiers.Primary | KeyModifiers.Shift) },
        ]);
        var timeControl = new MenuItem("Time control");
        foreach ((string header, TimeControl? clock) in TimeControls)
        {
            var item = new MenuItem(header, () => controller.SetTimeControl(clock));
            timeControlItems.Add((item, clock));
            timeControl.Items.Add(item);
        }
        timeControl.Items.Add(MenuItem.Separator());
        timeControl.Items.Add(new MenuItem("Custom...", () => _ = ChooseCustomTimeControlAsync()));
        var level = new MenuItem("Set level (untimed play)");
        foreach ((string header, Level value) in Levels)
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
        engineMenu.Items.Clear();
        rosterItems.Clear();
        engineMenu.Items.Add(builtInEngineItem);
        foreach (EngineEntry entry in roster.Engines)
        {
            EngineEntry chosen = entry;
            var item = new MenuItem(entry.Name, () => _ = StartEngineSafelyAsync(chosen.Path, chosen.Options));
            rosterItems.Add((item, chosen.Path));
            engineMenu.Items.Add(item);
        }
        engineMenu.Items.AddRange(
        [
            MenuItem.Separator(),
            new MenuItem("Add UCI engine...", () => _ = AddEngineAsync()),
            engineOptionsItem,
            removeEngineItem,
            MenuItem.Separator(),
            showEngineOutputItem,
            new MenuItem("Engine match...", () => _ = StartMatchFromDialogAsync()),
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
            new MenuItem("Clear square", () => controller.EditSquare(editSquare, (Color.None, Piece.None))),
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
        builtInEngineItem.IsChecked = controller.EnginePath is null && !controller.IsMatch;
        foreach ((MenuItem item, string path) in rosterItems)
        {
            item.IsChecked = !controller.IsMatch && controller.EnginePath == path;
        }
        bool isCurrentEngineRegistered = roster.Engines.Any(e => e.Path == controller.EnginePath);
        engineOptionsItem.IsEnabled = isCurrentEngineRegistered;
        removeEngineItem.IsEnabled = isCurrentEngineRegistered;
        foreach ((MenuItem item, TimeControl? clock) in timeControlItems)
        {
            item.IsChecked = Equals(clock, controller.TimeControl);
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
        bool isClockRunning = controller.ActiveClock is not null && !controller.IsGameOver && !controller.IsEditing;
        StatusBar.Segments =
        [
            new StatusSegment("You are playing:", GameText.HumanColor(controller)),
            new StatusSegment("Color to move:", controller.SideToMove.ToString()),
            new StatusSegment(GameText.ModeAnnotation(controller).Trim(), GameText.Mode(controller)),
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
                case Key.N: _ = NewGameWithClockAsync(Color.White); return true;
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
            _ = NewGameWithClockAsync(Color.Black);
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
        }
        controller.TryHumanMove(from, to, promotion);
    }

    // ---------------------------------------------------------------------------------------------
    // Game and view actions
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The clock is chosen before every new game begins; cancelling either dialog keeps the current game.
    /// </summary>
    private async Task NewGameWithClockAsync(Color human)
    {
        var dialog = new NewGameDialog(controller.TimeControl);
        await ShowDialogAsync(dialog);
        if (!dialog.Confirmed)
        {
            return;
        }
        TimeControl? clock = dialog.Choice;
        if (dialog.CustomRequested)
        {
            var custom = new CustomTimeControlDialog(controller.TimeControl);
            await ShowDialogAsync(custom);
            if (custom.Choice is null)
            {
                return;
            }
            clock = custom.Choice;
        }
        controller.SetTimeControl(clock);
        controller.NewGame(human);
    }

    private async Task ChooseCustomTimeControlAsync()
    {
        var dialog = new CustomTimeControlDialog(controller.TimeControl);
        await ShowDialogAsync(dialog);
        if (dialog.Choice is { } clock)
        {
            controller.SetTimeControl(clock);
        }
    }

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
    // The engine roster: registered external UCI engines, persisted per user
    // ---------------------------------------------------------------------------------------------

    private async Task SaveRosterAsync()
    {
        if (EngineRosterFile.Save(roster, rosterPath) is { } failure)
        {
            await ShowMessageAsync("Engines", $"The engine list could not be saved:\n{failure}");
        }
    }

    private async Task<bool> StartEngineSafelyAsync(string? path, IReadOnlyDictionary<string, string>? options = null)
    {
        try
        {
            return await controller.StartEngineAsync(path, options);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ShowMessageAsync("Engine", $"The engine could not be started:\n{ex.Message}");
            return false;
        }
    }

    private async Task AddEngineAsync()
    {
        if (await ChooseFileAsync(() => platform.ChooseFileToOpenAsync("Choose a UCI engine", [FileFilter.All])) is not { } path)
        {
            return;
        }
        if (await StartEngineSafelyAsync(path) && controller.EnginePath == path)
        {
            roster.Add(controller.EngineName ?? Path.GetFileName(path), path);
            await SaveRosterAsync();
            BuildEngineMenu();
            Refresh();
        }
    }

    private async Task EditEngineOptionsAsync()
    {
        if (roster.Engines.FirstOrDefault(e => e.Path == controller.EnginePath) is not { } entry)
        {
            return;
        }
        var dialog = new EngineOptionsDialog(entry.Name, entry.Options);
        await ShowDialogAsync(dialog);
        if (dialog.Saved is not { } options)
        {
            return;
        }
        entry.Options.Clear();
        foreach ((string key, string value) in options)
        {
            entry.Options[key] = value;
        }
        await SaveRosterAsync();
        await StartEngineSafelyAsync(entry.Path, entry.Options);   // Restart so the options apply now
    }

    private async Task RemoveCurrentEngineAsync()
    {
        if (controller.EnginePath is not string path)
        {
            return;
        }
        roster.Remove(path);
        await SaveRosterAsync();
        BuildEngineMenu();
        await StartEngineSafelyAsync(null);
    }

    private async Task StartMatchFromDialogAsync()
    {
        List<string> names = ["Built-in engine", .. roster.Engines.Select(e => e.Name)];
        var dialog = new MatchDialog(names);
        await ShowDialogAsync(dialog);
        if (dialog.Choice is not { } choice)
        {
            return;
        }
        (string? Path, Dictionary<string, string>? Options) Pick(int index) =>
            index == 0 ? (null, null) : (roster.Engines[index - 1].Path, roster.Engines[index - 1].Options);
        (string? whitePath, Dictionary<string, string>? whiteOptions) = Pick(choice.White);
        (string? blackPath, Dictionary<string, string>? blackOptions) = Pick(choice.Black);
        try
        {
            await controller.StartMatchAsync(whitePath, blackPath, whiteOptions, blackOptions);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await ShowMessageAsync("Engine match", $"The match could not be started:\n{ex.Message}");
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
