namespace StoopidChess.Core;

/// <summary>
/// Square indexing in "0x88" form: the low nibble is the file (0 = a, 7 = h) and the high nibble the
/// rank (0 = 1, 7 = 8), so a1 is 0x00, h1 is 0x07 and h8 is 0x77. Indices with bit 3 or bit 7 set
/// are off the board, which makes bounds checking a single mask, and the difference between two
/// squares uniquely identifies both the distance and the direction between them.
/// See https://www.chessprogramming.org/0x88
/// </summary>
public static class Squares
{
    /// <summary>
    /// Sentinel for "no square", e.g. no en passant square.
    /// </summary>
    public const int None = -1;

    public const int A1 = 0x00, B1 = 0x01, C1 = 0x02, D1 = 0x03, E1 = 0x04, F1 = 0x05, G1 = 0x06, H1 = 0x07;
    public const int A8 = 0x70, B8 = 0x71, C8 = 0x72, D8 = 0x73, E8 = 0x74, F8 = 0x75, G8 = 0x76, H8 = 0x77;

    public static int Index(int file, int rank) => file | (rank << 4);

    public static int FileOf(int square) => square & 0x07;

    public static int RankOf(int square) => square >> 4;

    /// <summary>
    /// True for the 64 real squares; false for the unused half of the 0x88 array and for <see cref="None"/>.
    /// </summary>
    public static bool IsOnBoard(int square) => (square & ~0x77) == 0;

    /// <summary>
    /// The 64 squares in a1, b1, ... h8 order.
    /// </summary>
    public static IEnumerable<int> All
    {
        get
        {
            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    yield return Index(file, rank);
                }
            }
        }
    }

    /// <summary>
    /// Algebraic name, e.g. "e4".
    /// </summary>
    public static string Name(int square)
    {
        return square == None ? "-" : $"{(char)('a' + FileOf(square))}{(char)('1' + RankOf(square))}";
    }

    /// <summary>
    /// Parse an algebraic square name such as "e4".
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> name, out int square)
    {
        square = None;
        if (name.Length != 2)
        {
            return false;
        }
        int file = name[0] - 'a';
        int rank = name[1] - '1';
        if (file is < 0 or > 7 || rank is < 0 or > 7)
        {
            return false;
        }
        square = Index(file, rank);
        return true;
    }

    public static int Parse(ReadOnlySpan<char> name)
    {
        return TryParse(name, out int square) ? square : throw new FormatException($"'{name}' is not a square");
    }
}
