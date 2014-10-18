using System;
namespace StoopidChess
{
    /// <summary>
    /// Class to represent a square on the board. 
    /// NB: Squares are immutable i.e. both colour and piece type are read only.
    /// This is required so the Board class can maintain its occupied squares 
    /// lists, positional hashes, castling rights, en passant status and piece-square score.
    /// </summary>
    [Serializable]
    class Square : IEquatable<Square>
    {
        /// <summary>
        /// Instance variables
        /// </summary>
        public readonly Colour colour;
        public readonly PieceType piece;
        /// <summary>
        /// Public constructor
        /// </summary>
        public Square(Colour colour, PieceType piece)
        {
            this.colour = colour;
            this.piece = piece;
        }
        /// <summary>
        /// Disallow default constructor
        /// </summary>
        private Square()
        {
        }
        /// <summary>
        /// Constant squares for use by various classes
        /// (avoids overhead of object creation during search etc.)
        /// </summary>
        public static readonly Square EMPTY          = new Square(Colour.NONE,  PieceType.NONE);
        public static readonly Square WHITE_KING     = new Square(Colour.WHITE, PieceType.KING);
        public static readonly Square BLACK_KING     = new Square(Colour.BLACK, PieceType.KING);
        public static readonly Square WHITE_KING_END = new Square(Colour.WHITE, PieceType.KING_END);
        public static readonly Square BLACK_KING_END = new Square(Colour.BLACK, PieceType.KING_END);
        public static readonly Square WHITE_ROOK     = new Square(Colour.WHITE, PieceType.ROOK);
        public static readonly Square BLACK_ROOK     = new Square(Colour.BLACK, PieceType.ROOK);
        public static readonly Square WHITE_PAWN     = new Square(Colour.WHITE, PieceType.PAWN);
        public static readonly Square BLACK_PAWN     = new Square(Colour.BLACK, PieceType.PAWN);
        /// <summary>
        /// Equality by value
        /// </summary>
        public static bool operator ==(Square left, Square right)
        {
            return left.Equals(right);
        }
        /// <summary>
        /// Inequality by value
        /// </summary>
        public static bool operator !=(Square left, Square right)
        {
            return !left.Equals(right);
        }
        /// <summary>
        /// Required when implementing == and != operators
        /// </summary>
        public override bool Equals(object obj)
        {
            Square that = obj as Square;
            if (that == null)
            {
                return false;
            }
            return Equals(that);
        }
        /// <summary>
        /// Used when determining if a collection of squares contains this square
        /// Equality by value
        /// </summary>
        public bool Equals(Square that)
        {
            return (this.piece == that.piece && this.colour == that.colour);
        }
        /// <summary>
        /// Required when implementing == and != operators
        /// </summary>
        public override int GetHashCode()
        {
            return (int)piece + ((int)colour << 16);
        }
        /// <summary>
        /// Used by Board.ToString() method
        /// </summary>
        /// <returns>single character: capitals for white</returns>
        public override string ToString()
        {
            char result = '.';
            switch (piece)
            {
                case PieceType.PAWN:
                    result = 'p';
                    break;
                case PieceType.KNIGHT:
                    result = 'n';
                    break;
                case PieceType.BISHOP:
                    result = 'b';
                    break;
                case PieceType.ROOK:
                    result = 'r';
                    break;
                case PieceType.QUEEN:
                    result = 'q';
                    break;
                case PieceType.KING:
                    result = 'k';
                    break;
            }
            if (colour == Colour.WHITE)
            {
                return result.ToString().ToUpper();
            }
            return result.ToString();
        }
    }
}
