using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
namespace StoopidChess
{
    /// <summary>
    /// Holds the chess board as an array of Squares in "0x88 format"
    /// Refer to http://en.wikipedia.org/wiki/Board_representation_%28chess%29#0x88_method for details
    /// 
    /// The board is arranged as an array of 128 rather than 64 squares
    /// The 'right hand' half of the board is unused
    /// 
    /// The lower nibble (bits 0,1,2) of the board index is the file (0 = "a", 7 = "h")
    /// The upper nibble (bits 4,5,6) of the board index is the rank (0 = "1", 7 = "8")
    /// 
    /// This has several advantages:
    /// 
    /// a) Delta between any two square indeces is unique with respect to both distance AND direction (i.e. vectors)
    /// b) Bits 3 and 7 are an 'off the board' flag
    /// c) Rapid attack detection is possible
    /// d) In the source code you can easily identify a square by using 0xYX where
    ///    Y is the rank and X is the file (0x00 is bottom left from white's perspective)
    /// 
    /// The Board class:
    /// 
    /// 1) Maintains lists of square occupancy by piece type and colour
    /// 2) Provides indexers for public access to the board
    /// 3) Maintains castling and en passant capture rights status
    /// 4) Maintains two independent incremental Zobrist hash values of position, 
    ///    which are used by the transposition tables
    /// 5) Implements stacks to enable multiple levels of move do & undo 
    ///    for use during recursive search (and game rewind)
    /// 6) Maintains an incremental score for the position, 
    ///    based on piece / square tables
    /// 7) Maintains a separate pawn hash, which is used for pawn evaluation caching
    /// </summary>
    partial class Board
    {
        /// <summary>
        /// Constants used for board manipulation
        /// </summary>         
        public const int RANK = 0xF0;
        public const int FILE = 0x0F;
        public const int OFF_BOARD = 0x88;
        public const int NO_EN_PASSANT = 0x08;  // Off the board
        /// <summary>
        /// Instance variables
        /// </summary>
        private Square[] theBoard;              // Do not access directly; use accessors
        private List<int>[,] pieceLocations;
        private Stack<CastlingFlags> flagStack;
        private Stack<int> enPassantStack;
        private Stack<Square> captureStack;
        private List<Move> moveStack;
        private CastlingFlags theCastlingFlags; // Do not access directly; use accessors
        private List<ulong> positionStack;
        private int theEnPassant;               // Do not access directly; use accessors
        private ulong theHash1;
        private ulong theHash2;
        private ulong thePawnHash;
        private int theScore;
        private int thePieceCount;
        /// <summary>
        /// Constructor - returns board set up for start of game
        /// </summary>
        public Board()
        {
            theHash1 = 0;
            theHash2 = 0;
            theScore = 0;
            thePawnHash = 0;
            thePieceCount = 0;
            theBoard = new Square[128];
            for (int i = 0; i != theBoard.Length; i++)
            {
                theBoard[i] = Square.EMPTY;
            }
            flagStack = new Stack<CastlingFlags>(256);
            enPassantStack = new Stack<int>(256);
            captureStack = new Stack<Square>(256);
            moveStack = new List<Move>(256);
            positionStack = new List<ulong>(256);
            pieceLocations = new List<int>[3, 7];
            foreach (Colour colour in new Colour[] { Colour.WHITE, Colour.BLACK })
            {
                foreach (PieceType piece in new PieceType[] 
                {PieceType.PAWN, PieceType.KNIGHT, PieceType.BISHOP, 
                 PieceType.ROOK, PieceType.QUEEN, PieceType.KING})
                {
                    pieceLocations[(int)colour, (int)piece] = new List<int>(8);
                }
            }
            // Set the board up for the start of a game
            SetStartingPosition();
            Debug.Assert(theScore == 0);
            Debug.Assert(thePieceCount == 32);
        }
        /// <summary>
        /// How many times has this position occurred so far
        /// either in the game history or the search tree.
        /// Used for draw by repetition detection
        /// </summary>
        public bool IsDrawByRepetition(Colour colourToMove)
        {
            ulong hash = GetHash1(colourToMove);
            int matches = 0;
            positionStack.ForEach(delegate(ulong previous) { if (previous == hash) matches++; });
            return matches >= 2;
        }
        /// <summary>
        /// Determine if we are in the end game
        /// </summary>
        /// <returns></returns>
        public bool IsEndGame()
        {
            int whiteQueens = LocationsOf(Colour.WHITE, PieceType.QUEEN).Count;
            int blackQueens = LocationsOf(Colour.BLACK, PieceType.QUEEN).Count;
            if (whiteQueens + blackQueens == 0)
            {
                return true;
            }
            if (whiteQueens >= 1)
            {
                if (LocationsOf(Colour.WHITE, PieceType.KNIGHT).Count + LocationsOf(Colour.WHITE, PieceType.BISHOP).Count +
                    LocationsOf(Colour.WHITE, PieceType.ROOK).Count > 1)
                {
                    return false;
                }
            }
            if (blackQueens >= 1)
            {
                if (LocationsOf(Colour.BLACK, PieceType.KNIGHT).Count + LocationsOf(Colour.BLACK, PieceType.BISHOP).Count +
                    LocationsOf(Colour.BLACK, PieceType.ROOK).Count > 1)
                {
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// Get a complete list of moves since the start of the game
        /// </summary>
        /// <returns></returns>
        public List<Move> GetMoveHistory()
        {
            return new List<Move>(moveStack);
        }
        /// <summary>
        /// Readonly accessor for pieceCount
        /// </summary>
        public int pieceCount
        {
            get
            {
                return thePieceCount;
            }
        }
        /// <summary>
        /// Accessor for castling rights
        /// NB: DO NOT access theCastlingFlags directly outside this accessor or the hash values
        /// will become corrupted
        /// </summary>
        public CastlingFlags castlingFlags
        {
            get
            {
                return theCastlingFlags;
            }
            set
            {
                // Mask out the old flags from the hashes
                theHash1 ^= ZobristHash.GetHashValueForCastlingRights1(theCastlingFlags);
                theHash2 ^= ZobristHash.GetHashValueForCastlingRights2(theCastlingFlags);
                // Assign the value
                theCastlingFlags = value;
                // Mask in the new value into the hashes
                theHash1 ^= ZobristHash.GetHashValueForCastlingRights1(value);
                theHash2 ^= ZobristHash.GetHashValueForCastlingRights2(value);
            }
        }
        /// <summary>
        /// Accessor for en passant square
        /// NB: DO NOT access theEnPassant directly outside this accessor or the hash values
        /// will become corrupted
        /// </summary>
        public int enPassant
        {
            get
            {
                return theEnPassant;
            }
            set
            {
                // Mask out the old value from the hashes
                theHash1 ^= ZobristHash.GetHashValueForEnPassantLocation1(theEnPassant);
                theHash2 ^= ZobristHash.GetHashValueForEnPassantLocation2(theEnPassant);
                theEnPassant = value;
                // Mask in new value to board hashes
                theHash1 ^= ZobristHash.GetHashValueForEnPassantLocation1(value);
                theHash2 ^= ZobristHash.GetHashValueForEnPassantLocation2(value);
            }
        }
        /// <summary>
        /// Indexer to allow public access to squares on the board.
        /// Set accessor also maintains the lists of piece locations by type and colour
        /// and incrementally updates the board hash values and positional score
        /// </summary>
        public Square this[int location]
        {
            get
            {
                return theBoard[location];
            }
            set
            {
                Square previous = theBoard[location];
                if (previous.piece != PieceType.NONE)
                {
                    // Remove existing piece from appropriate piece locations list
                    pieceLocations[(int)previous.colour, (int)previous.piece].Remove(location);
                    // Subtract existing piece square value from score
                    theScore -= Evaluator.GetPieceSquareValue(previous, location);
                    // Mask out the existing piece from the board hashes
                    theHash1 ^= ZobristHash.GetHashValueForSquare1(previous, location);
                    theHash2 ^= ZobristHash.GetHashValueForSquare2(previous, location);
                    thePieceCount--;
                    if (previous.piece == PieceType.PAWN)
                    {
                        // Mask out previous pawn from pawn hash
                        thePawnHash ^= ZobristHash.GetHashValueForSquare1(previous, location);
                    }
                }
                // Assign the new value
                theBoard[location] = value;
                if (value.piece != PieceType.NONE)
                {
                    // Add new piece to locations list
                    pieceLocations[(int)value.colour, (int)value.piece].Add(location);
                    // Add piece square value to score
                    theScore += Evaluator.GetPieceSquareValue(value, location);
                    // Mask in the new piece to the board hashes
                    theHash1 ^= ZobristHash.GetHashValueForSquare1(value, location);
                    theHash2 ^= ZobristHash.GetHashValueForSquare2(value, location);
                    thePieceCount++;
                    if (value.piece == PieceType.PAWN)
                    {
                        // Mask in new pawn into pawn hash
                        thePawnHash ^= ZobristHash.GetHashValueForSquare1(value, location);
                    }
                }
            }
        }
        /// <summary>
        /// Read only accessor for piece / square board value
        /// </summary>
        public int pieceSquareScore
        {
            get
            {
                return theScore;
            }
        }
        /// <summary>
        /// Read only accessor for pawn hash
        /// </summary>
        public ulong pawnHash
        {
            get
            {
                return thePawnHash;
            }
        }
        /// <summary>
        /// Insufficient material (draw) is one of:
        /// a) king vs king
        /// b) king and bishop vs king
        /// c) king and knight vs king
        /// d) king and bishop vs king and bishop (bishops on same colour)
        /// </summary>
        /// <returns></returns>
        public bool IsDrawByInsufficientMaterial()
        {
            if (thePieceCount == 2)
            {
                // king vs king
                return true;
            }
            else if (thePieceCount == 3)
            {
                if (LocationsOf(Colour.WHITE, PieceType.BISHOP).Count == 1 || LocationsOf(Colour.BLACK, PieceType.BISHOP).Count == 1)
                {
                    // king and bishop vs king
                    return true;
                }
                if (LocationsOf(Colour.WHITE, PieceType.KNIGHT).Count == 1 || LocationsOf(Colour.BLACK, PieceType.KNIGHT).Count == 1)
                {
                    // king and knight vs king
                    return true;
                }
            }
            else if (thePieceCount == 4)
            {
                if (LocationsOf(Colour.WHITE, PieceType.BISHOP).Count == 1 && LocationsOf(Colour.BLACK, PieceType.BISHOP).Count == 1)
                {
                    // king and bishop vs king and bishop
                    // Are bishops on the same colour?
                    if (SquareColour(LocationsOf(Colour.WHITE, PieceType.BISHOP)[0]) == SquareColour(LocationsOf(Colour.BLACK, PieceType.BISHOP)[0]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        /// <summary>
        /// Determine if this is a draw by 50 move rule:
        /// a) No capture has been made 
        /// b) No pawn has moved
        ///    ... in the past 50 moves
        /// </summary>
        public bool IsDrawByFiftyMoveRule()
        {
            if (moveStack.Count < 50)
            {
                return false;
            }
            int offset = moveStack.Count - 1;
            for (int i = 0; i != 50; i++)
            {
                Move move = moveStack[offset - i];
                if (move.capturedPiece != PieceType.NONE || move.movingPiece == PieceType.PAWN)
                {
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// Return the colour of a square indexed in 0x88 format
        /// </summary>
        private Colour SquareColour(int location)
        {
            int rank = (location & RANK) >> 4;
            int file = location & FILE;
            return (((rank + file) & 1) == 0) ? Colour.BLACK : Colour.WHITE;
        }
        /// <summary>
        /// Get the primary Zobrist hash value for this board position.
        /// Hash value includes piece locations, castling rights flags, en passant location
        /// and whose turn it is to move
        /// </summary>
        public ulong GetHash1(Colour colourToMove)
        {
            return (colourToMove == Colour.WHITE ? theHash1 : theHash1 ^ ZobristHash.isBlackToMoveHash1);
        }
        /// <summary>
        /// Get the secondary Zobrist hash value for this board position.
        /// Secondary hash uses an alternate 64 bit LCG to synthesize Zobrist keys 
        /// thus minimizes the risk of hash collision. 
        /// </summary>
        public ulong GetHash2(Colour colourToMove)
        {
            return (colourToMove == Colour.WHITE ? theHash2 : theHash2 ^ ZobristHash.isBlackToMoveHash2);
        }
        /// <summary>
        /// Retrieve the locations on the board of a piece type and colour
        /// </summary>
        public List<int> LocationsOf(Colour colour, PieceType piece)
        {
            return pieceLocations[(int)colour, (int)piece];
        }
        /// <summary>
        /// Return the location of the king
        /// </summary>
        public int KingLocation(Colour colour)
        {
            Debug.Assert(pieceLocations[(int)colour, (int)PieceType.KING].Count == 1);
            return pieceLocations[(int)colour, (int)PieceType.KING][0];
        }
        /// <summary>
        /// Return a copy of the capture stack (used when creating capture bins)
        /// NB Stack enumerator reverses stack order during copy
        /// </summary>
        public Stack<Square> GetCaptureStack()
        {
            return new Stack<Square>(captureStack);
        }
        /// <summary>
        /// Convert x,y co-ordinates into 0x88 board index value
        /// Used for both absolute locations and vector moves (deltas)
        /// </summary>
        /// <param name="x">file on the board (0 = "a", 7 = "h")</param>
        /// <param name="y">rank on the board (0 = "1", 7 = "8")</param>
        /// <returns>index into board array in 0x88 format</returns>
        public static int Index(int x, int y)
        {
            return x + (y << 4);
        }
        /// <summary>
        /// Place a new piece onto the board
        /// </summary>
        private void PlacePiece(int x, int y, Colour colour, PieceType piece)
        {
            // Use the indexer to ensure that hash, piece locations 
            // lists and positional score are correctly updated 
            // following placement of the new piece
            this[Index(x, y)] = new Square(colour, piece);
        }
        /// <summary>
        /// Used by the UI for positional editing
        /// </summary>
        public void EditSquare(Square square, int x, int y)
        {
            this[Index(x, y)] = square;
            // Determine new castling rights
            if (this[0x04] != Square.WHITE_KING)
            {
                castlingFlags &= ~(CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE | CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE);
            }
            if (this[0x07] != Square.WHITE_ROOK)
            {
                castlingFlags &= ~CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE;
            }
            if (this[0x00] != Square.WHITE_ROOK)
            {
                castlingFlags &= ~CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE;
            }
            if (this[0x74] != Square.BLACK_KING)
            {
                castlingFlags &= ~(CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE | CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE);
            }
            if (this[0x77] != Square.BLACK_ROOK)
            {
                castlingFlags &= ~CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE;
            }
            if (this[0x70] != Square.BLACK_ROOK)
            {
                castlingFlags &= ~CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE;
            }
            // Reset en passant location
            enPassant = NO_EN_PASSANT;
            // Prevent further move undo following positional edit
            moveStack.Clear();
        }
        /// <summary>
        /// Set up the chess board for the start of a game.
        /// </summary>
        private void SetStartingPosition()
        {
            // Add the pawns
            for (int x = 0; x != 8; x++)
            {
                PlacePiece(x, 1, Colour.WHITE, PieceType.PAWN);
                PlacePiece(x, 6, Colour.BLACK, PieceType.PAWN);
            }
            // Add the rooks
            foreach (int x in new int[] { 0, 7 })
            {
                PlacePiece(x, 0, Colour.WHITE, PieceType.ROOK);
                PlacePiece(x, 7, Colour.BLACK, PieceType.ROOK);
            }
            // Add the knights
            foreach (int x in new int[] { 1, 6 })
            {
                PlacePiece(x, 0, Colour.WHITE, PieceType.KNIGHT);
                PlacePiece(x, 7, Colour.BLACK, PieceType.KNIGHT);
            }
            // Add the bishops
            foreach (int x in new int[] { 2, 5 })
            {
                PlacePiece(x, 0, Colour.WHITE, PieceType.BISHOP);
                PlacePiece(x, 7, Colour.BLACK, PieceType.BISHOP);
            }
            // Add the queens
            PlacePiece(3, 0, Colour.WHITE, PieceType.QUEEN);
            PlacePiece(3, 7, Colour.BLACK, PieceType.QUEEN);
            // Add the kings
            PlacePiece(4, 0, Colour.WHITE, PieceType.KING);
            PlacePiece(4, 7, Colour.BLACK, PieceType.KING);
            // Enable all castling rights bits and reset en passant square
            castlingFlags = CastlingFlags.ALL_CASTLE_RIGHTS;
            enPassant = NO_EN_PASSANT;
        }
        /// <summary>
        /// Textual representation of the board for console use.
        /// Capital letters for white pieces
        /// Lower case letters for black pieces
        /// </summary>
        /// <returns>board string</returns>
        public override string ToString()
        {
            StringBuilder result = new StringBuilder();
            for (int y = 7; y >= 0; y--)
            {
                result.Append((y + 1).ToString() + " ");
                for (int x = 0; x != 8; x++)
                {
                    result.Append(this[Index(x, y)].ToString());
                }
                result.Append(Environment.NewLine);
            }
            result.Append(Environment.NewLine + "  abcdefgh");
            return result.ToString();
        }
    }
}
