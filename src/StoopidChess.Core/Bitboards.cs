using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace StoopidChess.Core;

/// <summary>
/// Helpers for bitboards: 64-bit sets of squares in which bit n stands for square n (see
/// <see cref="Squares"/>), so bit 0 is a1, bit 7 is h1 and bit 63 is h8. A set of squares is a
/// plain <see cref="ulong"/>; these methods give the shifts, masks and bit scans that the board,
/// the move generator and the evaluator build on. The bit scans compile to single instructions
/// (tzcnt, popcnt, blsr) through <see cref="BitOperations"/>.
/// See https://www.chessprogramming.org/Bitboards
/// </summary>
public static class Bitboards
{
    public const ulong Empty = 0;
    public const ulong Full = ~0UL;

    public const ulong FileA = 0x0101010101010101UL;
    public const ulong FileB = FileA << 1;
    public const ulong FileC = FileA << 2;
    public const ulong FileD = FileA << 3;
    public const ulong FileE = FileA << 4;
    public const ulong FileF = FileA << 5;
    public const ulong FileG = FileA << 6;
    public const ulong FileH = FileA << 7;

    public const ulong Rank1 = 0xFFUL;
    public const ulong Rank2 = Rank1 << 8;
    public const ulong Rank3 = Rank1 << 16;
    public const ulong Rank4 = Rank1 << 24;
    public const ulong Rank5 = Rank1 << 32;
    public const ulong Rank6 = Rank1 << 40;
    public const ulong Rank7 = Rank1 << 48;
    public const ulong Rank8 = Rank1 << 56;

    public const ulong LightSquares = 0x55AA55AA55AA55AAUL;
    public const ulong DarkSquares = ~LightSquares;

    /// <summary>
    /// The set containing only the given square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Of(int square) => 1UL << square;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(ulong bits, int square) => (bits & (1UL << square)) != 0;

    /// <summary>
    /// Number of squares in the set.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Count(ulong bits) => BitOperations.PopCount(bits);

    /// <summary>
    /// True if the set has exactly one square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSingle(ulong bits) => bits != 0 && (bits & (bits - 1)) == 0;

    /// <summary>
    /// The lowest square in the set (64 for the empty set).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int First(ulong bits) => BitOperations.TrailingZeroCount(bits);

    /// <summary>
    /// Remove and return the lowest square in the set. The usual iteration idiom is
    /// <c>for (ulong bits = set; bits != 0;) { int square = Bitboards.PopFirst(ref bits); ... }</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopFirst(ref ulong bits)
    {
        int square = BitOperations.TrailingZeroCount(bits);
        bits &= bits - 1;
        return square;
    }

    public static ulong File(int file) => FileA << file;

    public static ulong Rank(int rank) => Rank1 << (rank * 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong North(ulong bits) => bits << 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong South(ulong bits) => bits >> 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong East(ulong bits) => (bits & ~FileH) << 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong West(ulong bits) => (bits & ~FileA) >> 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong NorthEast(ulong bits) => (bits & ~FileH) << 9;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong NorthWest(ulong bits) => (bits & ~FileA) << 7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SouthEast(ulong bits) => (bits & ~FileH) >> 7;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SouthWest(ulong bits) => (bits & ~FileA) >> 9;

    /// <summary>
    /// Shift the set one rank towards the given colour's opponent: north for white, south for black.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Forward(Colour colour, ulong bits) => colour == Colour.White ? North(bits) : South(bits);

    /// <summary>
    /// Text rendering for debugging: rank 8 at the top, '1' for squares in the set.
    /// </summary>
    public static string ToDiagram(ulong bits)
    {
        var text = new StringBuilder(72);
        for (int rank = 7; rank >= 0; rank--)
        {
            for (int file = 0; file < 8; file++)
            {
                text.Append(Contains(bits, Squares.Index(file, rank)) ? '1' : '.');
            }
            text.Append('\n');
        }
        return text.ToString();
    }
}
