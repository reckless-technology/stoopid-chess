using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace StoopidChess
{
    /// <summary>
    /// Class to represent a move
    /// </summary>
    class Move : IEquatable<Move>, IComparable<Move>
    {
        /// <summary>
        /// Instance variables
        /// </summary>
        public readonly int from;                   // Source square (0x88 format)
        public readonly int to;                     // Dest square (0x88 format)
        public readonly PieceType movingPiece;      // May not be PieceType.NONE
        public readonly MoveType moveType;          // Which type of move it is
        public readonly PieceType capturedPiece;    // May be PieceType.NONE if this move is not a capture
        /// <summary>
        /// Constructor
        /// </summary>
        public Move(int from, int to, PieceType movingPiece, MoveType moveType, PieceType capturedPiece)
        {
            this.from = from;
            this.to = to;
            this.movingPiece = movingPiece;
            this.moveType = moveType;
            this.capturedPiece = capturedPiece;
        }
        /// <summary>
        /// Disallow default construction
        /// </summary>
        private Move()
        {
        }
        /// <summary>
        /// Use NONE instead of null to represent no move
        /// </summary>
        public static readonly Move NONE = new Move(0, 0, PieceType.NONE, MoveType.NONE, PieceType.NONE);
        /// <summary>
        /// CompareTo() is used when sorting collections of moves.
        /// </summary>
        public int CompareTo(Move? that)
        {
            if (that is null)
            {
                return 1;
            }
            return this.GetHashCode() - that.GetHashCode();
        }
        /// <summary>
        /// Move equality by value
        /// </summary>
        public bool Equals(Move? that)
        {
            if (that is null)
            {
                return false;
            }
            return (this.to == that.to && this.from == that.from && this.moveType == that.moveType
                 && this.movingPiece == that.movingPiece && this.capturedPiece == that.capturedPiece);
        }
        /// <summary>
        /// Required in order to overload the == and != operators with value semantics
        /// </summary>
        public override bool Equals(object? obj)
        {
            return Equals(obj as Move);
        }
        /// <summary>
        /// Required when overloading the == and != operators
        /// Used for ordering moves within the SortedDictionary of Moves
        /// in MVV / LVA sequence (most valuable victim, least valuable attacker)
        ///</summary>
        public override int GetHashCode()
        {
            // capturedPiece inverted polarity and greatest left shift,
            // so moves will be sorted by largest captured piece first, 
            // i.e. MVV / LVA
            return to + (from << 8) + ((int)moveType << 16) + ((int)movingPiece << 20) - ((int)capturedPiece << 24);
        }
        /// <summary>
        /// Equality by value
        /// </summary>
        public static bool operator ==(Move? left, Move? right)
        {
            return left is null ? right is null : left.Equals(right);
        }
        /// <summary>
        /// Inequality by value
        /// </summary>
        public static bool operator !=(Move? left, Move? right)
        {
            return !(left == right);
        }
        /// <summary>
        /// Generates the alphanumeric description of the move
        /// </summary>
        public override string ToString()
        {
            if (moveType == MoveType.CASTLE_KING_SIDE_WHITE || moveType == MoveType.CASTLE_KING_SIDE_BLACK)
            {
                return "O-O";
            }
            if (moveType == MoveType.CASTLE_QUEEN_SIDE_WHITE || moveType == MoveType.CASTLE_QUEEN_SIDE_BLACK)
            {
                return "O-O-O";
            }
            StringBuilder result = new StringBuilder();
            switch (movingPiece)
            {
                case PieceType.KNIGHT:
                    result.Append('N');
                    break;
                case PieceType.BISHOP:
                    result.Append('B');
                    break;
                case PieceType.ROOK:
                    result.Append('R');
                    break;
                case PieceType.QUEEN:
                    result.Append('Q');
                    break;
                case PieceType.KING:
                    result.Append('K');
                    break;
            }
            int xfrom = from & Board.FILE;
            int yfrom = (from & Board.RANK) >> 4;
            int xto = to & Board.FILE;
            int yto = (to & Board.RANK) >> 4;
            result.Append((char)('a' + xfrom));
            result.Append((char)('1' + yfrom));
            if (capturedPiece != PieceType.NONE)
            {
                result.Append("x");
            }
            else
            {
                result.Append("-");
            }
            result.Append((char)('a' + xto));
            result.Append((char)('1' + yto));
            if (moveType == MoveType.EN_PASSANT_CAPTURE)
            {
                result.Append("e.p.");
            }
            else if (moveType == MoveType.PAWN_PROMOTION_TO_KNIGHT)
            {
                result.Append("=N");
            }
            else if (moveType == MoveType.PAWN_PROMOTION_TO_QUEEN)
            {
                result.Append("=Q");
            }
            return result.ToString();
        }
    }
}
