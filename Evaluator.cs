using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace StoopidChess
{
    /// <summary>
    /// Static class to determine the heuristic value of a chess position
    /// Used by the Board class to determine a score for leaf nodes in the search tree
    /// This is a simple evaluator which only takes material and location into consideration
    /// Further work to refine the heuristic is required...
    /// </summary>
    static class Evaluator
    {   
        /// <summary>
        /// Material value. Chosen to satisfy:
        /// B > N > 3P
        /// B + N = R + 2P
        /// Q + P = 2R
        /// 
        /// then normalised a pawn to 100
        /// </summary>
        public const int VALUE_PAWN = 100;
        public const int VALUE_KNIGHT = 330;
        public const int VALUE_BISHOP = 370;
        public const int VALUE_ROOK = 500;
        public const int VALUE_QUEEN = 900;
        public const int VALUE_KING = 123456;
        // Terminal node values
        public const int LOSING_SCORE = -VALUE_KING;
        // Contempt for a draw
        public const int DRAW_SCORE = -200;
        // Evaluator bonus values
        public const int PASSED_PAWN_BONUS = 50;
        public const int DEFENDED_PAWN_BONUS = 10;
        public const int BISHOP_MOBILITY_BONUS = 2; // Per available move - avoid trapped bishops
        public const int DOUBLED_PAWN_PENALTY = -12;
        public const int BLOCKED_PAWN_PENALTY = -15;
        // Piece-square tables - transformed into 0x88 format
        // in the static constructor before use during the game.
        // Bottom (last) row of each array is rank "1"
        // Top (first) row is rank "8"
        // Positions and values are with respect to white.
        // These scores are added to material value to determine the 
        // heuristic score of having a piece P located on a square S.
        private static readonly int[] PAWNS = {
             0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
             5,  5, 10, 25, 25, 10,  5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5, -5,-10,  0,  0,-10, -5,  5,
             5, 10, 10,-20,-20, 10, 10,  5,
             0,  0,  0,  0,  0,  0,  0,  0
        };
        private static readonly int[] KNIGHTS = {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
        };
        private static readonly int[] BISHOPS = {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20,
        };
        private static readonly int[] ROOKS = {
              0,  0,  0,  0,  0,  0,  0,  0,
              5, 10, 10, 10, 10, 10, 10,  5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
             -5,  0,  0,  0,  0,  0,  0, -5,
              0,  0,  0,  5,  5,  0,  0,  0
        };
        private static readonly int[] QUEENS = {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
             -5,  0,  5,  5,  5,  5,  0, -5,
              0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20
        };
        // Piece-square values for king during the opening and mid-game
        // The king should be encouraged to remain safely in the back rank
        // during the opening and mid-game
        private static readonly int[] KING = {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
             20, 20,  0,  0,  0,  0, 20, 20,
             20, 30, 10,  0,  0, 10, 30, 20
        };
        // Piece-square values for king during the end-game
        // The king should be encouraged to move towards the center of 
        // the board in the end-game to avoid becoming trapped
        private static readonly int[] KING_END = {
            -50,-40,-30,-20,-20,-30,-40,-50,
            -30,-20,-10,  0,  0,-10,-20,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-30,  0,  0,  0,  0,-30,-30,
            -50,-30,-30,-30,-30,-30,-30,-50
        };

        // Indexed by [Colour, PieceType, location]
        // Holds the value of having piece P on square S
        // White pieces have positive values
        // Black pieces have negative values
        private static readonly int[, ,] pieceSquareTables;
        private static readonly PawnHashEntry[] pawnHashTable;
        private const int HASH_TABLE_SIZE = 0x100000;
        private const int SHIFT_HASH = 44;
        /// <summary>
        /// Static constructor
        /// </summary>
        static Evaluator()
        {
            pawnHashTable = new PawnHashEntry[HASH_TABLE_SIZE];
            pieceSquareTables = new int[3, 8, 128];
            for (int x = 0; x != 8; x++)
            {
                for (int y = 0; y != 8; y++)
                {
                    pieceSquareTables[(int)Colour.WHITE, (int)PieceType.PAWN, Board.Index(x, y)] = PAWNS[x + (7 - y) * 8] + VALUE_PAWN;
                    pieceSquareTables[(int)Colour.WHITE, (int)PieceType.KNIGHT, Board.Index(x, y)] = KNIGHTS[x + (7 - y) * 8] + VALUE_KNIGHT;
                    pieceSquareTables[(int)Colour.WHITE, (int)PieceType.BISHOP, Board.Index(x, y)] = BISHOPS[x + (7 - y) * 8] + VALUE_BISHOP;
                    pieceSquareTables[(int)Colour.WHITE, (int)PieceType.ROOK, Board.Index(x, y)] = ROOKS[x + (7 - y) * 8] + VALUE_ROOK;
                    pieceSquareTables[(int)Colour.WHITE, (int)PieceType.QUEEN, Board.Index(x, y)] = QUEENS[x + (7 - y) * 8] + VALUE_QUEEN;
                    pieceSquareTables[(int)Colour.WHITE, (int)PieceType.KING, Board.Index(x, y)] = KING[x + (7 - y) * 8] + VALUE_KING;
                    pieceSquareTables[(int)Colour.WHITE, (int)PieceType.KING_END, Board.Index(x, y)] = KING_END[x + (7 - y) * 8] + VALUE_KING;

                    pieceSquareTables[(int)Colour.BLACK, (int)PieceType.PAWN, Board.Index(x, y)] = -PAWNS[x + y * 8] - VALUE_PAWN;
                    pieceSquareTables[(int)Colour.BLACK, (int)PieceType.KNIGHT, Board.Index(x, y)] = -KNIGHTS[x + y * 8] - VALUE_KNIGHT;
                    pieceSquareTables[(int)Colour.BLACK, (int)PieceType.BISHOP, Board.Index(x, y)] = -BISHOPS[x + y * 8] - VALUE_BISHOP;
                    pieceSquareTables[(int)Colour.BLACK, (int)PieceType.ROOK, Board.Index(x, y)] = -ROOKS[x + y * 8] - VALUE_ROOK;
                    pieceSquareTables[(int)Colour.BLACK, (int)PieceType.QUEEN, Board.Index(x, y)] = -QUEENS[x + y * 8] - VALUE_QUEEN;
                    pieceSquareTables[(int)Colour.BLACK, (int)PieceType.KING, Board.Index(x, y)] = -KING[x + y * 8] - VALUE_KING;
                    pieceSquareTables[(int)Colour.BLACK, (int)PieceType.KING_END, Board.Index(x, y)] = -KING_END[x + y * 8] - VALUE_KING;
                }
            }
        }
        /// <summary>
        /// Used by board class to maintain incremental score for position
        /// </summary>
        public static int GetPieceSquareValue(Square square, int location)
        {
            return pieceSquareTables[(int)square.colour, (int)square.piece, location];
        }
        /// Get the score for this position from the perspective of colour
        /// </summary>
        public static int EvaluatePosition(Board board, Colour colour)
        {
            Game.debug.callsToGetScore++;
            int result = board.pieceSquareScore;
            // Amend king piece square values if towards end game
            if (board.IsEndGame())
            {
                int whiteKingLocation = board.KingLocation(Colour.WHITE);
                int blackKingLocation = board.KingLocation(Colour.BLACK);
                // Subtract king opening/mid-game piece square values
                result -= GetPieceSquareValue(Square.WHITE_KING, whiteKingLocation);
                result -= GetPieceSquareValue(Square.BLACK_KING, blackKingLocation);
                // Add king end-game piece square values
                result += GetPieceSquareValue(Square.WHITE_KING_END, whiteKingLocation);
                result += GetPieceSquareValue(Square.BLACK_KING_END, blackKingLocation);
            }
            // Check for pawn hash
            ulong pawnHash = board.pawnHash;
            int tableIndex = (int)(pawnHash >> SHIFT_HASH);
            PawnHashEntry entry = pawnHashTable[tableIndex];
            if (entry != null && entry.hash == pawnHash)
            {
                result += entry.score;
                Game.debug.pawnHashHits++;
            }
            else
            {
                int pawnScore = 0;
                // We must now compute the pawn hash evaluation
                foreach (int location in board.LocationsOf(Colour.WHITE, PieceType.PAWN))
                {
                    pawnScore += EvaluatePawn(board, location, Colour.WHITE, MoveGenerator.PAWN_VECTORS_WHITE);
                }
                foreach (int location in board.LocationsOf(Colour.BLACK, PieceType.PAWN))
                {
                    pawnScore -= EvaluatePawn(board, location, Colour.BLACK, MoveGenerator.PAWN_VECTORS_BLACK);
                }
                pawnHashTable[tableIndex] = new PawnHashEntry(pawnHash, pawnScore);
                result += pawnScore;
            }
            // Evaluate bishop mobility
            SortedDictionary<int, Move> moves = new SortedDictionary<int, Move>();
            foreach (int location in board.LocationsOf(Colour.WHITE, PieceType.BISHOP))
            {
                moves.Clear();
                MoveGenerator.GenerateBishopMoves(board, Colour.WHITE, location, moves);
                result += moves.Count * BISHOP_MOBILITY_BONUS;
            }
            foreach (int location in board.LocationsOf(Colour.BLACK, PieceType.BISHOP))
            {
                moves.Clear();
                MoveGenerator.GenerateBishopMoves(board, Colour.BLACK, location, moves);
                result -= moves.Count * BISHOP_MOBILITY_BONUS;
            }
            // Negate evaluation if black
            return (colour == Colour.WHITE ? result : -result);
        }

        private static int EvaluatePawn(Board board, int location, Colour colour, int[] PAWN_VECTORS)
        {
            int result = 0;
            Colour enemyColour = colour ^ Colour.XOR;
            int target = location + PAWN_VECTORS[1];
            // Does this pawn defend another of the same colour?
            if ((target & Board.OFF_BOARD) == 0 && board[target] == new Square(colour, PieceType.PAWN))
            {
                result += DEFENDED_PAWN_BONUS;
            }
            target = location + PAWN_VECTORS[2];
            if ((target & Board.OFF_BOARD) == 0 && board[target] == new Square(colour, PieceType.PAWN))
            {
                result += DEFENDED_PAWN_BONUS;
            }
            // Is this pawn passed?
            // A pawn is passed if there is no pawn in front of it, and no pawn of 
            // the opposing colour ahead of it on either adjacent file
            bool isPassed = true;
            // Check file directly ahead
            for (target = location + PAWN_VECTORS[0]; (target & Board.OFF_BOARD) == 0; target += PAWN_VECTORS[0])
            {
                if (board[target].piece == PieceType.PAWN)
                {
                    isPassed = false;
                    break;
                }
            }
            // Check adjacent files
            if (isPassed)
            {
                for (target = location + PAWN_VECTORS[1]; (target & Board.OFF_BOARD) == 0; target += PAWN_VECTORS[0])
                {
                    if (board[target] == new Square(enemyColour, PieceType.PAWN))
                    {
                        isPassed = false;
                        break;
                    }
                }
            }
            if (isPassed)
            {
                for (target = location + PAWN_VECTORS[2]; (target & Board.OFF_BOARD) == 0; target += PAWN_VECTORS[0])
                {
                    if (board[target] == new Square(enemyColour, PieceType.PAWN))
                    {
                        isPassed = false;
                        break;
                    }
                }
            }
            if (isPassed)
            {
                result += PASSED_PAWN_BONUS;
                Game.debug.passedPawns++;
            }
            else
            {
                // Is this pawn doubled?
                for (target = location + PAWN_VECTORS[0]; (target & Board.OFF_BOARD) == 0; target += PAWN_VECTORS[0])
                {
                    if (board[target] == new Square(colour, PieceType.PAWN))
                    {
                        result += DOUBLED_PAWN_PENALTY;
                        Game.debug.doubledPawns++;
                    }
                }
            }
            // Is this pawn blocked?
            bool isBlocked = false;
            target = location + PAWN_VECTORS[0];
            if (board[target] == new Square(enemyColour, PieceType.PAWN))
            {
                isBlocked = true;
                // Check to see if there are any enemy pawns on adjacent files which could unblock it
                for (target = location + PAWN_VECTORS[1]; (target & Board.OFF_BOARD) == 0; target += PAWN_VECTORS[0])
                {
                    if (board[target] == new Square(enemyColour, PieceType.PAWN))
                    {
                        isBlocked = false;
                        break;
                    }
                }
                if (isBlocked)
                {
                    for (target = location + PAWN_VECTORS[2]; (target & Board.OFF_BOARD) == 0; target += PAWN_VECTORS[0])
                    {
                        if (board[target] == new Square(enemyColour, PieceType.PAWN))
                        {
                            isBlocked = false;
                            break;
                        }
                    }
                }
                if (isBlocked)
                {
                    result += BLOCKED_PAWN_PENALTY;
                    Game.debug.blockedPawns++;
                }
            }
            return result;
        }
    }
}
