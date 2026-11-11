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

    private readonly string _folder = Directory.CreateTempSubdirectory("stoopid-chess-gui-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>
    /// A main view in a window, its platform faked and its external engines scripted by path.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        private readonly Dictionary<string, Func<string, IEnumerable<string>?>> _scripts = [];

        public Rig(SavedGame? game = null)
        {
            Controller = new GameController
            {
                LimitsFor = limits => limits with { Depth = 1 },
                StartExternalEngine = path =>
                    _scripts.TryGetValue(path, out Func<string, IEnumerable<string>?>? script)
                        ? ScriptedEngine.Start(Path.GetFileName(path), script)
                        : throw new InvalidOperationException($"could not start '{path}'"),
            };
            if (game is not null)
            {
                Assert.True(Controller.TryLoad(game));
            }
            View = new MainView(Platform, Controller);
            Window = new ViewHarness(View);
        }

        public FakePlatform Platform { get; } = new();

        public GameController Controller { get; }

        public MainView View { get; }

        public ViewHarness Window { get; }

        public string Register(string path, Func<string, IEnumerable<string>?> script)
        {
            _scripts[path] = script;
            return path;
        }

        /// <summary>The dialog on top; fails when there is none.</summary>
        public T Dialog<T>()
            where T : DialogView => Assert.IsType<T>(Window.Root.Overlays[^1]);

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

        public SKPoint Square(string name) =>
            View.Board.ToRoot(View.Board.SquareCentre(StoopidChess.Core.Square.Parse(name)));

        public async Task SettleAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                Assert.True(
                    DateTime.UtcNow < deadline,
                    $"the condition never held: engine {Controller.EnginePath ?? "built-in"} "
                        + $"({Controller.EngineName}, {(Controller.HasEngine ? "running" : "none")}), overlays "
                        + string.Join(
                            ", ",
                            Window.Root.Overlays.Select(overlay =>
                                overlay is MessageDialog message ? message.Message.Text : overlay.GetType().Name
                            )
                        )
                );
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
        using var rig = new Rig();
        MainView view = rig.View;
        Assert.Equal(["File", "Edit", "Game", "View", "Engine", "Help"], view.MenuBar.Menus.Select(menu => menu.Text));
        Assert.Equal(MenuBarView.BarHeight, view.MenuBar.Height);
        Assert.Equal(view.MoveList.Left(), view.MoveNowButton.Left());
        Assert.True(view.MoveNowButton.Bounds.Top > view.MoveList.Bounds.Bottom);
        Assert.Equal(MainView.CaptureBinHeight, view.CaptureBin.Height);
        Assert.Equal(660 - StatusBarView.BarHeight, view.StatusBar.Bounds.Top);
        Assert.False(view.MoveNowButton.IsEnabled);
        Assert.Equal("Stoopid Chess", rig.Platform.Title);
        Assert.Equal(
            ["You are playing:", "Color to move:", "Difficulty:", "", "White clock:", "Black clock:"],
            view.StatusBar.Segments.Select(segment => segment.Caption)
        );
        Assert.Equal(new SKSizeI(860, 660), MainView.DefaultSize);
        Assert.Equal(new SKSizeI(680, 540), MainView.MinimumSize);
        Assert.Equal(
            "Ctrl+N".Replace("Ctrl", Shortcut.PrimaryName, StringComparison.Ordinal),
            view.MenuBar.Menus[2].Items[0].Items[0].Shortcut
        );
        rig.Window.Render();
        Assert.Throws<ArgumentNullException>(() => new MainView(null!, rig.Controller));
        Assert.Throws<ArgumentNullException>(() => new MainView(rig.Platform, null!));
    }

    [Theory]
    [InlineData(860, 660)] // The default size
    [InlineData(680, 540)] // The least
    [InlineData(700, 1000)] // Tall and narrow: the board is as wide as the column's least width allows
    [InlineData(1400, 600)] // Short and wide: the column takes the width the board cannot use
    [InlineData(1200, 900)]
    [InlineData(681, 541)] // An odd size: the board is a whole number of pixels per square
    public void TheBoardIsSquareAndTheMoveListColumnTakesTheRestOfTheWidth(int width, int height)
    {
        using var rig = new Rig();
        rig.Window.Root.Resize(width, height);
        MainView view = rig.View;
        BoardView board = view.Board;
        float side = (8 * board.SquareSize) + (2 * BoardView.Edge);
        Assert.Equal(side, board.Width);
        Assert.Equal(side, board.Height);
        Assert.Equal(MainView.Margin, board.Bounds.Left);
        Assert.Equal(MenuBarView.BarHeight + MainView.Margin, board.Bounds.Top);
        // The capture bin under the board, as wide as it
        Assert.Equal(board.Bounds.Left, view.CaptureBin.Bounds.Left);
        Assert.Equal(board.Width, view.CaptureBin.Width);
        Assert.Equal(board.Bounds.Bottom + MainView.Margin, view.CaptureBin.Bounds.Top);
        Assert.True(view.CaptureBin.Bounds.Bottom <= view.StatusBar.Bounds.Top - MainView.Margin);
        // The column one margin right of the board, to the window's edge, never narrower than its least width
        Assert.Equal(board.Bounds.Right + MainView.Margin, view.MoveList.Bounds.Left);
        Assert.Equal(width - MainView.Margin, view.MoveList.Bounds.Right);
        Assert.True(view.MoveList.Width >= MainView.ColumnWidth);
        Assert.Equal(view.MoveList.Bounds.Left, view.MoveNowButton.Bounds.Left);
        Assert.Equal(view.MoveList.Bounds.Right, view.MoveNowButton.Bounds.Right);
        Assert.Equal(view.StatusBar.Bounds.Top - MainView.Margin, view.MoveNowButton.Bounds.Bottom);
        // The board as large as it can be: one more pixel per square would cross the column's least width or the bin
        float larger = side + 8;
        Assert.True(
            larger > width - MainView.ColumnWidth - (3 * MainView.Margin)
                || larger
                    > view.StatusBar.Bounds.Top
                        - MainView.Margin
                        - MainView.CaptureBinHeight
                        - MainView.Margin
                        - board.Bounds.Top
        );
        rig.Window.Render();
    }

    [Fact]
    public void AtTheDefaultSizeTheBoardFillsTheHeightAndTheColumnTheWidth()
    {
        using var rig = new Rig();
        Assert.Equal(64, rig.View.Board.SquareSize);
        Assert.Equal(332, rig.View.MoveList.Width);
        Assert.Equal(0, rig.View.MoveList.Bounds.Left - rig.View.Board.Bounds.Right - MainView.Margin);
    }

    [Fact]
    public void ADraggedMoveIsPlayedAndAnIllegalOneRefused()
    {
        using var rig = new Rig();
        rig.Window.Drag(rig.Square("e2"), rig.Square("e5"));
        Assert.Equal(("Illegal move", "That move is not permitted."), rig.TakeMessage());
        rig.Window.Drag(rig.Square("e2"), rig.Square("e4"));
        Assert.Equal(Square.Parse("e4"), rig.Controller.Game.Position.LastMove!.Value.To);
        Assert.Contains("e4", rig.View.MoveList.Lines[0], StringComparison.Ordinal);
        rig.Window.Drag(rig.Square("e7"), rig.Square("e5")); // Not the human's piece: nothing
        Assert.Empty(rig.Window.Root.Overlays);
    }

    [Fact]
    public void APromotionAsksForThePiece()
    {
        using var rig = new Rig(new SavedGame { StartFen = "8/4P3/8/8/8/8/k7/4K3 w - - 0 1" });
        rig.Window.Drag(rig.Square("e7"), rig.Square("e8"));
        PromotionDialog dialog = rig.Dialog<PromotionDialog>();
        Assert.Equal(Piece.None, dialog.Choice);
        rig.Window.Click(dialog.Pieces[3]);
        Assert.Equal((Color.White, Piece.Knight), rig.Controller.PieceAt(Square.Parse("e8")));
    }

    [Fact]
    public void EscapeInThePromotionDialogCancelsTheMove()
    {
        using var rig = new Rig(new SavedGame { StartFen = "8/4P3/8/8/8/8/k7/4K3 w - - 0 1" });
        rig.Window.Drag(rig.Square("e7"), rig.Square("e8"));
        rig.Dialog<PromotionDialog>();
        rig.Window.Key(Key.Escape);
        Assert.Empty(rig.Window.Root.Overlays);
        Assert.Empty(rig.Controller.Game.Moves);
        Assert.Equal((Color.White, Piece.Pawn), rig.Controller.PieceAt(Square.Parse("e7")));
    }

    [Fact]
    public void TheEditMenuEditsThePosition()
    {
        using var rig = new Rig();
        rig.Window.Click(rig.Square("e4"), PointerButton.Right);
        MenuPopup menu = Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single());
        rig.ChooseIn(menu, "Black", "Queen");
        Assert.Equal((Color.Black, Piece.Queen), rig.Controller.PieceAt(Square.Parse("e4")));
        rig.Window.Click(rig.Square("a1"), PointerButton.Right);
        rig.ChooseIn(Assert.IsType<MenuPopup>(rig.Window.Root.Overlays.Single()), "Clear entire position");
        Assert.True(rig.Controller.IsEditing);
        Assert.Equal("Position edited by user: place both kings to continue", rig.View.MoveList.Lines.Single());
        rig.Window.Click(rig.Square("e2")); // Unfinished: said so
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
        using var rig = new Rig();
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
        rig.Window.Key(Key.X, KeyModifiers.Alt); // No such menu
        rig.Window.Key(Key.B, KeyModifiers.Primary); // No such shortcut
        rig.Window.Key(Key.Return);
        Assert.Empty(rig.Window.Root.Overlays);
    }

    [Fact]
    public void ANewGameStartsAtOnceAsTheChosenSide()
    {
        using var rig = new Rig(new SavedGame { Moves = ["e2e4"] });
        rig.Window.Key(Key.N, KeyModifiers.Primary | KeyModifiers.Shift);
        Assert.Empty(rig.Controller.Game.Moves);
        Assert.Equal(Color.Black, rig.Controller.HumanColor);
        Assert.Empty(rig.Window.Root.Overlays);

        rig.Choose("Game", "New Game", "White");
        Assert.Equal(Color.White, rig.Controller.HumanColor);
        rig.Window.Key(Key.N, KeyModifiers.Primary);
        Assert.Equal(Color.White, rig.Controller.HumanColor);
    }

    [Fact]
    public void TheGameMenuSetsLevelsAndSides()
    {
        using var rig = new Rig(new SavedGame { Moves = ["e2e4"] });
        rig.Choose("Game", "Set level", "Hard");
        Assert.Equal(Level.Hard, rig.Controller.Level);
        Assert.True(rig.View.MenuBar.Menus[2].Items[4].Items[2].IsChecked);
        rig.Choose("Game", "Undo last move");
        Assert.Empty(rig.Controller.Game.Moves);
        rig.Choose("Game", "Switch sides");
        Assert.Equal(Color.Black, rig.Controller.HumanColor);
        Assert.True(rig.Controller.IsBoardFlipped); // Switching to Black turns the board round
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
    public void TheClocksCountDownInTheStatusBar()
    {
        using var rig = new Rig();
        rig.Controller.SetTimeControl(TimeControl.OfMinutes(5, 0));
        rig.View.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal("5+0", rig.View.StatusBar.Segments[3].Value);
        StatusSegment white = rig.View.StatusBar.Segments[4];
        Assert.Equal("00:04:58", white.Value);
        Assert.True(white.IsRunning);
        Assert.False(rig.View.StatusBar.Segments[5].IsRunning);
        rig.Window.Render();
    }

    [Fact]
    public void TheTimeControlMenuChoosesTheClock()
    {
        using var rig = new Rig();
        MenuItem timeControl = rig.View.MenuBar.Menus[2].Items[3];
        Assert.Equal("Time control", timeControl.Text);
        Assert.Equal(TimeControl.Default.ToString(), timeControl.Items.Single(item => item.IsChecked).Text);
        rig.Choose("Game", "Time control", "5+3");
        Assert.Equal(TimeControl.OfMinutes(5, 3), rig.Controller.TimeControl);
        Assert.Equal("5+3", timeControl.Items.Single(item => item.IsChecked).Text);
        Assert.Equal(
            ["40/5", "40/40", "1+0", "3+2", "5+3", "10+0", "15+10", "30+0"],
            timeControl.Items.Select(item => item.Text)
        );
        rig.Choose("Game", "Time control", "40/40");
        Assert.Equal(TimeControl.Periods(40, 40), rig.Controller.TimeControl);
        Assert.Equal("40/40", rig.View.StatusBar.Segments[3].Value);
    }

    [Fact]
    public async Task TheAnalysisWindowOpensOnceAndShowsTheEnginesLines() =>
        await ControllerThread.Run(async () =>
        {
            using var rig = new Rig();
            string engine = rig.Register(
                "analyst",
                command =>
                    command.StartsWith("go", StringComparison.Ordinal)
                        ? ["info depth 1 score cp 12 nodes 20 pv e7e5", "info string not shown", "bestmove e7e5"]
                        : []
            );
            rig.Choose("View", "Analysis window");
            FakePlatform.FakeWindow window = Assert.Single(rig.Platform.Windows);
            Assert.Equal("Analysis", window.Title);
            Assert.Same(rig.View.Analysis, window.Content.Content);
            rig.Window.Key(Key.A, KeyModifiers.Primary);
            Assert.Equal(1, window.Activations);
            Assert.Single(rig.Platform.Windows);

            Assert.True(await rig.Controller.StartEngineAsync(engine));
            rig.Window.Drag(rig.Square("e2"), rig.Square("e4"));
            await rig.SettleAsync(() =>
                rig.Controller.Game.Moves.Count == 2 && rig.View.Analysis!.Output.Lines.Count == 1
            );
            Assert.StartsWith("d  1", rig.View.Analysis!.Output.Lines[0], StringComparison.Ordinal);

            rig.Choose("Engine", "Show engine output in move list");
            Assert.Contains(rig.View.MoveList.Lines, line => line.Contains("info depth 1", StringComparison.Ordinal));
            window.Close();
            Assert.Null(rig.View.Analysis);
            rig.Window.Key(Key.A, KeyModifiers.Primary);
            Assert.Equal(2, rig.Platform.Windows.Count);
            rig.View.Dispose(); // Closes it
            Assert.True(rig.Platform.Windows[1].IsClosed);
        });

    [Fact]
    public async Task TheEngineIsStartedAndItsFailuresAreSaid() =>
        await ControllerThread.Run(async () =>
        {
            using var rig = new Rig();
            await rig.View.StartAsync();
            Assert.StartsWith("Stoopid Chess - Stoopid Chess ", rig.Platform.Title, StringComparison.Ordinal);
            Assert.True(rig.View.EngineMenuItems[0].IsChecked);

            string illegal = rig.Register("illegal", ScriptedEngine.Plays("a1a8"));
            Assert.True(await rig.Controller.StartEngineAsync(illegal));
            rig.Window.Drag(rig.Square("e2"), rig.Square("e4"));
            await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
            Assert.Equal(("Engine", "The engine played an illegal move: a1a8"), rig.TakeMessage());

            string dies = rig.Register(
                "dies",
                command => command.StartsWith("go", StringComparison.Ordinal) ? null : []
            );
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
    public async Task AnotherUciEngineIsOpenedByItsPath() =>
        await ControllerThread.Run(async () =>
        {
            using var rig = new Rig();
            string path = rig.Register(Path.Combine(_folder, "pawnstar"), ScriptedEngine.Plays("e7e5"));
            rig.Platform.FileAnswers.Enqueue(path);
            rig.Choose("Engine", "Open UCI engine...");
            await rig.SettleAsync(() => rig.Controller.EnginePath == path && rig.Controller.HasEngine);
            Assert.Equal("pawnstar", rig.Controller.EngineName);
            Assert.False(rig.View.EngineMenuItems[0].IsChecked);

            rig.Choose("Engine", "Use built-in engine");
            await rig.SettleAsync(() => rig.Controller.EnginePath is null && rig.Controller.HasEngine);
            Assert.True(rig.View.EngineMenuItems[0].IsChecked);

            rig.Platform.FileAnswers.Enqueue(Path.Combine(_folder, "missing"));
            rig.Choose("Engine", "Open UCI engine...");
            await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
            Assert.StartsWith("The engine could not be started:", rig.TakeMessage().Text, StringComparison.Ordinal);
            await rig.SettleAsync(() => rig.Controller.EnginePath is null && rig.Controller.HasEngine); // The fallback
            rig.Choose("Engine", "Open UCI engine..."); // Cancelled: the engine stays
            Assert.True(rig.Controller.HasEngine);
        });

    [Fact]
    public async Task GamesAreSavedAndLoaded() =>
        await ControllerThread.Run(async () =>
        {
            using var rig = new Rig(new SavedGame { Moves = ["e2e4", "e7e5"] });
            string saved = Path.Combine(_folder, "game");
            rig.Platform.FileAnswers.Enqueue(saved);
            rig.Window.Key(Key.S, KeyModifiers.Primary);
            Assert.True(File.Exists(saved + ".chess")); // The extension added
            Assert.Equal("Save game", rig.Platform.FileDialogs[^1].Title);
            Assert.Equal("chess", rig.Platform.FileDialogs[^1].Filters[0].Extensions);

            rig.Controller.NewGame(Color.White);
            rig.Platform.FileAnswers.Enqueue(saved + ".chess");
            rig.Window.Key(Key.O, KeyModifiers.Primary);
            Assert.Equal(2, rig.Controller.Game.Moves.Count);

            string junk = Path.Combine(_folder, "junk.chess");
            await File.WriteAllTextAsync(junk, "not a game");
            rig.Platform.FileAnswers.Enqueue(junk);
            rig.Choose("File", "Load game");
            Assert.Equal(
                ("Load game", "This file does not contain a game in Stoopid Chess format."),
                rig.TakeMessage()
            );
            rig.Platform.FileAnswers.Enqueue(_folder); // A folder: unreadable
            rig.Choose("File", "Load game");
            Assert.StartsWith("Unable to load the game:", rig.TakeMessage().Text, StringComparison.Ordinal);
            rig.Platform.FileAnswers.Enqueue(Path.Combine(_folder, "no", "such", "folder", "game.chess"));
            rig.Choose("File", "Save game");
            Assert.StartsWith("Unable to save the game:", rig.TakeMessage().Text, StringComparison.Ordinal);
            rig.Choose("File", "Save game"); // Cancelled
            rig.Choose("File", "Load game"); // Cancelled
            rig.Platform.FileAnswers.Enqueue(new InvalidOperationException("no portal"));
            rig.Choose("File", "Load game");
            Assert.Equal(("Files", "The file dialog could not be opened:\nno portal"), rig.TakeMessage());

            rig.Controller.ClearBoard();
            rig.Choose("File", "Save game");
            Assert.Equal(("Save game", "Finish editing the position before saving."), rig.TakeMessage());
            await Task.CompletedTask;
        });

    [Fact]
    public async Task PgnIsImportedAndExported() =>
        await ControllerThread.Run(async () =>
        {
            using var rig = new Rig(new SavedGame { Moves = ["e2e4", "e7e5", "g1f3"] });
            string exported = Path.Combine(_folder, "game");
            rig.Platform.FileAnswers.Enqueue(exported);
            rig.Choose("File", "Export PGN...");
            await rig.SettleAsync(() => ReadBack(exported + ".pgn").Contains("Nf3", StringComparison.Ordinal));

            rig.Controller.NewGame(Color.White);
            rig.Platform.FileAnswers.Enqueue(exported + ".pgn");
            rig.Choose("File", "Import PGN...");
            await rig.SettleAsync(() => rig.Controller.Game.Moves.Count == 3);

            string junk = Path.Combine(_folder, "junk.pgn");
            await File.WriteAllTextAsync(junk, "1. Zz9");
            rig.Platform.FileAnswers.Enqueue(junk);
            rig.Choose("File", "Import PGN...");
            await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
            Assert.Equal(("Import PGN", "The file does not hold a readable PGN game."), rig.TakeMessage());
            rig.Platform.FileAnswers.Enqueue(_folder);
            rig.Choose("File", "Import PGN...");
            await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
            Assert.StartsWith("Unable to read the file:", rig.TakeMessage().Text, StringComparison.Ordinal);
            rig.Platform.FileAnswers.Enqueue(Path.Combine(_folder, "no", "such", "game.pgn"));
            rig.Choose("File", "Export PGN...");
            await rig.SettleAsync(() => rig.Window.Root.Overlays.Count > 0);
            Assert.StartsWith("Unable to save the file:", rig.TakeMessage().Text, StringComparison.Ordinal);
            rig.Choose("File", "Import PGN..."); // Cancelled
            rig.Choose("File", "Export PGN..."); // Cancelled

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
