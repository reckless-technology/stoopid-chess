using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Text;

namespace StoopidChess
{
    /// <summary>
    /// Structure to contain variables for debug purposes
    /// Not used by the game algorithm
    /// </summary>
    struct GameDebugVariables
    {
        public int callsToAlphaBeta;
        public int alphaBetaCutoffs;
        public int bestDepthAchieved;
        public int computerScore;
        public int alwaysReplaceTranspositionHits;
        public int replaceDeeperTranspositionHits;
        public int transpositionsExact;
        public int transpositionsBeta;
        public int transpositionMoves;
        public int callsToQuiescentSearch;
        public int quiescentBetaCutoffs;
        public int callsToInCheck;
        public int checkDictionaryMatches;
        public int killersFound;
        public int callsToGetScore;
        public int pawnHashHits;
        public int doubledPawns;
        public int passedPawns;
        public int blockedPawns;
    }
    /// <summary>
    /// The Game class uses negamax with alpha-beta pruning and quiescence search extension to determine the best move.
    /// </summary>
    partial class Game : IChessEngine
    {
        /// <summary>
        /// Constants for use during the game
        /// </summary>
        private const int ALPHA = int.MinValue + 1;             // alpha initial value = -infinity    
        private const int BETA = -ALPHA;                        // beta initial value  = +infinity
        private const int TRANSPOSITION_TABLE_SIZE = 0x100000;  // Use ~100MB RAM for hash tables
        private const int SHIFT_HASH = 44;                      // hash >> SHIFT_HASH == transposition table index
        /// <summary>
        /// Instance variables
        /// </summary>
        private Board board;                                // The chess board used by the Game
        internal bool doCancelSearch;                       // Set true to cancel the recursive search
        private System.Timers.Timer timer;                  // Timer to control how long the computer thinking thread runs
        private Transposition[] alwaysReplaceHashTable;     // Hash table - always replace strategy
        private Transposition[] replaceByDepthHashTable;    // Hash table - maximum depth strategy      
        private int maxSearchDepth;                         // Maximum search depth for alpha-beta phase of search
        private Thread worker;                              // Run alpha beta search on worker thread
        private Move[] killerMoves;                         // Indexed by [depthFromRoot]
        public event ComputerThinkingCompletedHandler ComputerThinkingCompletedEvent; 
        public static GameDebugVariables debug;             // Debug variables
        public string reasonForDraw;
        /// <summary>
        /// Public constructor
        /// </summary>
        public Game()
        {
            board = new Board();
            doCancelSearch = false;
            timer = new System.Timers.Timer(5000);
            maxSearchDepth = 5;
            timer.AutoReset = false;
            timer.Elapsed += new ElapsedEventHandler(ComputerMoveTimeElapsed);
            alwaysReplaceHashTable = new Transposition[TRANSPOSITION_TABLE_SIZE];
            replaceByDepthHashTable = new Transposition[TRANSPOSITION_TABLE_SIZE];
            killerMoves = new Move[1024];   // depthFromRoot should NEVER get this big or we will have an out of bounds exception
            for (int i = 0; i != 1024; i++)
            {
                killerMoves[i] = Move.NONE;
            }
        }

