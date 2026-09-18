namespace StoopidChess.Core;

/// <summary>
/// The kind of a chess piece, independent of colour.
/// </summary>
public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6,
}

public static class PieceTypeExtensions
{
    private const string Letters = ".PNBRQK";

    /// <summary>
    /// The upper-case letter used for this piece type in FEN and algebraic notation ('.' for none).
    /// </summary>
    public static char ToChar(this PieceType type) => Letters[(int)type];

    /// <summary>
    /// Parse a piece letter in either case. Returns <see cref="PieceType.None"/> for anything else.
    /// </summary>
    public static PieceType FromChar(char letter)
    {
        int index = Letters.IndexOf(char.ToUpperInvariant(letter), StringComparison.Ordinal);
        return index <= 0 ? PieceType.None : (PieceType)index;
    }

    /// <summary>
    /// The piece types a pawn may promote to.
    /// </summary>
    public static ReadOnlySpan<PieceType> PromotionTypes => [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];
}
