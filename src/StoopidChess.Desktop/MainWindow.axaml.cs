using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    private static readonly FilePickerFileType PgnFileType = new("PGN") { Patterns = ["*.pgn"] };

    private static readonly (string Header, TimeControl? Clock)[] TimeControls =
    [
        ("Untimed (play by level)", null),
        ("Standard: 40 moves in 40 minutes", TimeControl.Standard(40, 40)),
        ("Standard: 40 moves in 5 minutes", TimeControl.Standard(40, 5)),
        ("Standard: 40 moves in 1 minute", TimeControl.Standard(40, 1)),
        ("Incremental: 5 minutes + 5 seconds per move", TimeControl.OfMinutes(5, 5)),
    ];

    private readonly GameController controller = new();
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly MenuFlyout editMenu;
    private readonly EngineRoster roster;
    private AnalysisWindow? analysisWindow;
    private readonly List<(MenuItem Item, TimeControl? Clock)> timeControlItems = [];
    private readonly List<(MenuItem Item, string Path)> rosterItems = [];
    private readonly MenuItem builtInEngineItem = new() { Header = "Use built-in engine", ToggleType = MenuItemToggleType.CheckBox };
    private readonly MenuItem engineOptionsItem = new() { Header = "Engine options (Hash, Threads)..." };
    private readonly MenuItem removeEngineItem = new() { Header = "Remove current engine from the list" };
    private readonly MenuItem showEngineOutputItem = new() { Header = "Show engine output in move list", ToggleType = MenuItemToggleType.CheckBox };
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
        roster = LoadRoster();
        builtInEngineItem.Click += async (_, _) => await StartEngineSafelyAsync(null);
        engineOptionsItem.Click += async (_, _) => await EditEngineOptionsAsync();
        removeEngineItem.Click += async (_, _) => await RemoveCurrentEngineAsync();
        showEngineOutputItem.Click += (_, _) => UpdateMoveList();
        BuildTimeControlMenu();
        BuildEngineMenu();
        controller.Changed += (_, _) => RefreshAll();
        controller.EngineFailed += (_, message) => _ = MessageDialog.ShowAsync(this, "Engine",
            message + "\n\nChoose an engine from the Engine menu to continue.");
        controller.EngineIllegalMove += (_, move) => _ = MessageDialog.ShowAsync(this, "Engine",
            $"The engine played an illegal move: {move}");
        controller.EngineInfo += (_, line) => Dispatcher.UIThread.Post(() =>
        {
            if (analysisWindow is { } window && FormatInfoLine(line) is { } formatted)
            {
                window.Append(formatted);
            }
        });
        clockTimer.Tick += OnClockTick;
        stopThinkingButton.IsEnabled = false;
        RefreshAll();
        clockTimer.Start();
    }

    // ---------------------------------------------------------------------------------------------
    // The engine roster: registered external UCI engines, persisted per user
    // ---------------------------------------------------------------------------------------------

    private static string RosterPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StoopidChess", "engines.json");

    private static EngineRoster LoadRoster()
    {
        try
        {
            using FileStream stream = File.OpenRead(RosterPath);
            return EngineRoster.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException)
        {
            return new EngineRoster();
        }
    }

    private async Task SaveRosterAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RosterPath)!);
            using FileStream stream = File.Create(RosterPath);
            roster.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Engines", $"The engine list could not be saved:\n{ex.Message}");
        }
    }

    private void BuildTimeControlMenu()
    {
        timeControlItems.Clear();
        timeControlMenuItem.Items.Clear();
        foreach ((string header, TimeControl? clock) in TimeControls)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox };
            item.Click += (_, _) => controller.SetTimeControl(clock);
            timeControlItems.Add((item, clock));
            timeControlMenuItem.Items.Add(item);
        }
        timeControlMenuItem.Items.Add(new Separator());
        timeControlMenuItem.Items.Add(EditMenuItem("Custom...", () => _ = ChooseCustomTimeControlAsync()));
    }

    private async Task ChooseCustomTimeControlAsync()
    {
        var dialog = new CustomTimeControlDialog(controller.TimeControl);
        await dialog.ShowDialog(this);
        if (dialog.Choice is { } clock)
        {
            controller.SetTimeControl(clock);
        }
    }

    private void BuildEngineMenu()
    {
        engineMenuItem.Items.Clear();
        rosterItems.Clear();
        engineMenuItem.Items.Add(builtInEngineItem);
        foreach (EngineEntry entry in roster.Engines)
        {
            var item = new MenuItem { Header = entry.Name, ToggleType = MenuItemToggleType.CheckBox };
            EngineEntry chosen = entry;
            item.Click += async (_, _) => await StartEngineSafelyAsync(chosen.Path, chosen.Options);
            rosterItems.Add((item, chosen.Path));
            engineMenuItem.Items.Add(item);
        }
        engineMenuItem.Items.Add(new Separator());
        engineMenuItem.Items.Add(EditMenuItem("Add UCI engine...", () => _ = AddEngineAsync()));
        engineMenuItem.Items.Add(engineOptionsItem);
        engineMenuItem.Items.Add(removeEngineItem);
        engineMenuItem.Items.Add(new Separator());
        engineMenuItem.Items.Add(showEngineOutputItem);
        engineMenuItem.Items.Add(EditMenuItem("Engine match...", () => _ = StartMatchFromDialogAsync()));
    }

    private async Task EditEngineOptionsAsync()
    {
        if (roster.Engines.FirstOrDefault(e => e.Path == controller.EnginePath) is not { } entry)
        {
            return;
        }
        var dialog = new EngineOptionsDialog(entry.Name, entry.Options);
        await dialog.ShowDialog(this);
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

    private async Task StartMatchFromDialogAsync()
    {
        List<string> names = ["Built-in engine", .. roster.Engines.Select(e => e.Name)];
        var dialog = new MatchDialog(names);
        await dialog.ShowDialog(this);
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
            await MessageDialog.ShowAsync(this, "Engine match", $"The match could not be started:\n{ex.Message}");
        }
    }

    private async Task AddEngineAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a UCI engine",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.All],
        });
        if (files.Count != 1 || files[0].TryGetLocalPath() is not string path)
        {
            return;
        }
        if (await StartEngineSafelyAsync(path) && controller.EnginePath == path)
        {
            roster.Add(controller.EngineName ?? Path.GetFileName(path), path);
            await SaveRosterAsync();
            BuildEngineMenu();
            RefreshAll();
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
            await MessageDialog.ShowAsync(this, "Engine", $"The engine could not be started:\n{ex.Message}");
            return false;
        }
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
        Title = controller.IsMatch
            ? $"Stoopid Chess - {controller.EngineName} vs {controller.OpponentName}"
            : controller.EngineName is null ? "Stoopid Chess" : $"Stoopid Chess - {controller.EngineName}";
        stopThinkingButton.IsEnabled = controller.IsThinking;
        UpdateMoveList();
        colorToMoveLabel.Text = controller.SideToMove.ToString();
        humanColorLabel.Text = controller.HumanColor == Color.None ? "Nobody (engine vs engine)" : controller.HumanColor.ToString();
        // The clock the running game is actually under; a differing selection is marked as pending
        modeAnnotationLabel.Text = controller.ActiveClock is null ? "   Difficulty:" : "   Clock:";
        string mode = controller.ActiveClock?.ToString() ?? controller.Level.ToString();
        if (!Equals(controller.TimeControl, controller.ActiveClock))
        {
            mode += $" (next game: {controller.TimeControl?.ToString() ?? "untimed"})";
        }
        difficultyLabel.Text = mode;
        UpdateClockLabels();
        builtInEngineItem.IsChecked = controller.EnginePath is null && !controller.IsMatch;
        foreach ((MenuItem item, string path) in rosterItems)
        {
            item.IsChecked = !controller.IsMatch && controller.EnginePath == path;
        }
        bool currentEngineRegistered = roster.Engines.Any(e => e.Path == controller.EnginePath);
        engineOptionsItem.IsEnabled = currentEngineRegistered;
        removeEngineItem.IsEnabled = currentEngineRegistered;
        foreach ((MenuItem item, TimeControl? clock) in timeControlItems)
        {
            item.IsChecked = Equals(clock, controller.TimeControl);
        }
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
        var replay = Core.Position.FromFen(game.StartFen);
        foreach (Move move in game.Moves)
        {
            if (mover == Color.White)
            {
                text.Append(CultureInfo.InvariantCulture, $"{moveNumber,2}. ");
                lineOpen = true;
            }
            text.Append(CultureInfo.InvariantCulture, $"{San.Format(replay, move),-9}");
            replay.MakeMove(move);
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
        if (controller.TimedOut != Color.None)
        {
            text.Append(CultureInfo.InvariantCulture, $"GAME OVER: {controller.TimedOut} lost on time");
        }
        if (showEngineOutputItem.IsChecked && controller.LastEngineInfo.Length != 0)
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
    private async void OnNewGameWhite(object? sender, RoutedEventArgs e) => await NewGameWithClockAsync(Color.White);
    private async void OnNewGameBlack(object? sender, RoutedEventArgs e) => await NewGameWithClockAsync(Color.Black);
    private void OnUndoMove(object? sender, RoutedEventArgs e) => controller.Undo();

    /// <summary>
    /// The clock is chosen before every new game begins; cancelling either dialog keeps the
    /// current game.
    /// </summary>
    private async Task NewGameWithClockAsync(Color human)
    {
        var dialog = new NewGameDialog(controller.TimeControl);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed)
        {
            return;
        }
        TimeControl? clock = dialog.Choice;
        if (dialog.CustomRequested)
        {
            var custom = new CustomTimeControlDialog(controller.TimeControl);
            await custom.ShowDialog(this);
            if (custom.Choice is null)
            {
                return;
            }
            clock = custom.Choice;
        }
        controller.SetTimeControl(clock);
        controller.NewGame(human);
    }
    private void OnSwitchSides(object? sender, RoutedEventArgs e) => controller.SwitchSides();
    private void OnMoveNow(object? sender, RoutedEventArgs e) => controller.MoveNow();

    private void OnFlipBoard(object? sender, RoutedEventArgs e)
    {
        controller.IsBoardFlipped = !controller.IsBoardFlipped;
        board.InvalidateVisual();
    }

    private void OnToggleAnalysis(object? sender, RoutedEventArgs e)
    {
        if (analysisWindow is { } open)
        {
            open.Activate();
            return;
        }
        analysisWindow = new AnalysisWindow();
        analysisWindow.Closed += (_, _) => analysisWindow = null;
        analysisWindow.Show(this);
    }

    /// <summary>
    /// One display line per engine "info" report that carries a full report: depth, score, node
    /// count and the principal variation.
    /// </summary>
    private static string? FormatInfoLine(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? depth = null;
        string? score = null;
        string? nodes = null;
        int pvStart = -1;
        for (int i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "depth" when i + 1 < tokens.Length:
                    depth = tokens[i + 1];
                    break;
                case "score" when i + 2 < tokens.Length && tokens[i + 1] == "cp" && int.TryParse(tokens[i + 2], out int cp):
                    score = (cp / 100.0).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
                    break;
                case "score" when i + 2 < tokens.Length && tokens[i + 1] == "mate":
                    score = "#" + tokens[i + 2];
                    break;
                case "nodes" when i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out long count):
                    nodes = count.ToString("N0", CultureInfo.InvariantCulture);
                    break;
                case "pv":
                    pvStart = i + 1;
                    break;
            }
            if (pvStart >= 0)
            {
                break;
            }
        }
        if (depth is null || score is null || pvStart < 0)
        {
            return null;
        }
        string pv = string.Join(' ', tokens.Skip(pvStart).Take(12));
        return $"d{depth,3}  {score,8}  {nodes,14} n   {pv}";
    }

    private async void OnCopyFen(object? sender, RoutedEventArgs e)
    {
        if (GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(controller.CurrentFen);
        }
    }

    private async void OnPasteFen(object? sender, RoutedEventArgs e)
    {
        if (GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }
        string? text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        if (Fen.TryParse(text.Trim()) is not { } position)
        {
            await MessageDialog.ShowAsync(this, "Paste FEN", "The clipboard does not hold a valid FEN position.");
            return;
        }
        controller.SetPosition(position);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Window-wide copy and paste of the position, unless a text box owns the keys
        if (e.KeyModifiers == KeyModifiers.Control && FocusManager?.GetFocusedElement() is not TextBox)
        {
            if (e.Key == Key.C)
            {
                OnCopyFen(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                OnPasteFen(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        base.OnKeyDown(e);
    }

    private async void OnImportPgn(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import PGN",
            AllowMultiple = false,
            FileTypeFilter = [PgnFileType, FilePickerFileTypes.All],
        });
        if (files.Count != 1)
        {
            return;
        }
        string text;
        try
        {
            Stream stream = await files[0].OpenReadAsync();
            await using (stream.ConfigureAwait(true))
            {
                using var readText = new StreamReader(stream);
                text = await readText.ReadToEndAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Import PGN", $"Unable to read the file:\n{ex.Message}");
            return;
        }
        if (Pgn.Parse(text) is not { } game)
        {
            await MessageDialog.ShowAsync(this, "Import PGN", "The file does not hold a readable PGN game.");
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

    private async void OnExportPgn(object? sender, RoutedEventArgs e)
    {
        if (controller.IsEditing)
        {
            await MessageDialog.ShowAsync(this, "Export PGN", "Finish editing the position before exporting.");
            return;
        }
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PGN",
            DefaultExtension = "pgn",
            SuggestedFileName = "game.pgn",
            FileTypeChoices = [PgnFileType],
        });
        if (file is null)
        {
            return;
        }
        (string white, string black) = PlayerNames();
        string pgn = Pgn.Write(controller.Game, white, black, ResultText(), DateOnly.FromDateTime(DateTime.Now));
        try
        {
            Stream stream = await file.OpenWriteAsync();
            await using (stream.ConfigureAwait(true))
            {
                using var writeText = new StreamWriter(stream);
                await writeText.WriteAsync(pgn);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(this, "Export PGN", $"Unable to save the file:\n{ex.Message}");
        }
    }

    private (string White, string Black) PlayerNames()
    {
        string engine = controller.EngineName ?? "Engine";
        if (controller.IsMatch)
        {
            return (engine, controller.OpponentName ?? "Engine");
        }
        return controller.HumanColor switch
        {
            Color.White => ("Human", engine),
            Color.Black => (engine, "Human"),
            _ => (engine, engine),
        };
    }

    private string ResultText()
    {
        if (controller.TimedOut != Color.None)
        {
            return controller.TimedOut == Color.White ? "0-1" : "1-0";
        }
        return controller.Game.Status switch
        {
            GameStatus.Checkmate => controller.Game.SideToMove == Color.White ? "0-1" : "1-0",
            GameStatus.Stalemate or GameStatus.DrawByRepetition or GameStatus.DrawByFiftyMoveRule
                or GameStatus.DrawByInsufficientMaterial => "1/2-1/2",
            _ => "*",
        };
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
