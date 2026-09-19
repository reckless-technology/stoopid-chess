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
    // The rights lost when a move touches each square: only the six home squares carry any
    private static readonly CastlingRights[] LostBySquareTable = BuildLostBySquare();

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
    public static CastlingRights LostBySquare(Square square) => LostBySquareTable[square];

    private static CastlingRights[] BuildLostBySquare()
    {
        var table = new CastlingRights[Square.Count + 1];   // Square.None loses nothing
        table[Square.E1] = CastlingRights.White;
        table[Square.A1] = CastlingRights.WhiteQueenSide;
        table[Square.H1] = CastlingRights.WhiteKingSide;
        table[Square.E8] = CastlingRights.Black;
        table[Square.A8] = CastlingRights.BlackQueenSide;
        table[Square.H8] = CastlingRights.BlackKingSide;
        return table;
    }
}
