namespace StoopidChess.Core;

/// <summary>
/// Square indexing: a1 is 0, b1 is 1, h1 is 7, a2 is 8 and h8 is 63 ("little-endian rank-file"
/// order), so the index of a square is also its bit number in a bitboard (see
/// <see cref="Bitboards"/>). The file is the low three bits and the rank the next three.
/// See https://www.chessprogramming.org/Square_Mapping_Considerations
/// </summary>
public static class Squares
{
    /// <summary>
    /// Sentinel for "no square", e.g. no en passant square.
    /// </summary>
    public const int None = -1;

    public const int Count = 64;

    public const int A1 = 0, B1 = 1, C1 = 2, D1 = 3, E1 = 4, F1 = 5, G1 = 6, H1 = 7;
    public const int A8 = 56, B8 = 57, C8 = 58, D8 = 59, E8 = 60, F8 = 61, G8 = 62, H8 = 63;

    public static int Index(int file, int rank) => file | (rank << 3);

    public static int FileOf(int square) => square & 7;

    public static int RankOf(int square) => square >> 3;

    /// <summary>
    /// True for the 64 real squares; false for <see cref="None"/> and anything else out of range.
    /// </summary>
    public static bool IsOnBoard(int square) => (uint)square < Count;

    /// <summary>
    /// The 64 squares in a1, b1, ... h8 order.
    /// </summary>
    public static IEnumerable<int> All => Enumerable.Range(0, Count);

    /// <summary>
    /// Algebraic name, e.g. "e4".
    /// </summary>
    public static string Name(int square)
    {
        return square == None ? "-" : $"{(char)('a' + FileOf(square))}{(char)('1' + RankOf(square))}";
    }

    /// <summary>
    /// Parse an algebraic square name such as "e4", or null if it is not one.
    /// </summary>
    public static int? TryParse(ReadOnlySpan<char> name)
    {
        if (name.Length != 2)
        {
            return null;
        }
        int file = name[0] - 'a';
        int rank = name[1] - '1';
        return file is >= 0 and <= 7 && rank is >= 0 and <= 7 ? Index(file, rank) : null;
    }

    public static int Parse(ReadOnlySpan<char> name)
    {
        return TryParse(name) ?? throw new FormatException($"'{name}' is not a square");
    }
}
