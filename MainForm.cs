using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Diagnostics;


namespace StoopidChess
{
    /// <summary>
    /// Application primary Form
    /// </summary>
    public partial class MainForm : Form
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception e)
            {
                MessageBox.Show("An unexpected error has occurred. Stoopid Chess will exit.", "Oops");
                Debug.WriteLine(e.ToString());
                Debug.WriteLine(e.Message);
            }
        }
        /// <summary>
        /// Instance variables
        /// </summary>
        internal PieceType promotedPiece;       // User's pawn promotion choice
        internal Bitmap[,] pieceBitmaps;        // Bitmaps for each piece in black and white [colour, pieceType]
        private Game game;                      // Game engine
        private Bitmap movingPieceBitmap;       // Piece currently being dragged by the user
        private Square[,] board;                // Holds copy of board as real board changes during search
        private int mouseX, mouseY;             // Holds mouse movements for animating piece being dragged
        private Colour colourToMove;            // Who's turn is it to move
        private Colour humanColour;             // Which colour is the user playing
        private bool isBoardFlipped;            // Board displays black on the bottom if true
        private List<Square>[] captureBins;     // Array of lists of capture bins
        private TimeSpan whiteClock, blackClock;// Total elapsed time per player since the game began
        private Timer timer;                    // Ticks every 100ms
        private bool isGameOver;                // End game menu support
        private MoveFromTo lastMove;            // The last move made is highlighted in the picture box
        private MoveFromTo currentMove;         // The move which the user is currently making
        private bool isTestGame;                // Is computer playing against itself

        public MainForm()
        {
            InitializeComponent();
            InitializeGame();
        }

        private void LoadPieceBitmaps()
        {
            pieceBitmaps = new Bitmap[3, 7];
            pieceBitmaps[(int)Colour.WHITE, (int)PieceType.PAWN] = ChessPieceImages.WhitePawn;
            pieceBitmaps[(int)Colour.WHITE, (int)PieceType.KNIGHT] = ChessPieceImages.WhiteKnight;
            pieceBitmaps[(int)Colour.WHITE, (int)PieceType.BISHOP] = ChessPieceImages.WhiteBishop;
            pieceBitmaps[(int)Colour.WHITE, (int)PieceType.ROOK] = ChessPieceImages.WhiteRook;
            pieceBitmaps[(int)Colour.WHITE, (int)PieceType.QUEEN] = ChessPieceImages.WhiteQueen;
            pieceBitmaps[(int)Colour.WHITE, (int)PieceType.KING] = ChessPieceImages.WhiteKing;

            pieceBitmaps[(int)Colour.BLACK, (int)PieceType.PAWN] = ChessPieceImages.BlackPawn;
            pieceBitmaps[(int)Colour.BLACK, (int)PieceType.KNIGHT] = ChessPieceImages.BlackKnight;
            pieceBitmaps[(int)Colour.BLACK, (int)PieceType.BISHOP] = ChessPieceImages.BlackBishop;
            pieceBitmaps[(int)Colour.BLACK, (int)PieceType.ROOK] = ChessPieceImages.BlackRook;
            pieceBitmaps[(int)Colour.BLACK, (int)PieceType.QUEEN] = ChessPieceImages.BlackQueen;
            pieceBitmaps[(int)Colour.BLACK, (int)PieceType.KING] = ChessPieceImages.BlackKing;
        }
        /// <summary>
        /// Set up state for the start of the game
        /// </summary>
        private void InitializeGame()
        {
            lastMove.xfrom = -1;    // No highlighted move
            LoadPieceBitmaps();
            game = new Game();
            game.ComputerThinkingCompletedEvent += new ComputerThinkingCompletedHandler(HandleComputerThinkingCompleted);
            board = game.GetBoard();
            captureBins = game.GetCaptureBins();
            movingPieceBitmap = null;
            colourToMove = Colour.WHITE;
            humanColour = Colour.WHITE;
            isBoardFlipped = false;
            colourToMoveLabel.Text = colourToMove.ToString();
            humanColourLabel.Text = humanColour.ToString();
            SetLevelEasyHandler(this, new EventArgs());
            timer = new Timer();
            timer.Interval = 100;
            timer.Tick += new EventHandler(TimerTick);
            isGameOver = false;
            whiteClock = new TimeSpan();
            blackClock = new TimeSpan();
            timer.Start();
            isTestGame = false;
            stopThinkingButton.Enabled = false;
            moveListTextBox.SelectionTabs = new int[] { 40, 80, 120, 160, 200 };
#if DEBUG
            debugToolStripMenuItem.Visible = true;
#else
            debugToolStripMenuItem.Visible = false;
#endif
        }
        /// <summary>
        /// Handler for the timer tick event (100 millisecond intervals)
        /// </summary>
        void TimerTick(object sender, EventArgs e)
        {
            if (colourToMove == Colour.WHITE)
            {
                whiteClock += new TimeSpan(0, 0, 0, 0, 100);
                whiteClockLabel.Text = String.Format("{0:d2}:{1:d2}:{2:d2}", whiteClock.Hours, whiteClock.Minutes, whiteClock.Seconds);
            }
            else if (colourToMove == Colour.BLACK)
            {
                blackClock += new TimeSpan(0, 0, 0, 0, 100);
                blackClockLabel.Text = String.Format("{0:d2}:{1:d2}:{2:d2}", blackClock.Hours, blackClock.Minutes, blackClock.Seconds);
            }
        }

        delegate void MakeCrossThreadMove(Move move);
        /// <summary>
        /// Handler for the computer thinking completed event
        /// </summary>
        void HandleComputerThinkingCompleted(Move move)
        {
            // Invoke the move on the main UI thread as cross thread calls 
            // on a Windows ComponentModel object are not permitted
            this.Invoke(new MakeCrossThreadMove(MakeMove), move);
        }
        /// <summary>
        /// Handles the board picture box paint event
        /// </summary>
        private void DrawChessBoard(Graphics graphics)
        {
            int squareSize = Math.Min(chessBoardPictureBox.Width >> 3, chessBoardPictureBox.Height >> 3);
            // Draw the board
            for (int x = 0; x != 8; x++)
            {
                for (int y = 0; y != 8; y++)
                {
                    if (((x + y) & 1) == 0)
                    {
                        graphics.FillRectangle(Brushes.Ivory, x * squareSize, y * squareSize, squareSize, squareSize);
                    }
                    else
                    {
                        graphics.FillRectangle(Brushes.Gray, x * squareSize, y * squareSize, squareSize, squareSize);
                    }
                }
            }
            // Draw the pieces
            for (int x = 0; x != 8; x++)
            {
                for (int y = 0; y != 8; y++)
                {
                    if (board[x, y].piece != PieceType.NONE)
                    {
                        if (movingPieceBitmap == null || x != currentMove.xfrom || y != currentMove.yfrom)
                        {
                            // Don't draw the moving piece here
                            int drawX = (isBoardFlipped ? (7 - x) * squareSize : x * squareSize);
                            int drawY = (isBoardFlipped ? y * squareSize : (7 - y) * squareSize);
                            graphics.DrawImage(pieceBitmaps[(int)board[x, y].colour, (int)board[x, y].piece], drawX, drawY, squareSize, squareSize);
                        }
                    }
                }
            }
            // Highlight the last move's squares
            if (lastMove.xfrom != -1)
            {
                Pen pen = new Pen(Color.DarkGoldenrod, 2.5f);
                int drawX = (isBoardFlipped ? (7 - lastMove.xfrom) * squareSize : lastMove.xfrom * squareSize);
                int drawY = (isBoardFlipped ? lastMove.yfrom * squareSize : (7 - lastMove.yfrom) * squareSize);
                graphics.DrawRectangle(pen, drawX, drawY, squareSize, squareSize);
                drawX = (isBoardFlipped ? (7 - lastMove.xto) * squareSize : lastMove.xto * squareSize);
                drawY = (isBoardFlipped ? lastMove.yto * squareSize : (7 - lastMove.yto) * squareSize);
                graphics.DrawRectangle(pen, drawX, drawY, squareSize, squareSize);
            }
            if (movingPieceBitmap != null)
            {
                // Animate the piece which is being dragged by the mouse
                graphics.DrawImage(movingPieceBitmap, mouseX - (squareSize >> 1), mouseY - (squareSize >> 1), squareSize, squareSize);
            }
        }
        /// <summary>
        /// Handler for the board picture box paint event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ChessBoardPictureBoxPaintHandler(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            DrawChessBoard(graphics);
            // Also redraw the capture bins
            captureBinPictureBox.Invalidate();
        }
        /// <summary>
        /// Handler for the capture bin picture box paint event
        /// </summary>
        private void CaptureBinPictureBoxPaintHandler(object sender, PaintEventArgs e)
        {
            int squareSize = Math.Min(captureBinPictureBox.Width >> 4, captureBinPictureBox.Height >> 1);
            Graphics graphics = e.Graphics;
            for (int i = 0; i != captureBins[(int)Colour.WHITE].Count; i++)
            {
                Bitmap bitmap = pieceBitmaps[(int)Colour.WHITE, (int)captureBins[(int)Colour.WHITE][i].piece];
                graphics.DrawImage(bitmap, i * squareSize, 0, squareSize, squareSize);
            }
            for (int i = 0; i != captureBins[(int)Colour.BLACK].Count; i++)
            {
                Bitmap bitmap = pieceBitmaps[(int)Colour.BLACK, (int)captureBins[(int)Colour.BLACK][i].piece];
                graphics.DrawImage(bitmap, i * squareSize, squareSize, squareSize, squareSize);
            }
        }
        /// <summary>
        /// Handler for the board picture box mouse down event
        /// </summary>
        private void ChessBoardPictureBoxMouseDownHandler(object sender, MouseEventArgs e)
        {
            if (colourToMove != humanColour || e.Button != MouseButtons.Left || isGameOver)
            {
                return;
            }
            int x, y;
            GetSquareFromMousePosition(out x, out y, e);
            if (movingPieceBitmap == null && board[x, y].colour == humanColour)
            {
                if (!game.IsPositionLegal())
                {
                    new IllegalPositionForm().ShowDialog();
                    return;
                }
                currentMove.xfrom = x;
                currentMove.yfrom = y;
                // Set the bitmap to be animated
                movingPieceBitmap = pieceBitmaps[(int)board[x, y].colour, (int)board[x, y].piece];
                mouseX = e.X;
                mouseY = e.Y;
            }
        }
        /// <summary>
        /// Handler for the board picture box mouse move event
        /// </summary>
        private void ChessBoardPictureBoxMouseMoveHandler(object sender, MouseEventArgs e)
        {
            if (movingPieceBitmap != null)
            {
                mouseX = e.X;
                mouseY = e.Y;
                chessBoardPictureBox.Invalidate();
            }
        }
        /// <summary>
        /// Handler for the board picture box mouse up event
        /// </summary>
        private void ChessBoardPictureBoxMouseUpHandler(object sender, MouseEventArgs e)
        {
            if (colourToMove != humanColour || movingPieceBitmap == null || e.Button != MouseButtons.Left)
            {
                return;
            }
            int x, y;
            GetSquareFromMousePosition(out x, out y, e);
            movingPieceBitmap = null;
            if (x == currentMove.xfrom && y == currentMove.yfrom)
            {
                // If the user drops the piece back on its original square do nothing
                chessBoardPictureBox.Invalidate();
                return;
            }
            currentMove.xto = x;
            currentMove.yto = y;
            // Determine the user's move
            Move move = game.DetermineMove(currentMove, humanColour, GetHumanChoiceOfPawnPromotion);
            if (!game.IsMoveLegal(move, humanColour))
            {
                chessBoardPictureBox.Invalidate();
                new MoveNotPermittedForm().ShowDialog();
            }
            else
            {
                // Move is legal so make it and return
                MakeMove(move);
            }
        }
        /// <summary>
        /// Determine the square x and y co-ordinates from the mouse
        /// position within the chess board picture box
        /// </summary>
        private void GetSquareFromMousePosition(out int x, out int y, MouseEventArgs e)
        {
            int squareSize = Math.Min(chessBoardPictureBox.Width >> 3, chessBoardPictureBox.Height >> 3);
            x = (e.X / squareSize);
            y = 7 - (e.Y / squareSize);
            y = Math.Max(y, 0);
            x = Math.Min(x, 7);
            y = Math.Min(y, 7);
            if (isBoardFlipped)
            {
                x = 7 - x;
                y = 7 - y;
            }
        }
        /// <summary>
        /// Make a move, display it, and change colour to play
        /// </summary>
        private void MakeMove(Move move)
        {
            game.DoMove(move);
            moveListTextBox.Clear();
            moveListTextBox.AppendText(game.GetMoveHistory());
            captureBins = game.GetCaptureBins();
            board = game.GetBoard();
            lastMove.xfrom = move.from & Board.FILE;
            lastMove.yfrom = (move.from & Board.RANK) >> 4;
            lastMove.xto = move.to & Board.FILE;
            lastMove.yto = (move.to & Board.RANK) >> 4;
            chessBoardPictureBox.Invalidate();
            this.Activate();
#if DEBUG
            // Display debug vars
            if (colourToMove == (humanColour ^ Colour.XOR) || isTestGame)
            {
                DisplayDebugInfo();
            }
#endif
            colourToMove ^= Colour.XOR;
            // Enable menu strip only when it is the human's turn
            // i.e. the computer thinking worker thread is not running
            if (colourToMove == humanColour)
            {
                mainMenuStrip.Enabled = true;
                stopThinkingButton.Enabled = false;
            }
            else
            {
                mainMenuStrip.Enabled = false;
                stopThinkingButton.Enabled = true;
            }
            colourToMoveLabel.Text = colourToMove.ToString();
            // Test for end game
            if (game.IsCheckmated(colourToMove))
            {
                moveListTextBox.AppendText(Environment.NewLine + "CHECKMATE: " + (colourToMove ^ Colour.XOR).ToString() + " wins");
                isGameOver = true;
                timer.Stop();
                mainMenuStrip.Enabled = true;
            }
            else if (game.IsDrawn(colourToMove))
            {
                moveListTextBox.AppendText(Environment.NewLine + "GAME OVER: DRAW");
                moveListTextBox.AppendText(Environment.NewLine + game.reasonForDraw);
                isGameOver = true;
                timer.Stop();
                mainMenuStrip.Enabled = true;
            }
            else if (game.IsInCheck(colourToMove))
            {
                this.moveListTextBox.AppendText(Environment.NewLine + "CHECK");
            }
            if (!isGameOver && (colourToMove == (humanColour ^ Colour.XOR) || isTestGame))
            {
                game.StartComputerThinking(colourToMove);
            }
            moveListTextBox.Focus();
            moveListTextBox.SelectionStart = moveListTextBox.Text.Length;
        }

        private void DisplayDebugInfo()
        {
            moveListTextBox.AppendText(String.Format("\r\nAlpha-beta:\t\t{0}\r\n", Game.debug.callsToAlphaBeta));
            moveListTextBox.AppendText(String.Format("Beta cutoffs:\t\t{0}\r\n", Game.debug.alphaBetaCutoffs));
            moveListTextBox.AppendText(String.Format("Always hits:\t\t{0}\r\n", Game.debug.alwaysReplaceTranspositionHits));
            moveListTextBox.AppendText(String.Format("Deeper hits:\t\t{0}\r\n", Game.debug.replaceDeeperTranspositionHits));
            moveListTextBox.AppendText(String.Format("Hash exact:\t\t{0}\r\n", Game.debug.transpositionsExact));
            moveListTextBox.AppendText(String.Format("Hash beta:\t\t{0}\r\n", Game.debug.transpositionsBeta));
            moveListTextBox.AppendText(String.Format("Hash moves:\t\t{0}\r\n", Game.debug.transpositionMoves));
            moveListTextBox.AppendText(String.Format("Quiescent:\t\t{0}\r\n", Game.debug.callsToQuiescentSearch));
            moveListTextBox.AppendText(String.Format("Quies cutoffs:\t\t{0}\r\n", Game.debug.quiescentBetaCutoffs));
            moveListTextBox.AppendText(String.Format("Alpha-beta depth:\t{0}\r\n", Game.debug.bestDepthAchieved));
            moveListTextBox.AppendText(String.Format("Transpositions:\t{0}\r\n", Transposition.numberOfTranspositionsCreated));
            moveListTextBox.AppendText(String.Format("IsInCheck calls:\t{0}\r\n", Game.debug.callsToInCheck));
            moveListTextBox.AppendText(String.Format("Check hash hits:\t{0}\r\n", Game.debug.checkDictionaryMatches));
            moveListTextBox.AppendText(String.Format("Check hit ratio:\t{0,3:F}\r\n", (float)Game.debug.checkDictionaryMatches / (float)Game.debug.callsToInCheck));
            moveListTextBox.AppendText(String.Format("Killers found:\t\t{0}\r\n", Game.debug.killersFound));
            moveListTextBox.AppendText(String.Format("Eval calls:\t\t{0}\r\n", Game.debug.callsToGetScore));
            moveListTextBox.AppendText(String.Format("Pawn hash hits:\t{0}\r\n", Game.debug.pawnHashHits));
            moveListTextBox.AppendText(String.Format("Blocked pawns:\t{0}\r\n", Game.debug.blockedPawns));
            moveListTextBox.AppendText(String.Format("Passed pawns:\t{0}\r\n", Game.debug.passedPawns));
            moveListTextBox.AppendText(String.Format("Doubled pawns:\t{0}\r\n", Game.debug.doubledPawns));
            moveListTextBox.AppendText(String.Format("Score:\t\t\t{0}\r\n", Game.debug.computerScore));
        }
        /// <summary>
        /// Delegate to handle the case of a promoted pawn
        /// </summary>
        internal PieceType GetHumanChoiceOfPawnPromotion(Colour colour)
        {
            new PawnPromotionForm(colour, this).ShowDialog();
            return promotedPiece;
        }
        /// <summary>
        /// Handler for the view->flip board menu item
        /// </summary>
        private void ViewFlipBoardHandler(object sender, EventArgs e)
        {
            isBoardFlipped = !isBoardFlipped;
            chessBoardPictureBox.Invalidate();
        }
        /// <summary>
        /// Handler for the game->undo move menu item
        /// </summary>
        private void UndoLastMoveHandler(object sender, EventArgs e)
        {
            // Don't permit move undo during computer's thinking time as 
            // worker thread will get out of synch with board state
            if (colourToMove != humanColour && !isGameOver)
            {
                return;
            }
            game.UndoLastMove();
            bool isAtGameStart = !game.UndoLastMove();
            isGameOver = false;
            board = game.GetBoard();
            moveListTextBox.Text = "";
            moveListTextBox.AppendText(game.GetMoveHistory());
            captureBins = game.GetCaptureBins();
            lastMove.xfrom = -1;
            chessBoardPictureBox.Invalidate();
            // Have we gone right back to the start of the game?
            // If so, and the computer is playing white, start the computer move again
            if (isAtGameStart)
            {
                colourToMove = Colour.WHITE;
                colourToMoveLabel.Text = colourToMove.ToString();
            }
            // Is it the computer's turn to move?
            if (colourToMove == (humanColour ^ Colour.XOR))
            {
                game.StartComputerThinking(colourToMove);
            }
            timer.Start();
        }
        /// <summary>
        /// Handler for the game->set level->beginner menu item
        /// </summary>
        private void SetLevelBeginnerHandler(object sender, EventArgs e)
        {
            game.SetMaxThinkingTime(1000);
            game.SetMaxDepth(0);
            difficultyLabel.Text = "Beginner";
        }
        /// <summary>
        /// Handler for the game->set level->easy menu item
        /// </summary>
        private void SetLevelEasyHandler(object sender, EventArgs e)
        {
            game.SetMaxThinkingTime(3000);
            game.SetMaxDepth(2);
            difficultyLabel.Text = "Easy";
        }
        /// <summary>
        /// Handler for the game->set level->medium menu item
        /// </summary>
        private void SetLevelMediumHandler(object sender, EventArgs e)
        {
            game.SetMaxThinkingTime(10000);
            game.SetMaxDepth(5);
            difficultyLabel.Text = "Medium";
        }
        /// <summary>
        /// Handler for the game->set level->hard menu item
        /// </summary>
        private void SetLevelHardHandler(object sender, EventArgs e)
        {
            game.SetMaxThinkingTime(20000);
            game.SetMaxDepth(7);
            difficultyLabel.Text = "Hard";
        }
        private void SetLevelBestHandler(object sender, EventArgs e)
        {
            game.SetMaxThinkingTime(40000);
            game.SetMaxDepth(12);
            difficultyLabel.Text = "Best";
        }
        /// <summary>
        /// Handler for the file->exit menu item
        /// </summary>
        private void FileExitHandler(object sender, EventArgs e)
        {
            Application.Exit();
        }
        /// <summary>
        /// Handler for the file->save game menu item
        /// </summary>
        private void FileSaveGameHandler(object sender, EventArgs e)
        {
            try
            {
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    Stream stream = saveFileDialog.OpenFile();
                    game.Serialize(stream);
                    BinaryFormatter formatter = new BinaryFormatter();
                    formatter.Serialize(stream, humanColour);
                    formatter.Serialize(stream, colourToMove);
                    formatter.Serialize(stream, isBoardFlipped);
                    stream.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Unable to save game - check file permissions", "Error");
            }
            finally
            {
                saveFileDialog.Dispose();
            }
        }
        /// <summary>
        /// Handler for the file->load game menu item
        /// </summary>
        private void FileLoadGameHandler(object sender, EventArgs e)
        {
            try
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    Stream stream = openFileDialog.OpenFile();
                    Game newGame = game.Deserialize(stream) as Game;
                    if (newGame == null)
                    {
                        MessageBox.Show("This file does not seem to contain a game in Stoopid Chess format", "Error");
                    }
                    else
                    {
                        game = newGame;
                        game.ComputerThinkingCompletedEvent += new ComputerThinkingCompletedHandler(HandleComputerThinkingCompleted);
                        captureBins = game.GetCaptureBins();
                        board = game.GetBoard();
                        moveListTextBox.Text = game.GetMoveHistory();
                        chessBoardPictureBox.Invalidate();
                        BinaryFormatter formatter = new BinaryFormatter();
                        humanColour = (Colour)formatter.Deserialize(stream);
                        colourToMove = (Colour)formatter.Deserialize(stream);
                        isBoardFlipped = (bool)formatter.Deserialize(stream);
                        colourToMoveLabel.Text = colourToMove.ToString();
                        humanColourLabel.Text = humanColour.ToString();
                        lastMove.xfrom = -1;
                    }
                    stream.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Unable to load game", "Error");
            }
            finally
            {
                openFileDialog.Dispose();
            }
        }
        /// <summary>
        /// Handler for the game->new game->white menu item
        /// </summary>
        private void NewGameWhiteHandler(object sender, EventArgs e)
        {
            isTestGame = false;
            game.NewGame();
            board = game.GetBoard();
            captureBins = game.GetCaptureBins();
            moveListTextBox.Text = "";
            movingPieceBitmap = null;
            colourToMove = Colour.WHITE;
            humanColour = Colour.WHITE;
            isBoardFlipped = false;
            colourToMoveLabel.Text = colourToMove.ToString();
            humanColourLabel.Text = humanColour.ToString();
            lastMove.xfrom = -1;
            chessBoardPictureBox.Invalidate();
            whiteClock = new TimeSpan();
            blackClock = new TimeSpan();
            whiteClockLabel.Text = whiteClock.ToString();
            blackClockLabel.Text = blackClock.ToString();
            isGameOver = false;
            stopThinkingButton.Enabled = false;
            timer.Start();
        }
        /// <summary>
        /// Handler for the game->new game->black menu item
        /// </summary>
        private void NewGameBlackHandler(object sender, EventArgs e)
        {
            isTestGame = false;
            game.NewGame();
            board = game.GetBoard();
            captureBins = game.GetCaptureBins();
            moveListTextBox.Text = "";
            movingPieceBitmap = null;
            colourToMove = Colour.WHITE;
            humanColour = Colour.BLACK;
            colourToMoveLabel.Text = colourToMove.ToString();
            humanColourLabel.Text = humanColour.ToString();
            isBoardFlipped = true;
            lastMove.xfrom = -1;
            chessBoardPictureBox.Invalidate();
            mainMenuStrip.Enabled = false;
            whiteClock = new TimeSpan();
            blackClock = new TimeSpan();
            whiteClockLabel.Text = whiteClock.ToString();
            blackClockLabel.Text = blackClock.ToString();
            stopThinkingButton.Enabled = true;
            game.StartComputerThinking(colourToMove);
            isGameOver = false;
            timer.Start();
        }
        /// <summary>
        /// Handler for the board picture box size changed event
        /// </summary>
        private void ChessBoardSizeChangedHandler(object sender, EventArgs e)
        {
            chessBoardPictureBox.Invalidate();
        }

        private void ChessBoardMouseClickHandler(object sender, MouseEventArgs e)
        {
            if (colourToMove == (humanColour ^ Colour.XOR))
            {
                return;
            }
            if (e.Button != MouseButtons.Right)
            {
                return;
            }
            currentMove.xfrom = e.X;
            currentMove.yfrom = e.Y;
            contextMenuStrip.Show(chessBoardPictureBox, currentMove.xfrom, currentMove.yfrom);
        }

        private void EditSquare(Square square)
        {
            int x, y;
            GetSquareFromMousePosition(out x, out y, new MouseEventArgs(MouseButtons.None, 0, currentMove.xfrom, currentMove.yfrom, 0));
            game.EditSquare(square, x, y);
            board = game.GetBoard();
            captureBins = game.GetCaptureBins();
            moveListTextBox.Text = "Position edited by user";
            lastMove.xfrom = -1;
            chessBoardPictureBox.Invalidate();
        }
        /// <summary>
        /// Handlers for the various position editing context menu items
        /// </summary>
        private void HandlerSetSquareWhitePawn(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.WHITE, PieceType.PAWN));
        }
        private void HandlerSetSquareWhiteKnight(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.WHITE, PieceType.KNIGHT));
        }
        private void HandlerSetSquareWhiteBishop(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.WHITE, PieceType.BISHOP));
        }
        private void HandlerSetSquareWhiteRook(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.WHITE, PieceType.ROOK));
        }
        private void HandlerSetSquareWhiteQueen(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.WHITE, PieceType.QUEEN));
        }
        private void HandlerSetSquareWhiteKing(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.WHITE, PieceType.KING));
        }
        private void HandlerSetSquareBlackPawn(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.BLACK, PieceType.PAWN));
        }
        private void HandlerSetSquareBlackKnight(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.BLACK, PieceType.KNIGHT));
        }
        private void HandlerSetSquareBlackBishop(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.BLACK, PieceType.BISHOP));
        }
        private void HandlerSetSquareBlackRook(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.BLACK, PieceType.ROOK));
        }
        private void HandlerSetSquareBlackQueen(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.BLACK, PieceType.QUEEN));
        }
        private void HandlerSetSquareBlackKing(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.BLACK, PieceType.KING));
        }
        private void ClearSquareHandler(object sender, EventArgs e)
        {
            EditSquare(new Square(Colour.NONE, PieceType.NONE));
        }
        private void MenuHelpHandler(object sender, EventArgs e)
        {
            new HelpForm().ShowDialog();
        }

        private void ClearBoardHandler(object sender, EventArgs e)
        {
            for (int x = 0; x != 8; x++)
            {
                for (int y = 0; y != 8; y++)
                {
                    game.EditSquare(Square.EMPTY, x, y);
                }
            }
            board = game.GetBoard();
            captureBins = game.GetCaptureBins();
            moveListTextBox.Text = "Position edited by user";
            lastMove.xfrom = -1;
            chessBoardPictureBox.Invalidate();
        }

        private void TestGameHandler(object sender, EventArgs e)
        {
            NewGameBlackHandler(this, new EventArgs());
            isTestGame = true;
            humanColour = Colour.NONE;
            humanColourLabel.Text = humanColour.ToString();
            isBoardFlipped = false;
            chessBoardPictureBox.Invalidate();
        }

        private void StopThinkingButtonHandler(object sender, EventArgs e)
        {
            game.doCancelSearch = true;
        }

        private void SwitchSidesHandler(object sender, EventArgs e)
        {
            humanColour = humanColour ^ Colour.XOR;
            humanColourLabel.Text = humanColour.ToString();
            isBoardFlipped = !isBoardFlipped;
            chessBoardPictureBox.Invalidate();
            if (colourToMove == (humanColour ^ Colour.XOR) && !isGameOver)
            {
                game.StartComputerThinking(colourToMove);
            }
        }
    }
}