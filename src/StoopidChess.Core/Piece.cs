namespace StoopidChess.Core;

/// <summary>
/// A coloured piece, or the absence of one (<see cref="None"/>).
/// </summary>
public readonly record struct Piece(Colour Colour, PieceType Type)
{
    public static readonly Piece None = new(Colour.None, PieceType.None);
    public static readonly Piece WhitePawn = new(Colour.White, PieceType.Pawn);
    public static readonly Piece WhiteKnight = new(Colour.White, PieceType.Knight);
    public static readonly Piece WhiteBishop = new(Colour.White, PieceType.Bishop);
    public static readonly Piece WhiteRook = new(Colour.White, PieceType.Rook);
    public static readonly Piece WhiteQueen = new(Colour.White, PieceType.Queen);
    public static readonly Piece WhiteKing = new(Colour.White, PieceType.King);
    public static readonly Piece BlackPawn = new(Colour.Black, PieceType.Pawn);
    public static readonly Piece BlackKnight = new(Colour.Black, PieceType.Knight);
    public static readonly Piece BlackBishop = new(Colour.Black, PieceType.Bishop);
    public static readonly Piece BlackRook = new(Colour.Black, PieceType.Rook);
    public static readonly Piece BlackQueen = new(Colour.Black, PieceType.Queen);
    public static readonly Piece BlackKing = new(Colour.Black, PieceType.King);

    public bool IsNone => Type == PieceType.None;

    /// <summary>
    /// The FEN letter for this piece: upper case for white, lower case for black, '.' for none.
    /// </summary>
    public char ToChar()
    {
        char letter = Type.ToChar();
        return Colour == Colour.Black ? char.ToLowerInvariant(letter) : letter;
    }

    /// <summary>
    /// Parse a FEN piece letter.
    /// </summary>
    public static bool TryFromChar(char letter, out Piece piece)
    {
        PieceType type = PieceTypeExtensions.FromChar(letter);
        if (type == PieceType.None)
        {
            piece = None;
            return false;
        }
        piece = new Piece(char.IsUpper(letter) ? Colour.White : Colour.Black, type);
        return true;
    }

    public override string ToString() => ToChar().ToString();
}
