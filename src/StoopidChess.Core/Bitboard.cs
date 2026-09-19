using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace StoopidChess.Core;

/// <summary>
/// A set of squares held as 64 bits, bit n standing for square n (see <see cref="Square"/>): the
/// squares a piece attacks, the squares occupied by white, the squares between two others. The
/// struct is a single <see cref="ulong"/> and every operation here is one or two machine
/// instructions; the bit scans compile to tzcnt, popcnt and blsr through
/// <see cref="BitOperations"/>. Iterate the squares of a set with <c>foreach (Square s in set)</c>,
/// or with <see cref="PopFirst"/> in a loop.
/// See https://www.chessprogramming.org/Bitboards
/// </summary>
public readonly record struct Bitboard
{
    private const ulong FileAMask = 0x0101010101010101UL;
    private const ulong FileHMask = FileAMask << 7;

    private readonly ulong bits;

    public Bitboard(ulong bits)
    {
        this.bits = bits;
    }

    /// <summary>
    /// The set containing only the given square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Of(Square square) => new(1UL << square.Index);

    public static Bitboard OfFile(int file) => new(FileAMask << file);

    public static Bitboard OfRank(int rank) => new(0xFFUL << (rank * 8));

    public static Bitboard Empty => default;

    public static Bitboard Full => new(~0UL);

    public static Bitboard FileA => new(FileAMask);

    public static Bitboard FileB => new(FileAMask << 1);

    public static Bitboard FileC => new(FileAMask << 2);

    public static Bitboard FileD => new(FileAMask << 3);

    public static Bitboard FileE => new(FileAMask << 4);

    public static Bitboard FileF => new(FileAMask << 5);

    public static Bitboard FileG => new(FileAMask << 6);

    public static Bitboard FileH => new(FileHMask);

    public static Bitboard Rank1 => new(0xFFUL);

    public static Bitboard Rank2 => new(0xFFUL << 8);

    public static Bitboard Rank3 => new(0xFFUL << 16);

    public static Bitboard Rank4 => new(0xFFUL << 24);

    public static Bitboard Rank5 => new(0xFFUL << 32);

    public static Bitboard Rank6 => new(0xFFUL << 40);

    public static Bitboard Rank7 => new(0xFFUL << 48);

    public static Bitboard Rank8 => new(0xFFUL << 56);

    public static Bitboard LightSquares => new(0x55AA55AA55AA55AAUL);

    public static Bitboard DarkSquares => new(~0x55AA55AA55AA55AAUL);

    /// <summary>
    /// The raw bits, for hashing and table lookups.
    /// </summary>
    public ulong Value => bits;

    public bool IsEmpty => bits == 0;

    public bool IsNotEmpty => bits != 0;

    /// <summary>
    /// True if the set has exactly one square.
    /// </summary>
    public bool IsSingle => bits != 0 && (bits & (bits - 1)) == 0;

    /// <summary>
    /// Number of squares in the set.
    /// </summary>
    public int Count => BitOperations.PopCount(bits);

    /// <summary>
    /// The lowest square in the set (<see cref="Square.None"/> for the empty set).
    /// </summary>
    public Square First => Square.Unchecked(BitOperations.TrailingZeroCount(bits));

    /// <summary>
    /// The set without its lowest square.
    /// </summary>
    public Bitboard WithoutFirst => new(bits & (bits - 1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Square square) => (bits & (1UL << square.Index)) != 0;

    /// <summary>
    /// Remove and return the lowest square in the set. The loop
    /// <c>for (Bitboard b = set; b.IsNotEmpty;) { Square s = Bitboard.PopFirst(ref b); }</c>
    /// is the explicit form of <c>foreach (Square s in set)</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Square PopFirst(ref Bitboard set)
    {
        ulong bits = set.bits;
        set = new Bitboard(bits & (bits - 1));
        return Square.Unchecked(BitOperations.TrailingZeroCount(bits));
    }

    public static Bitboard operator &(Bitboard left, Bitboard right) => new(left.bits & right.bits);

    public static Bitboard operator |(Bitboard left, Bitboard right) => new(left.bits | right.bits);

    public static Bitboard operator ^(Bitboard left, Bitboard right) => new(left.bits ^ right.bits);

    public static Bitboard operator ~(Bitboard set) => new(~set.bits);

    public static explicit operator ulong(Bitboard set) => set.bits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard North() => new(bits << 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard South() => new(bits >> 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard East() => new((bits & ~FileHMask) << 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard West() => new((bits & ~FileAMask) >> 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard NorthEast() => new((bits & ~FileHMask) << 9);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard NorthWest() => new((bits & ~FileAMask) << 7);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard SouthEast() => new((bits & ~FileHMask) >> 7);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard SouthWest() => new((bits & ~FileAMask) >> 9);

    /// <summary>
    /// The set shifted one rank towards the colour's opponent: north for white, south for black.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bitboard Forward(Colour colour) => colour == Colour.White ? North() : South();

    public Enumerator GetEnumerator() => new(bits);

    public override string ToString() => $"0x{bits:X16}";

    /// <summary>
    /// Text rendering for debugging: rank 8 at the top, '1' for squares in the set.
    /// </summary>
    public string ToDiagram()
    {
        var text = new StringBuilder(72);
        for (int rank = 7; rank >= 0; rank--)
        {
            for (int file = 0; file < 8; file++)
            {
                text.Append(Contains(Square.At(file, rank)) ? '1' : '.');
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// Yields the squares of a set from the lowest up, clearing one bit per step.
    /// </summary>
    public struct Enumerator
    {
        private ulong remaining;
        private Square current;

        internal Enumerator(ulong bits)
        {
            remaining = bits;
            current = Square.None;
        }

        public readonly Square Current => current;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (remaining == 0)
            {
                return false;
            }
            current = Square.Unchecked(BitOperations.TrailingZeroCount(remaining));
            remaining &= remaining - 1;
            return true;
        }
    }
}
