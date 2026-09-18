using System.Numerics;
using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// Attack sets for every piece as bitboards. Knights, kings and pawns come from 64-entry tables.
/// Sliding pieces use magic bitboards: the occupancy along the piece's rays is hashed by a
/// multiply and shift into a table of attack sets, so a bishop, rook or queen's attacks for any
/// occupancy are one multiply and two loads, with no loops or branches.
/// See https://www.chessprogramming.org/Magic_Bitboards
///
/// The magic multipliers are pawnstar's. They only need a plain 64-bit multiply, so they are
/// portable across CPUs. The tables are built from them when the class is first used; the builder
/// throws if a multiplier maps two occupancies with different attack sets to the same slot, so a
/// wrong constant can never produce a wrong move quietly.
/// </summary>
public static class Attacks
{
    private static readonly (int File, int Rank)[] KnightSteps = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int File, int Rank)[] KingSteps = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];
    private static readonly (int File, int Rank)[] DiagonalDirections = [(1, 1), (1, -1), (-1, -1), (-1, 1)];
    private static readonly (int File, int Rank)[] OrthogonalDirections = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    // Collision-free magic multipliers for the standard relevant-occupancy masks (the rays from
    // the square with the edge squares removed), one per square, from pawnstar's generated_data.h.
    private static readonly ulong[] BishopMagicNumbers =
    [
        0x1850104108008810UL, 0x1060512c09024090UL, 0x4021010112000010UL, 0x0084440280112000UL,
        0x2004042000000840UL, 0xc00088200a238104UL, 0x0004040202109004UL, 0x9102002c06081404UL,
        0x4540101042080040UL, 0x1000900401104604UL, 0x022211480281000dUL, 0x0180310502008040UL,
        0x8400011040000812UL, 0x0200010c2a400a80UL, 0x00840b80b0282020UL, 0x0001009084112020UL,
        0x5106012008100104UL, 0x4002498802040420UL, 0xe810000800882408UL, 0x0041001804110010UL,
        0x4011001811400800UL, 0x2202002022100200UL, 0x0302100482100201UL, 0x0800402611008800UL,
        0x0402401090040881UL, 0x0482020048080810UL, 0x1042900818440020UL, 0x1a40104024004080UL,
        0x0685010004104000UL, 0x400400210500a011UL, 0x001410a0340c8400UL, 0x40040040008a1080UL,
        0x109004601050028aUL, 0x029208202442024cUL, 0x2122082400120800UL, 0x0010400a00002200UL,
        0x2240010200030048UL, 0x0021101080030814UL, 0x0008280480044200UL, 0x0002408a01850440UL,
        0x0008111090004880UL, 0x6100540420180420UL, 0x0210202028005000UL, 0x5800084200810801UL,
        0x000c404081201a00UL, 0x8020200041840140UL, 0x0092041804a00202UL, 0x806440a20a020340UL,
        0x0444110402222280UL, 0x0000862101200010UL, 0x1000004218040312UL, 0xa000080484040000UL,
        0x2141111002088000UL, 0x5000400508548100UL, 0x1020097000808001UL, 0x4005010802028860UL,
        0x00a0208808080281UL, 0x012000a09c0c2082UL, 0x1001020044040400UL, 0x4002100300208808UL,
        0x4040000010204840UL, 0x18001008b0018200UL, 0x0902425002008104UL, 0x5004204202082900UL,
    ];

    private static readonly ulong[] RookMagicNumbers =
    [
        0x0080081080204000UL, 0x8040002000100042UL, 0x008010008020000dUL, 0x0480043000804800UL,
        0x0480040002810800UL, 0x0200080102000410UL, 0x020001220000c804UL, 0x8200008204004133UL,
        0x0400800040008020UL, 0x8408804000200080UL, 0x1041806000803000UL, 0x101c800800900080UL,
        0x0002802400080080UL, 0x2805004300040008UL, 0x0002000801040200UL, 0x001a802080004100UL,
        0x8180014000600040UL, 0x0019010020804004UL, 0x8188420020108200UL, 0x2429210010030900UL,
        0x0198008004008008UL, 0x4089818004000200UL, 0x5000040008100102UL, 0x04000200005108a4UL,
        0x0144208280104000UL, 0x0080400040201000UL, 0x2001084100200010UL, 0x0000120200084220UL,
        0x0000040080080081UL, 0xa001000300080400UL, 0x28108a8c00100108UL, 0xc304010200008044UL,
        0x0000824001800020UL, 0x0830402000401008UL, 0x0320001041002100UL, 0x4050008010800800UL,
        0x8000800400800801UL, 0x0002000400808002UL, 0x1000c80104000210UL, 0x01c0804402000081UL,
        0x8002c00021828000UL, 0x0010002000414008UL, 0x0c01002000410010UL, 0x0002000840120020UL,
        0x4000040008008080UL, 0x0002000810020004UL, 0x2001020108240070UL, 0xa801000080410002UL,
        0x0500410c80220200UL, 0x30ca200482400080UL, 0x0d00820040182200UL, 0x0401000820100100UL,
        0x1480040080080080UL, 0x8002800400020080UL, 0x4040084210014400UL, 0x8019241100488a00UL,
        0x26034300201a8001UL, 0x0240001045082081UL, 0x0000200100400811UL, 0x0030002008041101UL,
        0x1021006800100417UL, 0x2006000144100822UL, 0x8200180081102a04UL, 0x00200a84012c4502UL,
    ];

    // The tables. Field initialisers run in this order, and each depends only on the ones above it.
    private static readonly ulong[] KnightTable = BuildTable(square => Leaps(square, KnightSteps));
    private static readonly ulong[] KingTable = BuildTable(square => Leaps(square, KingSteps));
    private static readonly ulong[] WhitePawnTable = BuildTable(square => Bitboards.NorthWest(Bitboards.Of(square)) | Bitboards.NorthEast(Bitboards.Of(square)));
    private static readonly ulong[] BlackPawnTable = BuildTable(square => Bitboards.SouthWest(Bitboards.Of(square)) | Bitboards.SouthEast(Bitboards.Of(square)));
    private static readonly ulong[] BishopRayTable = BuildTable(square => SlowBishop(0, square));
    private static readonly ulong[] RookRayTable = BuildTable(square => SlowRook(0, square));
    private static readonly ulong[] BetweenTable = BuildPairTable(betweenOnly: true);
    private static readonly ulong[] LineTable = BuildPairTable(betweenOnly: false);
    private static readonly SliderAttacks BishopAttacks = new(DiagonalDirections, SlowBishop, BishopMagicNumbers);
    private static readonly SliderAttacks RookAttacks = new(OrthogonalDirections, SlowRook, RookMagicNumbers);

    /// <summary>
    /// The squares a knight on the square attacks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Knight(int square) => KnightTable[square];

    /// <summary>
    /// The squares a king on the square attacks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong King(int square) => KingTable[square];

    /// <summary>
    /// The squares a pawn of the colour on the square attacks (the two diagonally forward squares).
    /// The same set, read the other way, is the squares from which a pawn of the opposite colour
    /// attacks the square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pawn(Colour colour, int square) => (colour == Colour.White ? WhitePawnTable : BlackPawnTable)[square];

    /// <summary>
    /// The squares attacked by a whole set of pawns of the colour.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pawns(Colour colour, ulong pawns)
    {
        return colour == Colour.White
            ? Bitboards.NorthWest(pawns) | Bitboards.NorthEast(pawns)
            : Bitboards.SouthWest(pawns) | Bitboards.SouthEast(pawns);
    }

    /// <summary>
    /// The squares a bishop on the square attacks given the occupied squares: each diagonal up to
    /// and including the first occupied square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Bishop(ulong occupied, int square) => BishopAttacks.Lookup(occupied, square);

    /// <summary>
    /// The squares a rook on the square attacks given the occupied squares.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Rook(ulong occupied, int square) => RookAttacks.Lookup(occupied, square);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Queen(ulong occupied, int square) => BishopAttacks.Lookup(occupied, square) | RookAttacks.Lookup(occupied, square);

    /// <summary>
    /// The diagonals through the square on an empty board (excluding the square itself).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong BishopRays(int square) => BishopRayTable[square];

    /// <summary>
    /// The rank and file through the square on an empty board (excluding the square itself).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong RookRays(int square) => RookRayTable[square];

    /// <summary>
    /// The squares strictly between two squares on the same rank, file or diagonal; empty if they
    /// are not aligned or are adjacent.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Between(int from, int to) => BetweenTable[(from << 6) | to];

    /// <summary>
    /// The whole rank, file or diagonal through two aligned squares, from edge to edge and
    /// including both squares; empty if they are not aligned. A piece pinned against its king may
    /// only move within the line through the two of them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Line(int from, int to) => LineTable[(from << 6) | to];

    /// <summary>
    /// Bishop attacks by walking the diagonals. The reference the magic tables are built from and
    /// checked against.
    /// </summary>
    internal static ulong SlowBishop(ulong occupied, int square) => Slide(occupied, square, DiagonalDirections);

    internal static ulong SlowRook(ulong occupied, int square) => Slide(occupied, square, OrthogonalDirections);

    private static ulong Slide(ulong occupied, int square, (int File, int Rank)[] directions)
    {
        ulong attacks = 0;
        foreach ((int fileStep, int rankStep) in directions)
        {
            int file = Squares.FileOf(square) + fileStep;
            int rank = Squares.RankOf(square) + rankStep;
            while (IsOnBoard(file, rank))
            {
                int to = Squares.Index(file, rank);
                attacks |= Bitboards.Of(to);
                if (Bitboards.Contains(occupied, to))
                {
                    break;
                }
                file += fileStep;
                rank += rankStep;
            }
        }
        return attacks;
    }

    /// <summary>
    /// The squares whose occupancy affects a slider's attacks from the square: its rays without the
    /// edge squares, which block nothing beyond them.
    /// </summary>
    private static ulong RelevantOccupancy(int square, (int File, int Rank)[] directions)
    {
        ulong mask = 0;
        foreach ((int fileStep, int rankStep) in directions)
        {
            int file = Squares.FileOf(square) + fileStep;
            int rank = Squares.RankOf(square) + rankStep;
            while (IsOnBoard(file + fileStep, rank + rankStep))
            {
                mask |= Bitboards.Of(Squares.Index(file, rank));
                file += fileStep;
                rank += rankStep;
            }
        }
        return mask;
    }

    private static ulong Leaps(int square, (int File, int Rank)[] steps)
    {
        ulong attacks = 0;
        foreach ((int fileStep, int rankStep) in steps)
        {
            int file = Squares.FileOf(square) + fileStep;
            int rank = Squares.RankOf(square) + rankStep;
            if (IsOnBoard(file, rank))
            {
                attacks |= Bitboards.Of(Squares.Index(file, rank));
            }
        }
        return attacks;
    }

    private static bool IsOnBoard(int file, int rank) => file is >= 0 and <= 7 && rank is >= 0 and <= 7;

    private static ulong[] BuildTable(Func<int, ulong> attacksFrom)
    {
        var table = new ulong[Squares.Count];
        for (int square = 0; square < Squares.Count; square++)
        {
            table[square] = attacksFrom(square);
        }
        return table;
    }

    private static ulong[] BuildPairTable(bool betweenOnly)
    {
        var table = new ulong[Squares.Count * Squares.Count];
        for (int from = 0; from < Squares.Count; from++)
        {
            for (int to = 0; to < Squares.Count; to++)
            {
                int fileStep = Math.Sign(Squares.FileOf(to) - Squares.FileOf(from));
                int rankStep = Math.Sign(Squares.RankOf(to) - Squares.RankOf(from));
                int fileDistance = Math.Abs(Squares.FileOf(to) - Squares.FileOf(from));
                int rankDistance = Math.Abs(Squares.RankOf(to) - Squares.RankOf(from));
                bool aligned = from != to && (fileDistance == 0 || rankDistance == 0 || fileDistance == rankDistance);
                if (!aligned)
                {
                    continue;
                }
                table[(from << 6) | to] = betweenOnly
                    ? Walk(from, fileStep, rankStep, stopAt: to)
                    : Bitboards.Of(from) | Walk(from, fileStep, rankStep, stopAt: Squares.None) | Walk(from, -fileStep, -rankStep, stopAt: Squares.None);
            }
        }
        return table;
    }

    /// <summary>
    /// The squares from (but excluding) the start square in the given direction, up to the edge or
    /// up to (and excluding) the stop square.
    /// </summary>
    private static ulong Walk(int from, int fileStep, int rankStep, int stopAt)
    {
        ulong squares = 0;
        int file = Squares.FileOf(from) + fileStep;
        int rank = Squares.RankOf(from) + rankStep;
        while (IsOnBoard(file, rank) && Squares.Index(file, rank) != stopAt)
        {
            squares |= Bitboards.Of(Squares.Index(file, rank));
            file += fileStep;
            rank += rankStep;
        }
        return squares;
    }

    /// <summary>
    /// One square's magic: the relevant occupancy mask, the multiplier, the shift that leaves the
    /// index bits, and where this square's slots start in the shared attack table.
    /// </summary>
    private readonly record struct Magic(ulong Mask, ulong Multiplier, int Shift, int Offset);

    /// <summary>
    /// The magic tables for one kind of slider (bishop or rook).
    /// </summary>
    private sealed class SliderAttacks
    {
        private readonly Magic[] magics = new Magic[Squares.Count];
        private readonly ulong[] table;

        public SliderAttacks((int File, int Rank)[] directions, Func<ulong, int, ulong> slowAttacks, ulong[] multipliers)
        {
            int size = 0;
            for (int square = 0; square < Squares.Count; square++)
            {
                ulong mask = RelevantOccupancy(square, directions);
                int bits = BitOperations.PopCount(mask);
                magics[square] = new Magic(mask, multipliers[square], 64 - bits, size);
                size += 1 << bits;
            }
            table = new ulong[size];
            for (int square = 0; square < Squares.Count; square++)
            {
                Magic magic = magics[square];
                // Every subset of the mask, by the carry-rippler trick; it ends where it started, at 0
                ulong occupancy = 0;
                do
                {
                    int index = magic.Offset + (int)((occupancy * magic.Multiplier) >> magic.Shift);
                    ulong attacks = slowAttacks(occupancy, square);
                    if (table[index] != 0 && table[index] != attacks)
                    {
                        throw new InvalidOperationException($"magic multiplier for square {Squares.Name(square)} collides");
                    }
                    table[index] = attacks;
                    occupancy = (occupancy - magic.Mask) & magic.Mask;
                }
                while (occupancy != 0);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong Lookup(ulong occupied, int square)
        {
            ref readonly Magic magic = ref magics[square];
            return table[magic.Offset + (int)(((occupied & magic.Mask) * magic.Multiplier) >> magic.Shift)];
        }
    }
}
