using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace StoopidChess
{
    /// <summary>
    /// Static class to generate moves. Also detects attacks and check, and determines 
    /// human's move type from source and destination square indeces.
    /// </summary>
    static class MoveGenerator
    {
        /// <summary>
        /// 0x88 format move vectors for each piece type
        /// </summary>
        public static readonly int[] PAWN_VECTORS_WHITE = {
            Board.Index( 0, 1),    // Move forward one
            Board.Index( 1, 1),    // Capture right
            Board.Index(-1, 1),    // Capture left
        };
        public static readonly int[] PAWN_VECTORS_BLACK = {
            Board.Index( 0,-1),     // Move forward one
            Board.Index( 1,-1),     // Capture right
            Board.Index(-1,-1),     // Capture left
        };
        private static readonly int[] KNIGHT_VECTORS = {
            Board.Index( 1, 2), 
            Board.Index( 1,-2), 
            Board.Index(-1, 2), 
            Board.Index(-1,-2), 
            Board.Index( 2, 1), 
            Board.Index( 2,-1), 
            Board.Index(-2, 1), 
            Board.Index(-2,-1) 
        };
        private static readonly int[] ROOK_VECTORS = {
            Board.Index( 1, 0), 
            Board.Index(-1, 0), 
            Board.Index( 0, 1), 
            Board.Index( 0,-1)  
        };
        private static readonly int[] BISHOP_VECTORS = {
            Board.Index( 1, 1), 
            Board.Index( 1,-1), 
            Board.Index(-1, 1), 
            Board.Index(-1,-1)
        };
        internal static readonly int[] KING_QUEEN_VECTORS = {
            Board.Index( 1, 0), 
            Board.Index(-1, 0), 
            Board.Index( 0, 1), 
            Board.Index( 0,-1),             
            Board.Index( 1, 1), 
            Board.Index( 1,-1), 
            Board.Index(-1, 1), 
            Board.Index(-1,-1)
        };
        private const int CHECK_TABLE_SIZE = 0x400000;
        private const int SHIFT_HASH = 42;
        /// <summary>
        /// Hash table for holding cached values of in-check status
        /// </summary>
        private static CheckHashEntry[] checkHashTable = new CheckHashEntry[CHECK_TABLE_SIZE];
        /// <summary>
        /// Determine if the colour specified is in check
        /// </summary>
        public static bool IsInCheck(Board board, Colour colour)
        {
            Game.debug.callsToInCheck++;
            ulong hash1 = board.GetHash1(colour);
            int tableIndex = (int)(hash1 >> SHIFT_HASH);
            CheckHashEntry check = checkHashTable[tableIndex];
            if (check != null && check.hash1 == hash1)
            {
                Game.debug.checkDictionaryMatches++;
                return check.isInCheck;
            }
            else
            {
                bool inCheck = IsSquareAttacked(board, colour ^ Colour.XOR, board.KingLocation(colour));
                checkHashTable[tableIndex] = new CheckHashEntry(hash1, inCheck);
                return inCheck;
            }
        }
        /// <summary>
        /// Generate all possible moves for the colour to play.
        /// Some of these moves may not be legal - they might leave the colour to play in check.
        /// </summary>
        public static SortedDictionary<int, Move> GenerateAllPseudoLegalMoves(Board board, Colour colour, bool doGenerateCastlingMoves)
        {
            SortedDictionary<int, Move> moves = new SortedDictionary<int, Move>();
            int[] PAWN_VECTORS = (colour == Colour.WHITE ? PAWN_VECTORS_WHITE : PAWN_VECTORS_BLACK);
            foreach (int location in board.LocationsOf(colour, PieceType.PAWN))
            {
                GeneratePawnMoves(board, colour, location, PAWN_VECTORS, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.KNIGHT))
            {
                GenerateKnightMoves(board, colour, location, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.BISHOP))
            {
                GenerateBishopMoves(board, colour, location, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.ROOK))
            {
                GenerateRookMoves(board, colour, location, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.QUEEN))
            {
                GenerateQueenMoves(board, colour, location, moves);
            }
            GenerateKingMoves(board, colour, board.KingLocation(colour), moves);
            if (doGenerateCastlingMoves)
            {
                GenerateCastlingMoves(board, colour, moves);
            }
            return moves;
        }
        /// <summary>
        /// Generate capture moves only. 
        /// Some of these moves may not be legal - they might leave the colour to play in check.
        /// </summary>
        public static SortedDictionary<int, Move> GeneratePseudoLegalCaptures(Board board, Colour colour)
        {
            SortedDictionary<int, Move> moves = new SortedDictionary<int, Move>();
            int[] PAWN_VECTORS = (colour == Colour.WHITE ? PAWN_VECTORS_WHITE : PAWN_VECTORS_BLACK);
            foreach (int location in board.LocationsOf(colour, PieceType.PAWN))
            {
                GeneratePawnCaptures(board, colour, location, PAWN_VECTORS, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.KNIGHT))
            {
                GenerateKnightCaptures(board, colour, location, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.BISHOP))
            {
                GenerateBishopCaptures(board, colour, location, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.ROOK))
            {
                GenerateRookCaptures(board, colour, location, moves);
            }
            foreach (int location in board.LocationsOf(colour, PieceType.QUEEN))
            {
                GenerateQueenCaptures(board, colour, location, moves);
            }
            GenerateKingCaptures(board, colour, board.KingLocation(colour), moves);
            return moves;
        }
        /// <summary>
        /// Generate bishop capture and non capture moves
        /// </summary>
        public static void GenerateBishopMoves(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.BISHOP));
            foreach (int vector in BISHOP_VECTORS)
            {
                for (int destLocation = sourceLocation + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                {
                    Square target = board[destLocation];
                    if (target == Square.EMPTY)
                    {
                        Move move = new Move(sourceLocation, destLocation, PieceType.BISHOP, MoveType.NORMAL, PieceType.NONE);
                        moves.Add(move.GetHashCode(), move);
                    }
                    else
                    {
                        if (target.colour != colour)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.BISHOP, MoveType.NORMAL, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Generate bishop capture moves only
        /// </summary>
        private static void GenerateBishopCaptures(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.BISHOP));
            foreach (int vector in BISHOP_VECTORS)
            {
                for (int destLocation = sourceLocation + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                {
                    Square target = board[destLocation];
                    if (target != Square.EMPTY)
                    {
                        if (target.colour != colour)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.BISHOP, MoveType.NORMAL, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Generate all rook moves
        /// </summary>
        private static void GenerateRookMoves(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.ROOK));
            foreach (int vector in ROOK_VECTORS)
            {
                for (int destLocation = sourceLocation + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                {
                    Square target = board[destLocation];
                    if (target == Square.EMPTY)
                    {
                        Move move = new Move(sourceLocation, destLocation, PieceType.ROOK, MoveType.ROOK, PieceType.NONE);
                        moves.Add(move.GetHashCode(), move);
                    }
                    else
                    {
                        if (target.colour != colour)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.ROOK, MoveType.ROOK, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Generate rook captures
        /// </summary>
        private static void GenerateRookCaptures(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.ROOK));
            foreach (int vector in ROOK_VECTORS)
            {
                for (int destLocation = sourceLocation + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                {
                    Square target = board[destLocation];
                    if (target != Square.EMPTY)
                    {
                        if (target.colour != colour)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.ROOK, MoveType.ROOK, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Generate all queen moves
        /// </summary>
        private static void GenerateQueenMoves(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.QUEEN));
            foreach (int vector in KING_QUEEN_VECTORS)
            {
                for (int destLocation = sourceLocation + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                {
                    Square target = board[destLocation];
                    if (target == Square.EMPTY)
                    {
                        Move move = new Move(sourceLocation, destLocation, PieceType.QUEEN, MoveType.NORMAL, PieceType.NONE);
                        moves.Add(move.GetHashCode(), move);
                    }
                    else
                    {
                        if (target.colour != colour)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.QUEEN, MoveType.NORMAL, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Generate queen captures
        /// </summary>
        private static void GenerateQueenCaptures(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.QUEEN));
            foreach (int vector in KING_QUEEN_VECTORS)
            {
                for (int destLocation = sourceLocation + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                {
                    Square target = board[destLocation];
                    if (target != Square.EMPTY)
                    {
                        if (target.colour != colour)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.QUEEN, MoveType.NORMAL, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Generate all knight moves
        /// </summary>
        private static void GenerateKnightMoves(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.KNIGHT));
            foreach (int vector in KNIGHT_VECTORS)
            {
                int destLocation = sourceLocation + vector;
                if ((destLocation & Board.OFF_BOARD) != 0)
                {
                    continue;
                }
                Square target = board[destLocation];
                if (target == Square.EMPTY)
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.KNIGHT, MoveType.NORMAL, PieceType.NONE);
                    moves.Add(move.GetHashCode(), move);
                }
                else if (target.colour != colour)
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.KNIGHT, MoveType.NORMAL, target.piece);
                    moves.Add(move.GetHashCode(), move);
                }
            }
        }
        /// <summary>
        /// Generate knight captures
        /// </summary>
        private static void GenerateKnightCaptures(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.KNIGHT));
            Colour enemyColour = colour ^ Colour.XOR;
            foreach (int vector in KNIGHT_VECTORS)
            {
                int destLocation = sourceLocation + vector;
                if ((destLocation & Board.OFF_BOARD) != 0)
                {
                    continue;
                }
                Square target = board[destLocation];
                if (target.colour == enemyColour)
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.KNIGHT, MoveType.NORMAL, target.piece);
                    moves.Add(move.GetHashCode(), move);
                }
            }
        }
        /// <summary>
        /// Generate all king moves
        /// </summary>
        private static void GenerateKingMoves(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.KING));
            foreach (int vector in KING_QUEEN_VECTORS)
            {
                int destLocation = sourceLocation + vector;
                if ((destLocation & Board.OFF_BOARD) != 0)
                {
                    continue;
                }
                Square target = board[destLocation];
                if (target == Square.EMPTY)
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.KING, MoveType.KING, PieceType.NONE);
                    moves.Add(move.GetHashCode(), move);
                }
                else if (target.colour != colour)
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.KING, MoveType.KING, target.piece);
                    moves.Add(move.GetHashCode(), move);
                }
            }
        }
        /// <summary>
        /// Generate king captures
        /// </summary>
        private static void GenerateKingCaptures(Board board, Colour colour, int sourceLocation, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.KING));
            Colour enemyColour = colour ^ Colour.XOR;
            foreach (int vector in KING_QUEEN_VECTORS)
            {
                int destLocation = sourceLocation + vector;
                if ((destLocation & Board.OFF_BOARD) != 0)
                {
                    continue;
                }
                Square target = board[destLocation];
                if (target.colour == enemyColour)
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.KING, MoveType.KING, target.piece);
                    moves.Add(move.GetHashCode(), move);
                }
            }
        }
        /// <summary>
        /// Generate all pawn moves: normal, en-passant (move forward two), captures and en-passant captures
        /// </summary>
        private static void GeneratePawnMoves(Board board, Colour colour, int sourceLocation, int[] PAWN_VECTORS, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.PAWN));
            Colour enemyColour = colour ^ Colour.XOR;
            int destLocation = sourceLocation + PAWN_VECTORS[0];
            int destRank = destLocation & Board.RANK;
            bool isPromotion = (destRank == 0x00 || destRank == 0x70);
            // No need to check for off board in moving forward one as a pawn will be promoted
            // before it gets an opportunity to walk off the back of the board
            if (board[destLocation] == Square.EMPTY)
            {
                if (isPromotion)
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_KNIGHT, PieceType.NONE);
                    moves.Add(move.GetHashCode(), move);
                    move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_QUEEN, PieceType.NONE);
                    moves.Add(move.GetHashCode(), move);
                }
                else
                {
                    Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.NORMAL, PieceType.NONE);
                    moves.Add(move.GetHashCode(), move);
                    int sourceRank = sourceLocation & Board.RANK;
                    if ((colour == Colour.WHITE && sourceRank == 0x10) || (colour == Colour.BLACK && sourceRank == 0x60))
                    {
                        destLocation += PAWN_VECTORS[0];
                        if (board[destLocation] == Square.EMPTY)
                        {
                            move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.EN_PASSANT, PieceType.NONE);
                            moves.Add(move.GetHashCode(), move);
                        }
                    }
                }
            }
            // Captures
            for (int i = 1; i != 3; i++)
            {
                destLocation = sourceLocation + PAWN_VECTORS[i];
                if ((destLocation & Board.OFF_BOARD) == 0)
                {
                    Square target = board[destLocation];
                    if (target.colour == enemyColour)
                    {
                        // Is this capture also a pawn promotion?
                        if (isPromotion)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_KNIGHT, target.piece);
                            moves.Add(move.GetHashCode(), move);
                            move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_QUEEN, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        else
                        {
                            // Regular capture
                            Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.NORMAL, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                    }
                    else if (board.enPassant == destLocation)
                    {
                        Debug.Assert(board[(sourceLocation & Board.RANK) + (destLocation & Board.FILE)] == new Square(enemyColour, PieceType.PAWN));
                        Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.EN_PASSANT_CAPTURE, PieceType.PAWN);
                        moves.Add(move.GetHashCode(), move);
                    }
                }
            }
        }
        /// <summary>
        /// Generate pawn captures
        /// </summary>
        private static void GeneratePawnCaptures(Board board, Colour colour, int sourceLocation, int[] PAWN_VECTORS, SortedDictionary<int, Move> moves)
        {
            Debug.Assert(board[sourceLocation] == new Square(colour, PieceType.PAWN));
            Colour enemyColour = colour ^ Colour.XOR;
            int destRank = (sourceLocation + PAWN_VECTORS[0]) & Board.RANK;
            bool isPromotion = (destRank == 0x00 || destRank == 0x70);
            for (int i = 1; i != 3; i++)    // Pawn capture vectors
            {
                int destLocation = sourceLocation + PAWN_VECTORS[i];
                if ((destLocation & Board.OFF_BOARD) == 0)
                {
                    Square target = board[destLocation];
                    if (target.colour == enemyColour)
                    {
                        // Is this capture also a pawn promotion?
                        if (isPromotion)
                        {
                            Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_KNIGHT, target.piece);
                            moves.Add(move.GetHashCode(), move);
                            move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.PAWN_PROMOTION_TO_QUEEN, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                        else
                        {
                            // Regular capture
                            Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.NORMAL, target.piece);
                            moves.Add(move.GetHashCode(), move);
                        }
                    }
                    else if (board.enPassant == destLocation)
                    {
                        Debug.Assert(board[(sourceLocation & Board.RANK) + (destLocation & Board.FILE)] == new Square(enemyColour, PieceType.PAWN));
                        Move move = new Move(sourceLocation, destLocation, PieceType.PAWN, MoveType.EN_PASSANT_CAPTURE, PieceType.PAWN);
                        moves.Add(move.GetHashCode(), move);
                    }
                }
            }
        }
        /// <summary>
        /// Castling is permitted if all of the following conditions are met:
        /// 
        /// a) The player has not already castled
        /// b) The king has not moved since the start of the game
        /// c) The target rook has not moved since the start of the game
        /// d) There are no occupied squares between the king and the target rook
        /// e) The king is not in check
        /// f) The king will not be in check after the move is made
        /// g) The square through which the king passes is not attacked by the opponent
        /// </summary>
        private static void GenerateCastlingMoves(Board board, Colour colour, SortedDictionary<int, Move> moves)
        {
            if (colour == Colour.WHITE)
            {
                // Castling possible on king side?
                if ((board.castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE) != CastlingFlags.NONE &&
                     board[0x07] == new Square(Colour.WHITE, PieceType.ROOK) &&
                     board[0x05] == Square.EMPTY && board[0x06] == Square.EMPTY)
                {
                    if (!IsInCheck(board, Colour.WHITE) &&
                        !IsSquareAttacked(board, Colour.BLACK, 0x05) &&
                        !IsSquareAttacked(board, Colour.BLACK, 0x06))
                    {
                        Move move = new Move(0x04, 0x06, PieceType.KING, MoveType.CASTLE_KING_SIDE_WHITE, PieceType.NONE);
                        moves.Add(move.GetHashCode(), move);
                    }
                }
                // Castling possible on queen side?
                if ((board.castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE) != CastlingFlags.NONE &&
                     board[0x00] == new Square(Colour.WHITE, PieceType.ROOK) &&
                     board[0x01] == Square.EMPTY && board[0x02] == Square.EMPTY && board[0x03] == Square.EMPTY)
                {
                    if (!IsInCheck(board, Colour.WHITE) &&
                        !IsSquareAttacked(board, Colour.BLACK, 0x02) &&
                        !IsSquareAttacked(board, Colour.BLACK, 0x03))
                    {
                        Move move = new Move(0x04, 0x02, PieceType.KING, MoveType.CASTLE_QUEEN_SIDE_WHITE, PieceType.NONE);
                        moves.Add(move.GetHashCode(), move);
                    }
                }
            }
            else
            {
                Debug.Assert(colour == Colour.BLACK);
                // Castling possible on king side?
                if ((board.castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE) != CastlingFlags.NONE &&
                     board[0x77] == new Square(Colour.BLACK, PieceType.ROOK) &&
                     board[0x75] == Square.EMPTY && board[0x76] == Square.EMPTY)
                {
                    if (!IsInCheck(board, Colour.BLACK) &&
                        !IsSquareAttacked(board, Colour.WHITE, 0x75) &&
                        !IsSquareAttacked(board, Colour.WHITE, 0x76))
                    {
                        Move move = new Move(0x74, 0x76, PieceType.KING, MoveType.CASTLE_KING_SIDE_BLACK, PieceType.NONE);
                        moves.Add(move.GetHashCode(), move);
                    }
                }
                // Castling possible on queen side?
                if ((board.castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE) != CastlingFlags.NONE &&
                     board[0x70] == new Square(Colour.BLACK, PieceType.ROOK) &&
                     board[0x71] == Square.EMPTY && board[0x72] == Square.EMPTY && board[0x73] == Square.EMPTY)
                {
                    if (!IsInCheck(board, Colour.BLACK) &&
                        !IsSquareAttacked(board, Colour.WHITE, 0x72) &&
                        !IsSquareAttacked(board, Colour.WHITE, 0x73))
                    {
                        Move move = new Move(0x74, 0x72, PieceType.KING, MoveType.CASTLE_QUEEN_SIDE_BLACK, PieceType.NONE);
                        moves.Add(move.GetHashCode(), move);
                    }
                }
            }
        }
        /// <summary>
        /// Generates all legal moves, i.e. those which do not leave the colour to move in check
        /// </summary>
        public static List<Move> GenerateStrictlyLegalMoves(Board board, Colour colour)
        {
            SortedDictionary<int, Move> moves = GenerateAllPseudoLegalMoves(board, colour, true);
            List<Move> legalMoves = new List<Move>();
            foreach (KeyValuePair<int, Move> pair in moves)
            {
                board.DoMove(pair.Value);
                if (!IsInCheck(board, colour))
                {
                    legalMoves.Add(pair.Value);
                }
                board.UndoLastMove();
            }
            return legalMoves;
        }
        /// <summary>
        /// Determines if a square is attacked
        /// </summary>
        public static bool IsSquareAttacked(Board board, Colour attackingColour, int location)
        {
            // Test for pawn attack
            // Swap black and white move vectors for pawn attack detection, as we are now 
            // considering moves from the attacked location; not the attacking location
            int[] PAWN_VECTORS = (attackingColour == Colour.WHITE ? PAWN_VECTORS_BLACK : PAWN_VECTORS_WHITE);
            int destLocation = location + PAWN_VECTORS[1];
            if ((destLocation & Board.OFF_BOARD) == 0)
            {
                Square target = board[destLocation];
                if (target.colour == attackingColour && target.piece == PieceType.PAWN)
                {
                    return true;
                }
            }
            destLocation = location + PAWN_VECTORS[2];
            if ((destLocation & Board.OFF_BOARD) == 0)
            {
                Square target = board[destLocation];
                if (target.colour == attackingColour && target.piece == PieceType.PAWN)
                {
                    return true;
                }
            }
            // Test for knight attack
            foreach (int knightLocation in board.LocationsOf(attackingColour, PieceType.KNIGHT))
            {
                if (Array.Exists(KNIGHT_VECTORS, delegate(int vector) { return vector == knightLocation - location; }))
                {
                    return true;
                }
            }
            // Test for rook or queen attack horizontally / vertically
            int queenCount = board.LocationsOf(attackingColour, PieceType.QUEEN).Count;
            if (queenCount != 0 || board.LocationsOf(attackingColour, PieceType.ROOK).Count != 0)
            {
                foreach (int vector in ROOK_VECTORS)
                {
                    for (destLocation = location + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                    {
                        Square target = board[destLocation];
                        if (target != Square.EMPTY)
                        {
                            if (target.colour == attackingColour && (target.piece == PieceType.ROOK || target.piece == PieceType.QUEEN))
                            {
                                return true;
                            }
                            break;
                        }
                    }
                }
            }
            // Test for bishop or queen attack diagonally
            if (queenCount != 0 || board.LocationsOf(attackingColour, PieceType.BISHOP).Count != 0)
            {
                foreach (int vector in BISHOP_VECTORS)
                {
                    for (destLocation = location + vector; (destLocation & Board.OFF_BOARD) == 0; destLocation += vector)
                    {
                        Square target = board[destLocation];
                        if (target != Square.EMPTY)
                        {
                            if (target.colour == attackingColour && (target.piece == PieceType.BISHOP || target.piece == PieceType.QUEEN))
                            {
                                return true;
                            }
                            break;
                        }
                    }
                }
            }
            // Test for king attack
            int kingLocation = board.KingLocation(attackingColour);
            if (Array.Exists(KING_QUEEN_VECTORS, delegate(int vector) { return vector == kingLocation - location; }))
            {
                return true;
            }
            // No attacker found
            return false;
        }

        public static bool IsMovePseudoLegal(Board board, Colour colour, Move move)
        {
            Square source = board[move.from];
            if (source.colour != colour)
            {
                return false;
            }
            SortedDictionary<int, Move> moves = new SortedDictionary<int, Move>();
            switch (source.piece)
            {
                case PieceType.PAWN:
                    GeneratePawnMoves(board, colour, move.from, colour == Colour.WHITE ? PAWN_VECTORS_WHITE : PAWN_VECTORS_BLACK, moves);
                    break;
                case PieceType.KNIGHT:
                    GenerateKnightMoves(board, colour, move.from, moves);
                    break;
                case PieceType.BISHOP:
                    GenerateBishopMoves(board, colour, move.from, moves);
                    break;
                case PieceType.ROOK:
                    GenerateRookMoves(board, colour, move.from, moves);
                    break;
                case PieceType.QUEEN:
                    GenerateQueenMoves(board, colour, move.from, moves);
                    break;
                case PieceType.KING:
                    GenerateKingMoves(board, colour, move.from, moves);
                    GenerateCastlingMoves(board, colour, moves);
                    break;
            }
            return (moves.ContainsKey(move.GetHashCode()));
        }
    }
}
