using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// A square of the board, or <see cref="None"/>. Squares are numbered a1 = 0, b1 = 1, h1 = 7,
/// a2 = 8 and h8 = 63 ("little-endian rank-file" order), so a square's index is also its bit in a
/// <see cref="Bitboard"/>; the file is the low three bits and the rank the next three. The struct
/// is one byte and converts implicitly to its <see cref="int"/> index for table lookups, but an
/// <see cref="int"/> only becomes a square explicitly, so an index can never be mistaken for one.
/// See https://www.chessprogramming.org/Square_Mapping_Considerations
/// </summary>
public readonly record struct Square
{
    public const int Count = 64;

    private readonly byte index;

    /// <summary>
    /// The square with the given index (0..63), or <see cref="None"/> for 64.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not 0..64.</exception>
    public Square(int index)
    {
        if ((uint)index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        this.index = (byte)index;
    }

    private Square(byte index)
    {
        this.index = index;
    }

    /// <summary>
    /// The square with the given index, for callers that have already established it is valid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Square Unchecked(int index)
    {
        Debug.Assert((uint)index <= Count, "square index out of range");
        return new Square((byte)index);
    }

    /// <summary>
    /// "No square", e.g. no en passant square. Its index, 64, is just off the board.
    /// </summary>
    public static Square None => Unchecked(Count);

    public static Square A1 => Unchecked(0);
    public static Square B1 => Unchecked(1);
    public static Square C1 => Unchecked(2);
    public static Square D1 => Unchecked(3);
    public static Square E1 => Unchecked(4);
    public static Square F1 => Unchecked(5);
    public static Square G1 => Unchecked(6);
    public static Square H1 => Unchecked(7);
    public static Square A8 => Unchecked(56);
    public static Square B8 => Unchecked(57);
    public static Square C8 => Unchecked(58);
    public static Square D8 => Unchecked(59);
    public static Square E8 => Unchecked(60);
    public static Square F8 => Unchecked(61);
    public static Square G8 => Unchecked(62);
    public static Square H8 => Unchecked(63);

    /// <summary>
    /// The square at a file (0 = a) and rank (0 = 1).
    /// </summary>
    public static Square At(int file, int rank)
    {
        if (file is < 0 or > 7 || rank is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(file is < 0 or > 7 ? nameof(file) : nameof(rank));
        }
        return Unchecked(file | (rank << 3));
    }

    public int Index => index;

    public int File => index & 7;

    public int Rank => index >> 3;

    /// <summary>
    /// True for the 64 real squares; false for <see cref="None"/>.
    /// </summary>
    public bool IsValid => index < Count;

    public bool IsLight => ((File + Rank) & 1) == 1;

    /// <summary>
    /// Algebraic name, e.g. "e4"; "-" for <see cref="None"/>.
    /// </summary>
    public string Name => IsValid ? $"{(char)('a' + File)}{(char)('1' + Rank)}" : "-";

    public override string ToString() => Name;

    /// <summary>
    /// The 64 squares in a1, b1, ... h8 order.
    /// </summary>
    public static IEnumerable<Square> All => Enumerable.Range(0, Count).Select(Unchecked);

    /// <summary>
    /// Parse an algebraic square name such as "e4", or null if it is not one.
    /// </summary>
    public static Square? TryParse(ReadOnlySpan<char> name)
    {
        if (name.Length != 2)
        {
            return null;
        }
        int file = name[0] - 'a';
        int rank = name[1] - '1';
        return file is >= 0 and <= 7 && rank is >= 0 and <= 7 ? At(file, rank) : null;
    }

    public static Square Parse(ReadOnlySpan<char> name)
    {
        return TryParse(name) ?? throw new FormatException($"'{name}' is not a square");
    }

    /// <summary>
    /// The index, so that a square can index a 64-entry table directly.
    /// </summary>
    public static implicit operator int(Square square) => square.index;

    public static explicit operator Square(int index) => new(index);

    /// <summary>
    /// The square a number of indices further on (8 is one rank up the board, 1 one file to the
    /// right). The caller guarantees the result is on the board.
    /// </summary>
    public static Square operator +(Square square, int delta) => Unchecked(square.index + delta);

    public static Square operator -(Square square, int delta) => Unchecked(square.index - delta);
}
