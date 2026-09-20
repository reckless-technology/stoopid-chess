namespace StoopidChess.Core;

/// <summary>
/// The kind of a chess piece, independent of color.
/// </summary>
public enum Piece : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6,
}

public static class PieceExtensions
{
    private const string Letters = ".PNBRQK";

    /// <summary>
    /// The upper-case letter used for this piece type in FEN and algebraic notation ('.' for none).
    /// </summary>
    public static char ToChar(this Piece type) => Letters[(int)type];

    /// <summary>
    /// Parse a piece letter in either case. Returns <see cref="Piece.None"/> for anything else.
    /// </summary>
    public static Piece FromChar(char letter)
    {
        int index = Letters.IndexOf(char.ToUpperInvariant(letter), StringComparison.Ordinal);
        return index <= 0 ? Piece.None : (Piece)index;
    }

    /// <summary>
    /// The FEN letter for a colored piece: upper case for white, lower case for black, '.' for none.
    /// </summary>
    public static char ToChar(this Piece type, Color color)
    {
        char letter = type.ToChar();
        return color == Color.Black ? char.ToLowerInvariant(letter) : letter;
    }

    /// <summary>
    /// The piece types a pawn may promote to.
    /// </summary>
    public static ReadOnlySpan<Piece> PromotionTypes => [Piece.Queen, Piece.Rook, Piece.Bishop, Piece.Knight];
}