        public void NewGame()
        {
            board = new Board();
        }
        /// <summary>
        /// Set the limit on the computer's thinking time for a move
        /// </summary>
        public void SetMaxThinkingTime(int milliseconds)
        {
            timer.Interval = milliseconds;
        }
        /// <summary>
        /// Set the limit on the search depth
        /// </summary>
        public void SetMaxDepth(int depth)
        {
            maxSearchDepth = depth;
        }
        /// <summary>
        /// Get a string containing all the moves made so 
        /// far in the game, in alphanumeric notation
        /// </summary>
        public string GetMoveHistory()
        {
            List<Move> moves = board.GetMoveHistory();
            StringBuilder result = new StringBuilder();
            for (int i = 0; i != moves.Count; i++)
            {
                if ((i & 1) == 0)
                {
                    result.Append(String.Format("{0,2:D}", (i >> 1) + 1));
                    result.Append(".\t");
                }
                result.Append(String.Format("{0,-9}", moves[i].ToString()));
                if ((i & 1) != 0)
                {
                    result.Append(Environment.NewLine);
                }
                else
                {
                    result.Append('\t');
                }
            }
            return result.ToString();
        }
        /// <summary>
        /// Retrieve lists of captured pieces, in separate bins for white and black
        /// </summary>
        public List<Square>[] GetCaptureBins()
        {
            List<Square>[] captureBins = new List<Square>[3];
            captureBins[(int)Colour.WHITE] = new List<Square>();
            captureBins[(int)Colour.BLACK] = new List<Square>();
            Stack<Square> captures = board.GetCaptureStack();
            while (captures.Count != 0)
            {
                Square square = captures.Pop();
                if (square.colour == Colour.WHITE)
                {
                    captureBins[(int)Colour.WHITE].Add(square);
                }
                else if (square.colour == Colour.BLACK)
                {
                    captureBins[(int)Colour.BLACK].Add(square);
                }
            }
            return captureBins;
        }
        /// <summary>
        /// Text version of the board state for console / debugging use
        /// </summary>
        public override string ToString()
        {
            return board.ToString();
        }
        /// <summary>
        /// Get an 8 x 8 array containing the board
        /// [x, y] where [0, 0] is bottom left with respect to white (square a1)
        /// </summary>
        public Square[,] GetBoard()
        {
            Square[,] result = new Square[8, 8];
            for (int x = 0; x != 8; x++)
            {
                for (int y = 0; y != 8; y++)
                {
                    result[x, y] = board[Board.Index(x, y)];
                }
            }
            return result;
        }
        /// <summary>
        /// Save the game to a generic stream
        /// </summary>
        public void Serialize(Stream stream)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            // Use isBlackToMoveHash1 as magic number (signature value) to identify Stoopid Chess game
            formatter.Serialize(stream, ZobristHash.isBlackToMoveHash1);
            formatter.Serialize(stream, board);
        }
        /// <summary>
        /// Load the game from a generic stream
        /// </summary>
        public IChessEngine Deserialize(Stream stream)
        {
            try
            {
                Game game = new Game();
                BinaryFormatter formatter = new BinaryFormatter();
                ulong signature = (ulong)formatter.Deserialize(stream);
                if (signature != ZobristHash.isBlackToMoveHash1)
                {
                    // Incorrect file type - signature mismatch
                    return null;
                }
                game.board = formatter.Deserialize(stream) as Board;
                return game;
            }
            catch (Exception)
            {
                // Something went wrong: we were unable to 
                // deserialize the game so simply return null
                return null;
            }
        }
        /// <summary>
        /// Used for positional editing by the UI
        /// </summary>
        public void EditSquare(Square square, int x, int y)
        {
            if (board[Board.Index(x, y)] != square)
            {
                board.EditSquare(square, x, y);
            }
        }
        /// <summary>
        /// Used to check legality of position following user edit
        /// Each side must have strictly one king on the board for play to resume
        /// </summary>
        public bool IsPositionLegal()
        {
            if (board.LocationsOf(Colour.WHITE, PieceType.KING).Count == 1 &&
                board.LocationsOf(Colour.BLACK, PieceType.KING).Count == 1)
            {
                // Each side has precisely one king
                // Test that they are not checking one another
                int whiteKingLocation = board.KingLocation(Colour.WHITE);
                int blackKingLocation = board.KingLocation(Colour.BLACK);
                if (Array.Exists(MoveGenerator.KING_QUEEN_VECTORS, delegate(int vector) { return vector == whiteKingLocation - blackKingLocation; }))
                {
                    return false;
                }
                return true;
            }
            return false;
        }
        /// <summary>
        /// Check if a move is legal for colour to play.
        /// Checks legality of piece movement, en passant capture, castling legality,
        /// and disallows a move which would place the colour to play in check
        /// </summary>
        public bool IsMoveLegal(Move move, Colour colour)
        {
            return MoveGenerator.GenerateStrictlyLegalMoves(board, colour).Contains(move);
        }
        /// <summary>
        /// Make the move
        /// </summary>
        public void DoMove(Move move)
        {
            Debug.Assert(move.moveType != MoveType.NONE);
            board.DoMove(move);
        }
        /// <summary>
        /// Unmake the last move
        /// </summary>
        /// <returns>false if there was no move to unmake</returns>
        public bool UndoLastMove()
        {
            return board.UndoLastMove();
        }
        /// <summary>
        /// Determines if there is a legal move available which does not result in check
        /// </summary>
        /// <returns>true if colour is unable to avoid check on their next move</returns>
        private bool CannotAvoidCheck(Colour colour)
        {
            return MoveGenerator.GenerateStrictlyLegalMoves(board, colour).Count == 0;
        }

        public bool IsInCheck(Colour colour)
        {
            return (MoveGenerator.IsInCheck(board, colour));
        }

        public bool IsCheckmated(Colour colour)
        {
            // Checkmate occurs when the colour to play is in check and is unable
            // to make a legal move out of check
            return (IsInCheck(colour) && CannotAvoidCheck(colour));
        }

