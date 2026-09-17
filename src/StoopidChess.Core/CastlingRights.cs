namespace StoopidChess.Core;

/// <summary>
/// Which castling moves are still available to each side.
/// </summary>
[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    White = WhiteKingSide | WhiteQueenSide,
    Black = BlackKingSide | BlackQueenSide,
    All = White | Black,
}

public static class CastlingRightsExtensions
{
    /// <summary>
    /// The FEN castling field, e.g. "KQkq" or "-".
    /// </summary>
    public static string ToFenString(this CastlingRights rights)
    {
        if (rights == CastlingRights.None)
        {
            return "-";
        }
        return string.Concat(
            rights.HasFlag(CastlingRights.WhiteKingSide) ? "K" : "",
            rights.HasFlag(CastlingRights.WhiteQueenSide) ? "Q" : "",
            rights.HasFlag(CastlingRights.BlackKingSide) ? "k" : "",
            rights.HasFlag(CastlingRights.BlackQueenSide) ? "q" : "");
    }

    /// <summary>
    /// The rights that are lost whenever a piece moves from, or a capture is made on, the given square.
    /// The king's home square carries both of that side's rights; each rook's home square carries one.
    /// </summary>
    public static CastlingRights LostBySquare(int square) => square switch
    {
        Squares.E1 => CastlingRights.White,
        Squares.A1 => CastlingRights.WhiteQueenSide,
        Squares.H1 => CastlingRights.WhiteKingSide,
        Squares.E8 => CastlingRights.Black,
        Squares.A8 => CastlingRights.BlackQueenSide,
        Squares.H8 => CastlingRights.BlackKingSide,
        _ => CastlingRights.None,
    };
}
