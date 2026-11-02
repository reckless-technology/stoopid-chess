using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Desktop.Dialogs;
using StoopidChess.Desktop.Views;
using StoopidChess.Engine;
using StoopidChess.Engine.Uci;
using StoopidChess.Play;
using StoopidChess.Tests.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The main view's wiring, driven as a person drives the window - its menus, its shortcuts, the board, the dialogs -
/// against a fake window system (<see cref="FakePlatform"/>) and scripted engines (<see cref="ScriptedEngine"/>)
/// standing in for external executables. The tests that start an engine run on a <see cref="ControllerThread"/>, so
/// its replies are handled between their steps.
/// </summary>
public sealed class MainViewTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string folder = Directory.CreateTempSubdirectory("stoopid-chess-gui-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// A main view in a window, its platform faked and its external engines scripted by path.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        private readonly Dictionary<string, Func<string, IEnumerable<string>?>> scripts = [];

        public Rig(string folder, SavedGame? game = null, EngineRoster? roster = null)
        {
            Controller = new GameController
            {
                LimitsFor = _ => SearchLimits.ForDepth(1),
                StartExternalEngine = path => scripts.TryGetValue(path, out Func<string, IEnumerable<string>?>? script)
                    ? ScriptedEngine.Start(Path.GetFileName(path), script)
                    : throw new InvalidOperationException($"could not start '{path}'"),
            };
            if (game is not null)
            {
                Assert.True(Controller.Load(game));
            }
            Roster = roster ?? new EngineRoster();
            RosterPath = Path.Combine(folder, "roster", "engines.json");
            View = new MainView(Platform, Controller, Roster, RosterPath);
            Window = new ViewHarness(View);
        }

        public FakePlatform Platform { get; } = new();

        public GameController Controller { get; }

        public EngineRoster Roster { get; }

        public string RosterPath { get; }

        public MainView View { get; }

        public ViewHarness Window { get; }

        public string Register(string path, Func<string, IEnumerable<string>?> script)
        {
            scripts[path] = script;
            return path;
        }

        /// <summary>The dialog on top; fails when there is none.</summary>
        public T Dialog<T>() where T : DialogView => Assert.IsType<T>(Window.Root.Overlays[^1]);

        /// <summary>The message on top: its title and text, dismissed.</summary>
        public (string Title, string Text) TakeMessage()
        {
            MessageDialog message = Dialog<MessageDialog>();
            (string, string) said = (message.Title, message.Message.Text);
            Window.Click(message.Buttons[0]);
            return said;
        }

        /// <summary>
        /// Choose a menu row as a person would: the title pressed, the pointer resting on each submenu's row, the
        /// last row clicked.
        /// </summary>
        public void Choose(params string[] path)
        {
            int title = View.MenuBar.Menus.FindIndex(menu => menu.Text == path[0]);
            SKRect titleBounds = View.MenuBar.TitleBounds(title);
            Window.Click(View.MenuBar.ToRoot(new SKPoint(titleBounds.MidX, titleBounds.MidY)));
            ChooseIn(View.MenuBar.Popup!, path[1..]);
        }

        /// <summary>Choose a row of an open popup menu, through its submenus.</summary>
        public void ChooseIn(MenuPopup popup, params string[] path)
        {
            for (int level = 0; level < path.Length; level++)
            {
                int row = popup.ItemsOf(level).ToList().FindIndex(item => item.Text == path[level]);
                Assert.True(row >= 0, $"no row {path[level]}");
                if (level < path.Length - 1)
                {
                    Window.Move(popup.RowCentre(level, row));
                }
                else
                {
                    Window.Click(popup.RowCentre(level, row));
                }
            }
        }

        public SKPoint Square(string name) => View.Board.ToRoot(View.Board.SquareCentre(StoopidChess.Core.Square.Parse(name)));

        public async Task SettleAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, $"the condition never held: engine {Controller.EnginePath ?? "built-in"} "
                    + $"({Controller.EngineName}, {(Controller.HasEngine ? "running" : "none")}), overlays "
                    + string.Join(", ", Window.Root.Overlays.Select(overlay => overlay is MessageDialog message ? message.Message.Text : overlay.GetType().Name)));
                await Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
                await Task.Delay(5);
            }
        }

        public void Dispose()
        {
            View.Dispose();
            Window.Dispose();
        }
    }

    [Fact]
    public void TheWindowIsLaidOutAsBefore()
    {
        using var rig = new Rig(folder);
        MainView view = rig.View;
        Assert.Equal(["File", "Edit", "Game", "View", "Engine", "Help"], view.MenuBar.Menus.Select(menu => menu.Text));
        Assert.Equal(MenuBarView.BarHeight, view.MenuBar.Height);
        Assert.Equal(MainView.ColumnWidth, view.MoveList.Width);
        Assert.Equal(view.MoveList.Left(), view.MoveNowButton.Left());
        Assert.True(view.MoveNowButton.Bounds.Top > view.MoveList.Bounds.Bottom);
        Assert.True(view.Board.Bounds.Right < view.MoveList.Bounds.Left);
        Assert.Equal(MainView.CaptureBinHeight, view.CaptureBin.Height);
        Assert.True(view.CaptureBin.Bounds.Top > view.Board.Bounds.Bottom);
        Assert.Equal(660 - StatusBarView.BarHeight, view.StatusBar.Bounds.Top);
        Assert.False(view.MoveNowButton.IsEnabled);
        Assert.Equal("Stoopid Chess", rig.Platform.Title);
        Assert.Equal(["You are playing:", "Color to move:", "Difficulty:", "White clock:", "Black clock:"],
            view.StatusBar.Segments.Select(segment => segment.Caption));
        Assert.Equal(new SKSizeI(860, 660), MainView.DefaultSize);
        Assert.Equal(new SKSizeI(680, 540), MainView.MinimumSize);
        Assert.Equal("Ctrl+N".Replace("Ctrl", Shortcut.PrimaryName, StringComparison.Ordinal),
            view.MenuBar.Menus[2].Items[0].Items[0].Shortcut);
        rig.Window.Render();
        Assert.Throws<ArgumentNullException>(() => new MainView(null!, rig.Controller, rig.Roster, rig.RosterPath));
        Assert.Throws<ArgumentNullException>(() => new MainView(rig.Platform, null!, rig.Roster, rig.RosterPath));
        Assert.Throws<ArgumentNullException>(() => new MainView(rig.Platform, rig.Controller, null!, rig.RosterPath));
    }

    [Fact]
    public void ADraggedMoveIsPlayedAndAnIllegalOneRefused()
    {
        using var rig = new Rig(folder);
        rig.Window.Drag(rig.Square("e2"), rig.Square("e5"));
        Assert.Equal(("Illegal move", "That move is not permitted."), rig.TakeMessage());
        rig.Window.Drag(rig.Square("e2"), rig.Square("e4"));
        Assert.Equal(Square.Parse("e4"), rig.Controller.Game.Position.LastMove!.Value.To);
        Assert.Contains("e4", rig.View.MoveList.Lines[0], StringComparison.Ordinal);
        rig.Window.Drag(rig.Square("e7"), rig.Square("e5"));      // Not the human's piece: nothing
        Assert.Empty(rig.Window.Root.Overlays);
    }

    [Fact]
    public void APromotionAsksForThePiece()
    {
        using var rig = new Rig(folder, new SavedGame { StartFen = "8/4P3/8/8/8/8/k7/4K3 w - - 0 1" });
        rig.Window.Drag(rig.Square("e7"), rig.Square("e8"));
        PromotionDialog dialog = rig.Dialog<PromotionDialog>();
        Assert.Equal(Piece.Queen, dialog.Choice);
        rig.Window.Click(dialog.Pieces[3]);
        Assert.Equal((Color.White, Piece.Knight), rig.Controller.PieceAt(Square.Parse("e8")));
    }

    [Fact]
    public void TheEditMenuEditsThePosition()
    {
        using var rig = new Rig(folder);
        rig.Window.Click(rig.Square("e4"), PointerButton.Right);
        MenuPopup menu = Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single());
        rig.ChooseIn(menu, "Black", "Queen");
        Assert.Equal((Color.Black, Piece.Queen), rig.Controller.PieceAt(Square.Parse("e4")));
        rig.Window.Click(rig.Square("a1"), PointerButton.Right);
        rig.ChooseIn(Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single()), "Clear entire position");
        Assert.True(rig.Controller.IsEditing);
        Assert.Equal("Position edited by user: place both kings to continue", rig.View.MoveList.Lines.Single());
        rig.Window.Click(rig.Square("e2"));                       // Unfinished: said so
        Assert.Equal("Editing position", rig.TakeMessage().Title);
        rig.Window.Click(rig.Square("e1"), PointerButton.Right);
        rig.ChooseIn(Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single()), "White", "King");
        rig.Window.Click(rig.Square("e8"), PointerButton.Right);
        rig.ChooseIn(Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single()), "Black", "King");
        rig.Window.Click(rig.Square("e8"), PointerButton.Right);
        rig.ChooseIn(Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single()), "Black to move");
        Assert.False(rig.Controller.IsEditing);
        Assert.Equal(Color.Black, rig.Controller.SideToMove);
        rig.Window.Click(rig.Square("e8"), PointerButton.Right);
        rig.ChooseIn(Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single()), "White to move");
        rig.Window.Click(rig.Square("d4"), PointerButton.Right);
        rig.ChooseIn(Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single()), "Clear square");
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", rig.Controller.CurrentFen);
    }

    [Fact]
    public void TheShortcutsReachTheirActions()
    {
        using var rig = new Rig(folder);
        rig.Window.Key(Key.C, KeyModifiers.Primary);
        Assert.Equal(Position.StartFen, rig.Platform.Clipboard);
        rig.Platform.Clipboard = "4k3/8/8/8/8/8/8/4K3 w - - 0 1";
        rig.Window.Key(Key.V, KeyModifiers.Primary);
        Assert.Equal(rig.Platform.Clipboard, rig.Controller.CurrentFen);
        rig.Platform.Clipboard = "not a position";
        rig.Window.Key(Key.V, KeyModifiers.Primary);
        Assert.Equal(("Paste FEN", "The clipboard does not hold a valid FEN position."), rig.TakeMessage());
        rig.Platform.Clipboard = "  ";
        rig.Window.Key(Key.V, KeyModifiers.Primary);
        Assert.Empty(rig.Window.Root.Overlays);

        rig.Window.Key(Key.F, KeyModifiers.Primary);
        Assert.True(rig.Controller.IsBoardFlipped);
        rig.Window.Key(Key.Q, KeyModifiers.Primary);
        Assert.True(rig.Platform.HasQuit);
        rig.Window.Key(Key.G, KeyModifiers.Alt);
        Assert.Equal(2, rig.View.MenuBar.OpenMenu);
        rig.Window.Key(Key.Escape);
        rig.Window.Key(Key.X, KeyModifiers.Alt);                  // No such menu
        rig.Window.Key(Key.B, KeyModifiers.Primary);              // No such shortcut
        rig.Window.Key(Key.Return);
        Assert.Empty(rig.Window.Root.Overlays);
    }

    [Fact]
    public void ANewGameAsksForItsClockFirst()
    {
        using var rig = new Rig(folder, new SavedGame { Moves = ["e2e4"] });
        rig.Window.Key(Key.N, KeyModifiers.Primary);
        NewGameDialog dialog = rig.Dialog<NewGameDialog>();
        rig.Window.Click(dialog.Buttons[1]);                      // Cancel: the game goes on
        Assert.Single(rig.Controller.Game.Moves);

        rig.Window.Key(Key.N, KeyModifiers.Primary | KeyModifiers.Shift);
        rig.Window.Key(Key.Return);                               // The default clock, 40 moves in 5 minutes
        Assert.Empty(rig.Controller.Game.Moves);
        Assert.Equal(Color.Black, rig.Controller.HumanColor);
        Assert.Equal(TimeControl.Standard(40, 5), rig.Controller.TimeControl);
        Assert.Equal("Clock:", rig.View.StatusBar.Segments[2].Caption);

        rig.Choose("Game", "New Game", "White");
        dialog = rig.Dialog<NewGameDialog>();
        rig.Window.Click(dialog.Choices.ToRoot(dialog.Choices.RowCentre(4)));   // Custom...
        rig.Window.Click(dialog.Buttons[0]);
        CustomTimeControlDialog custom = rig.Dialog<CustomTimeControlDialog>();
        custom.Minutes.Value = 15;
        custom.Increment.Value = 10;
        custom.MovesPerSession.Value = 0;
        rig.Window.Key(Key.Return);
        Assert.Equal(TimeControl.OfMinutes(15, 10), rig.Controller.TimeControl);
        Assert.Equal(Color.White, rig.Controller.HumanColor);

        rig.Window.Key(Key.N, KeyModifiers.Primary);
        rig.Window.Key(Key.Return);                               // Custom again, from the current clock
        rig.Window.Key(Key.Escape);                               // Cancelled: no new game
        Assert.Empty(rig.Window.Root.Overlays);
        Assert.Equal(TimeControl.OfMinutes(15, 10), rig.Controller.TimeControl);
    }

    [Fact]
    public void TheGameMenuSetsClocksLevelsAndSides()
    {
        using var rig = new Rig(folder, new SavedGame { Moves = ["e2e4"] });
        rig.Choose("Game", "Time control", "Incremental: 5 minutes + 5 seconds per move");
        Assert.Equal(TimeControl.OfMinutes(5, 5), rig.Controller.TimeControl);
        MenuItem timeControl = rig.View.MenuBar.Menus[2].Items[3];
        Assert.True(timeControl.Items[4].IsChecked);
        Assert.False(timeControl.Items[0].IsChecked);
        rig.Choose("Game", "Time control", "Custom...");
        CustomTimeControlDialog custom = rig.Dialog<CustomTimeControlDialog>();
        Assert.Equal(5, custom.Minutes.Value);
        Assert.Equal(5, custom.Increment.Value);
        rig.Window.Click(custom.Buttons[1]);                      // Cancel
        Assert.Equal(TimeControl.OfMinutes(5, 5), rig.Controller.TimeControl);
        rig.Choose("Game", "Time control", "Custom...");
        rig.Dialog<CustomTimeControlDialog>().MovesPerSession.Value = 40;
        rig.Window.Key(Key.Return);
        Assert.Equal(new TimeControl(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5), 40), rig.Controller.TimeControl);

        rig.Choose("Game", "Set level (untimed play)", "Hard");
        Assert.Equal(Level.Hard, rig.Controller.Level);
        Assert.True(rig.View.MenuBar.Menus[2].Items[4].Items[3].IsChecked);
        rig.Choose("Game", "Undo last move");
        Assert.Empty(rig.Controller.Game.Moves);
        rig.Choose("Game", "Switch sides");
        Assert.Equal(Color.Black, rig.Controller.HumanColor);
        Assert.True(rig.Controller.IsBoardFlipped);               // Switching to Black turns the board round
        rig.Choose("View", "Flip position");
        Assert.False(rig.Controller.IsBoardFlipped);
        rig.Choose("Help");
        (string title, string text) = rig.TakeMessage();
        Assert.StartsWith("Stoopid Chess ", title, StringComparison.Ordinal);
        Assert.Contains("drag it to the target square", text, StringComparison.Ordinal);
        rig.Choose("File", "Exit");
        Assert.True(rig.Platform.HasQuit);
    }

    [Fact]
    public void TheClockTicksInTheStatusBar()
    {
        using var rig = new Rig(folder);
        rig.Controller.SetTimeControl(TimeControl.Standard(40, 5));
        rig.Controller.NewGame(Color.White);
        rig.View.Tick(TimeSpan.FromSeconds(2));
        StatusSegment white = rig.View.StatusBar.Segments[3];
        Assert.Equal("00:04:58", white.Value);
        Assert.True(white.IsRunning);
        Assert.False(rig.View.StatusBar.Segments[4].IsRunning);
        rig.Window.Render();
    }

    [Fact]
    public async Task TheAnalysisWindowOpensOnceAndShowsTheEnginesLines() => await ControllerThread.Run(async () =>
    {
        using var rig = new Rig(folder);
        string engine = rig.Register("analyst", command => command.StartsWith("go", StringComparison.Ordinal)
            ? ["info depth 1 score cp 12 nodes 20 pv e7e5", "info string not shown", "bestmove e7e5"]
            : []);
        rig.Choose("View", "Analysis window");
        FakePlatform.FakeWindow window = Assert.Single(rig.Platform.Windows);
        Assert.Equal("Analysis", window.Title);
        Assert.Same(rig.View.Analysis, window.Content.Content);
        rig.Window.Key(Key.A, KeyModifiers.Primary);
        Assert.Equal(1, window.Activations);
        Assert.Single(rig.Platform.Windows);

        Assert.True(await rig.Controller.StartEngineAsync(engine));
        rig.Window.Drag(rig.Square("e2"), rig.Square("e4"));
        await rig.SettleAsync(() => rig.Controller.Game.Moves.Count == 2 && rig.View.Analysis!.Output.Lines.Count == 1);
        Assert.StartsWith("d  1", rig.View.Analysis!.Output.Lines[0], StringComparison.Ordinal);

        rig.Choose("Engine", "Show engine output in move list");
        Assert.Contains(rig.View.MoveList.Lines, line => line.Contains("info depth 1", StringComparison.Ordinal));
        window.Close();
        Assert.Null(rig.View.Analysis);
        rig.Window.Key(Key.A, KeyModifiers.Primary);
        Assert.Equal(2, rig.Platform.Windows.Count);
        rig.View.Dispose();                                       // Closes it
        Assert.True(rig.Platform.Windows[1].IsClosed);
    });

    [Fact]
    public async Task TheEngineIsStartedAndItsFailuresAreSaid() => await ControllerThread.Run(async () =>
    {
        using var rig = new Rig(folder);
        await rig.View.StartAsync();
        Assert.StartsWith("Stoopid Chess - Stoopid Chess ", rig.Platform.Title, StringComparison.Ordinal);
        Assert.True(rig.View.EngineMenuItems[0].IsChecked);
        Assert.False(rig.View.EngineMenuItems.Single(item => item.Text.StartsWith("Engine options", StringComparison.Ordinal)).IsEnabled);

        string illegal = rig.Register("illegal", ScriptedEngine.Plays("a1a8"));
        Assert.True(await rig.Controller.StartEngineAsync(illegal));
        rig.Window.Drag(rig.Square("e2"), rig.Square("e4"));
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        Assert.Equal(("Engine", "The engine played an illegal move: a1a8"), rig.TakeMessage());

        rig.Controller.NewGame(Color.White);                      // The person's move, so the next engine is asked nothing
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        string dies = rig.Register("dies", command => command.StartsWith("go", StringComparison.Ordinal) ? null : []);
        Assert.True(await rig.Controller.StartEngineAsync(dies));
        rig.Controller.NewGame(Color.Black);
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        (string title, string text) = rig.TakeMessage();
        Assert.Equal("Engine", title);
        Assert.EndsWith("Choose an engine from the Engine menu to continue.", text, StringComparison.Ordinal);

        rig.Choose("Engine", "Use built-in engine");
        await rig.SettleAsync(() => rig.Controller.EngineName is not null && rig.Controller.EnginePath is null);
    });

    [Fact]
    public async Task EnginesAreAddedConfiguredAndRemoved() => await ControllerThread.Run(async () =>
    {
        using var rig = new Rig(folder);
        string path = rig.Register(Path.Combine(folder, "pawnstar"), ScriptedEngine.Plays("e7e5"));
        rig.Platform.FileAnswers.Enqueue(path);
        rig.Choose("Engine", "Add UCI engine...");
        await rig.SettleAsync(() => rig.Roster.Engines.Count == 1);
        Assert.Equal("pawnstar", rig.Roster.Engines[0].Name);
        Assert.True(File.Exists(rig.RosterPath));
        MenuItem added = Assert.Single(rig.View.EngineMenuItems, item => item.Text == "pawnstar");
        Assert.True(added.IsChecked);

        rig.Choose("Engine", "Engine options (Hash, Threads)...");
        EngineOptionsDialog options = rig.Dialog<EngineOptionsDialog>();
        Assert.Equal("pawnstar options", options.Title);
        options.Hash.Value = 128;
        rig.Window.Key(Key.Return);
        await rig.SettleAsync(() => rig.Controller.EnginePath == path && rig.Controller.HasEngine);
        Assert.Equal("128", rig.Roster.Engines[0].Options["Hash"]);
        rig.Choose("Engine", "Engine options (Hash, Threads)...");
        rig.Window.Key(Key.Escape);                               // Cancelled: nothing changes
        Assert.Equal("128", rig.Roster.Engines[0].Options["Hash"]);

        rig.Choose("Engine", "Use built-in engine");
        await rig.SettleAsync(() => rig.Controller.EnginePath is null && rig.Controller.HasEngine);
        rig.Choose("Engine", "pawnstar");
        await rig.SettleAsync(() => rig.Controller.EnginePath == path && rig.Controller.HasEngine);
        rig.Choose("Engine", "Remove current engine from the list");
        await rig.SettleAsync(() => rig.Roster.Engines.Count == 0 && rig.Controller.EnginePath is null && rig.Controller.HasEngine);
        Assert.DoesNotContain(rig.View.EngineMenuItems, item => item.Text == "pawnstar");
        rig.Choose("Engine", "Remove current engine from the list");   // Disabled for the built-in: nothing
        Assert.NotNull(rig.View.MenuBar.Popup);                    // And the menu stays open
        rig.Window.Key(Key.Escape);

        rig.Platform.FileAnswers.Enqueue(Path.Combine(folder, "missing"));
        rig.Choose("Engine", "Add UCI engine...");
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        Assert.StartsWith("The engine could not be started:", rig.TakeMessage().Text, StringComparison.Ordinal);
        rig.Choose("Engine", "Add UCI engine...");                // Cancelled
        Assert.Empty(rig.Roster.Engines);
    });

    [Fact]
    public async Task AnUnsavableEngineListIsSaid() => await ControllerThread.Run(async () =>
    {
        using var rig = new Rig(folder);
        File.WriteAllText(Path.Combine(folder, "roster"), "a file where the folder should be");
        string path = rig.Register("engine", ScriptedEngine.Plays("e7e5"));
        rig.Platform.FileAnswers.Enqueue(path);
        rig.Choose("Engine", "Add UCI engine...");
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        (string title, string text) = rig.TakeMessage();
        Assert.Equal("Engines", title);
        Assert.StartsWith("The engine list could not be saved:", text, StringComparison.Ordinal);
    });

    [Fact]
    public async Task AnEngineMatchIsChosenAndStarted() => await ControllerThread.Run(async () =>
    {
        var roster = new EngineRoster();
        roster.Add("white engine", "white");
        using var rig = new Rig(folder, roster: roster);
        rig.Register("white", ScriptedEngine.Plays("e2e4"));
        rig.Choose("Engine", "Engine match...");
        MatchDialog dialog = rig.Dialog<MatchDialog>();
        Assert.Equal(["Built-in engine", "white engine"], dialog.White.Choices);
        dialog.White.SelectedIndex = 1;
        dialog.Black.SelectedIndex = 0;
        rig.Window.Click(dialog.Buttons[0]);
        await rig.SettleAsync(() => rig.Controller.IsMatch && rig.Controller.Game.Moves.Count > 0);
        Assert.StartsWith("Stoopid Chess - white vs Stoopid Chess ", rig.Platform.Title, StringComparison.Ordinal);
        Assert.False(rig.View.EngineMenuItems[0].IsChecked);
        rig.Controller.NewGame(Color.White);                      // A human game ends the match
        await rig.Controller.WaitUntilIdleAsync().WaitAsync(Timeout);
        while (rig.Window.Root.Overlays.Count > 0)
        {
            rig.TakeMessage();                                    // The scripted engine's second e2e4 was illegal
        }

        rig.Choose("Engine", "Engine match...");
        rig.Window.Key(Key.Escape);                               // Cancelled
        roster.Add("broken", "broken");
        rig.Choose("Engine", "Engine match...");
        dialog = rig.Dialog<MatchDialog>();
        dialog.White.SelectedIndex = 2;
        rig.Window.Click(dialog.Buttons[0]);
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        Assert.Equal("Engine", rig.TakeMessage().Title);         // The controller says the engine would not start
    });

    [Fact]
    public async Task GamesAreSavedAndLoaded() => await ControllerThread.Run(async () =>
    {
        using var rig = new Rig(folder, new SavedGame { Moves = ["e2e4", "e7e5"] });
        string saved = Path.Combine(folder, "game");
        rig.Platform.FileAnswers.Enqueue(saved);
        rig.Window.Key(Key.S, KeyModifiers.Primary);
        Assert.True(File.Exists(saved + ".chess"));               // The extension added
        Assert.Equal("Save game", rig.Platform.FileDialogs[^1].Title);
        Assert.Equal("chess", rig.Platform.FileDialogs[^1].Filters[0].Extensions);

        rig.Controller.NewGame(Color.White);
        rig.Platform.FileAnswers.Enqueue(saved + ".chess");
        rig.Window.Key(Key.O, KeyModifiers.Primary);
        Assert.Equal(2, rig.Controller.Game.Moves.Count);

        string junk = Path.Combine(folder, "junk.chess");
        await File.WriteAllTextAsync(junk, "not a game");
        rig.Platform.FileAnswers.Enqueue(junk);
        rig.Choose("File", "Load game");
        Assert.Equal(("Load game", "This file does not contain a game in Stoopid Chess format."), rig.TakeMessage());
        rig.Platform.FileAnswers.Enqueue(folder);                 // A folder: unreadable
        rig.Choose("File", "Load game");
        Assert.StartsWith("Unable to load the game:", rig.TakeMessage().Text, StringComparison.Ordinal);
        rig.Platform.FileAnswers.Enqueue(Path.Combine(folder, "no", "such", "folder", "game.chess"));
        rig.Choose("File", "Save game");
        Assert.StartsWith("Unable to save the game:", rig.TakeMessage().Text, StringComparison.Ordinal);
        rig.Choose("File", "Save game");                          // Cancelled
        rig.Choose("File", "Load game");                          // Cancelled
        rig.Platform.FileAnswers.Enqueue(new InvalidOperationException("no portal"));
        rig.Choose("File", "Load game");
        Assert.Equal(("Files", "The file dialog could not be opened:\nno portal"), rig.TakeMessage());

        rig.Controller.ClearBoard();
        rig.Choose("File", "Save game");
        Assert.Equal(("Save game", "Finish editing the position before saving."), rig.TakeMessage());
        await Task.CompletedTask;
    });

    [Fact]
    public async Task PgnIsImportedAndExported() => await ControllerThread.Run(async () =>
    {
        using var rig = new Rig(folder, new SavedGame { Moves = ["e2e4", "e7e5", "g1f3"] });
        string exported = Path.Combine(folder, "game");
        rig.Platform.FileAnswers.Enqueue(exported);
        rig.Choose("File", "Export PGN...");
        await rig.SettleAsync(() => ReadBack(exported + ".pgn").Contains("Nf3", StringComparison.Ordinal));

        rig.Controller.NewGame(Color.White);
        rig.Platform.FileAnswers.Enqueue(exported + ".pgn");
        rig.Choose("File", "Import PGN...");
        await rig.SettleAsync(() => rig.Controller.Game.Moves.Count == 3);

        string junk = Path.Combine(folder, "junk.pgn");
        await File.WriteAllTextAsync(junk, "1. Zz9");
        rig.Platform.FileAnswers.Enqueue(junk);
        rig.Choose("File", "Import PGN...");
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        Assert.Equal(("Import PGN", "The file does not hold a readable PGN game."), rig.TakeMessage());
        rig.Platform.FileAnswers.Enqueue(folder);
        rig.Choose("File", "Import PGN...");
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        Assert.StartsWith("Unable to read the file:", rig.TakeMessage().Text, StringComparison.Ordinal);
        rig.Platform.FileAnswers.Enqueue(Path.Combine(folder, "no", "such", "game.pgn"));
        rig.Choose("File", "Export PGN...");
        await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
        Assert.StartsWith("Unable to save the file:", rig.TakeMessage().Text, StringComparison.Ordinal);
        rig.Choose("File", "Import PGN...");                      // Cancelled
        rig.Choose("File", "Export PGN...");                      // Cancelled

        rig.Controller.ClearBoard();
        rig.Choose("File", "Export PGN...");
        Assert.Equal(("Export PGN", "Finish editing the position before exporting."), rig.TakeMessage());
    });

    /// <summary>
    /// A file's text once it can be read: empty while it is missing or still held open by its writer.
    /// </summary>
    private static string ReadBack(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return "";
        }
    }

    [Fact]
    public void ASaveDialogsPathKeepsItsOwnExtension()
    {
        Assert.Equal("game.pgn", MainView.WithExtension("game.pgn", "chess"));
        Assert.Equal("game.chess", MainView.WithExtension("game", "chess"));
    }
}

/// <summary>
/// A view's left edge, for the layout's assertions.
/// </summary>
internal static class ViewEdges
{
    public static float Left(this View view) => view.Bounds.Left;
}