        public bool IsDrawn(Colour colour)
        {
            // Stalemate occurs when the colour to play is not in check and is
            // unable to make a legal move which would not result in check
            if (!IsInCheck(colour) && CannotAvoidCheck(colour))
            {
                reasonForDraw = "Stalemate";
                return true;
            }
            // Check for draw by repetition 3 times
            if (board.IsDrawByRepetition(colour))
            {
                reasonForDraw = "Draw by repetition 3 times";
                return true;
            }
            // Check for draw by insufficient material
            if (board.IsDrawByInsufficientMaterial())
            {
                reasonForDraw = "Draw by insufficient material";
                return true;
            }
            // Check for draw by 50 move rule
            if (board.IsDrawByFiftyMoveRule())
            {
                reasonForDraw = "Draw by 50 moves rule";
                return true;
            }
            return false;
        }
        /// <summary>
        /// Start a worker thread to determine the computer move and return immediately on the calling thread
        /// </summary>
        public void StartComputerThinking(Colour colour)
        {
            worker = new Thread(new ParameterizedThreadStart(GetComputerMove));
            worker.Name = "Computer worker thread";
            worker.Start(colour);
        }
        /// <summary>
        /// Delegate to cancel search when the time up timer ticks
        /// </summary>
        private void ComputerMoveTimeElapsed(object source, ElapsedEventArgs e)
        {
#if DEBUG
            Debug.Assert(doCancelSearch == false);
#else
            doCancelSearch = true;
#endif
        }
        /// <summary>
        /// Determine the Move type from the source and destination square indeces
        /// Used to generate human moves
        /// </summary>
        public Move DetermineMove(MoveFromTo coords, Colour colour, PawnPromotionCallback callback)
        {
            PieceType promotedPiece = PieceType.NONE;
            int from = Board.Index(coords.xfrom, coords.yfrom);
            int to = Board.Index(coords.xto, coords.yto);
            // Is this a pawn promotion move?
            if (board[from].piece == PieceType.PAWN && (coords.yto == 0 || coords.yto == 7))
            {
                // Check legality of pawn promotion before consulting user
                if (IsMoveLegal(new Move(from, to, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_QUEEN, board[to].piece), colour))
                {
                    // Ask the user if they wish to promote to a queen or a knight
                    promotedPiece = callback(colour);
                    if (promotedPiece == PieceType.QUEEN)
                    {
                        return new Move(from, to, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_QUEEN, board[to].piece);
                    }
                    else
                    {
                        return new Move(from, to, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_KNIGHT, board[to].piece);
                    }
                }
            }
            // Is this a castling move?
            else if (board[from].piece == PieceType.KING && coords.xfrom == 4 && (coords.xto == 2 || coords.xto == 6))
            {
                if (coords.xto == 6 && colour == Colour.WHITE)
                {
                    return new Move(from, to, PieceType.KING, MoveType.CASTLE_KING_SIDE_WHITE, PieceType.NONE);
                }
                if (coords.xto == 6 && colour == Colour.BLACK)
                {
                    return new Move(from, to, PieceType.KING, MoveType.CASTLE_KING_SIDE_BLACK, PieceType.NONE);
                }
                if (coords.xto == 2 && colour == Colour.WHITE)
                {
                    return new Move(from, to, PieceType.KING, MoveType.CASTLE_QUEEN_SIDE_WHITE, PieceType.NONE);
                }
                if (coords.xto == 2 && colour == Colour.BLACK)
                {
                    return new Move(from, to, PieceType.KING, MoveType.CASTLE_QUEEN_SIDE_BLACK, PieceType.NONE);
                }
            }
            // Is this an en passant pawn move?
            else if (board[from].piece == PieceType.PAWN && Math.Abs(coords.yto - coords.yfrom) == 2)
            {
                return new Move(from, to, PieceType.PAWN, MoveType.EN_PASSANT, PieceType.NONE);
            }
            // Is this an en passant capture move?
            else if (board[from].piece == PieceType.PAWN && Math.Abs(coords.xto - coords.xfrom) == 1 && board[to].piece == PieceType.NONE)
            {
                return new Move(from, to, PieceType.PAWN, MoveType.EN_PASSANT_CAPTURE, PieceType.PAWN);
            }
            // Is this a king move?
            else if (board[from].piece == PieceType.KING)
            {
                return new Move(from, to, PieceType.KING, MoveType.KING, board[to].piece);
            }
            // Is this a rook move?
            else if (board[from].piece == PieceType.ROOK)
            {
                return new Move(from, to, PieceType.ROOK, MoveType.ROOK, board[to].piece);
            }
            // This is a normal move
            return new Move(from, to, board[from].piece, MoveType.NORMAL, board[to].piece);
        }
    }
}
