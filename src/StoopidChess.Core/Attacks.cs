using System.Numerics;
using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// Attack sets for every piece. Knights, kings and pawns come from 64-entry tables. Sliding pieces
/// use magic bitboards: the occupancy along the piece's rays is hashed by a multiply and shift into
/// a table of attack sets, so a bishop, rook or queen's attacks for any occupancy are one multiply
/// and two loads, with no loops or branches.
/// See https://www.chessprogramming.org/Magic_Bitboards
///
/// The magic multipliers are not written down: they are found when the class is first used, by
/// trying random numbers from a fixed seed until each square has one that sends every occupancy of
/// its rays to a slot holding the right attack set (see <see cref="SliderAttacks"/>). Any such
/// multiplier gives exactly the same attack sets, so which one is found does not matter.
/// </summary>
public static class Attacks
{
    private static readonly (int File, int Rank)[] s_knightSteps =
    [
        (1, 2),
        (2, 1),
        (2, -1),
        (1, -2),
        (-1, -2),
        (-2, -1),
        (-2, 1),
        (-1, 2),
    ];
    private static readonly (int File, int Rank)[] s_kingSteps =
    [
        (0, 1),
        (1, 1),
        (1, 0),
        (1, -1),
        (0, -1),
        (-1, -1),
        (-1, 0),
        (-1, 1),
    ];
    private static readonly (int File, int Rank)[] s_diagonalDirections = [(1, 1), (1, -1), (-1, -1), (-1, 1)];
    private static readonly (int File, int Rank)[] s_orthogonalDirections = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    // The tables. Field initialisers run in this order, and each depends only on the ones above it.
    private static readonly Bitboard[] s_knightTable = BuildTable(square => Leaps(square, s_knightSteps));
    private static readonly Bitboard[] s_kingTable = BuildTable(square => Leaps(square, s_kingSteps));
    private static readonly Bitboard[] s_whitePawnTable = BuildTable(square =>
        Bitboard.Of(square).NorthWest() | Bitboard.Of(square).NorthEast()
    );
    private static readonly Bitboard[] s_blackPawnTable = BuildTable(square =>
        Bitboard.Of(square).SouthWest() | Bitboard.Of(square).SouthEast()
    );
    private static readonly Bitboard[] s_bishopRayTable = BuildTable(square =>
        BishopAttacksByWalking(Bitboard.Empty, square)
    );
    private static readonly Bitboard[] s_rookRayTable = BuildTable(square =>
        RookAttacksByWalking(Bitboard.Empty, square)
    );
    private static readonly Bitboard[] s_betweenTable = BuildPairTable(betweenOnly: true);
    private static readonly Bitboard[] s_lineTable = BuildPairTable(betweenOnly: false);
    private static readonly Bitboard[] s_diagonalTable = BuildTable(square =>
        Bitboard.Of(square) | Walk(square, 1, 1, Square.None) | Walk(square, -1, -1, Square.None)
    );
    private static readonly Bitboard[] s_antiDiagonalTable = BuildTable(square =>
        Bitboard.Of(square) | Walk(square, 1, -1, Square.None) | Walk(square, -1, 1, Square.None)
    );
    private static readonly SliderAttacks s_bishopAttacks = new(s_diagonalDirections, BishopAttacksByWalking);
    private static readonly SliderAttacks s_rookAttacks = new(s_orthogonalDirections, RookAttacksByWalking);

    /// <summary>
    /// Build the tables now rather than on first use, so that their cost (mostly the magic search,
    /// a few tens of milliseconds) is paid at start-up, not inside the first search or a timed run.
    /// </summary>
    public static void Initialize() => RuntimeHelpers.RunClassConstructor(typeof(Attacks).TypeHandle);

    /// <summary>
    /// The squares a knight on the square attacks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Knight(Square square) => s_knightTable[square];

    /// <summary>
    /// The squares a king on the square attacks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard King(Square square) => s_kingTable[square];

    /// <summary>
    /// The squares a pawn of the color on the square attacks (the two diagonally forward squares).
    /// The same set, read the other way, is the squares from which a pawn of the opposite color
    /// attacks the square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Pawn(Color color, Square square) =>
        (color == Color.White ? s_whitePawnTable : s_blackPawnTable)[square];

    /// <summary>
    /// The squares attacked by a whole set of pawns of the color.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Pawns(Color color, Bitboard pawns)
    {
        return color == Color.White ? pawns.NorthWest() | pawns.NorthEast() : pawns.SouthWest() | pawns.SouthEast();
    }

    /// <summary>
    /// The squares a bishop on the square attacks given the occupied squares: each diagonal up to
    /// and including the first occupied square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Bishop(Bitboard occupied, Square square) => s_bishopAttacks.Lookup(occupied, square);

    /// <summary>
    /// The squares a rook on the square attacks given the occupied squares.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Rook(Bitboard occupied, Square square) => s_rookAttacks.Lookup(occupied, square);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Queen(Bitboard occupied, Square square) =>
        s_bishopAttacks.Lookup(occupied, square) | s_rookAttacks.Lookup(occupied, square);

    /// <summary>
    /// The diagonals through the square on an empty board (excluding the square itself).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard BishopRays(Square square) => s_bishopRayTable[square];

    /// <summary>
    /// The rank and file through the square on an empty board (excluding the square itself).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard RookRays(Square square) => s_rookRayTable[square];

    /// <summary>
    /// The squares strictly between two squares on the same rank, file or diagonal; empty if they
    /// are not aligned or are adjacent.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Between(Square from, Square to) => s_betweenTable[(from.Index << 6) | to.Index];

    /// <summary>
    /// The whole rank, file or diagonal through two aligned squares, from edge to edge and
    /// including both squares; empty if they are not aligned. A piece pinned against its king may
    /// only move within the line through the two of them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Line(Square from, Square to) => s_lineTable[(from.Index << 6) | to.Index];

    /// <summary>
    /// The a1-h8 diagonal through the square (file minus rank constant), including the square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard Diagonal(Square square) => s_diagonalTable[square];

    /// <summary>
    /// The a8-h1 diagonal through the square (file plus rank constant), including the square.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Bitboard AntiDiagonal(Square square) => s_antiDiagonalTable[square];

    /// <summary>
    /// Bishop attacks found the obvious way: walk each diagonal from the square, one step at a time,
    /// up to the edge or the first occupied square. The magic tables are filled from this.
    /// </summary>
    internal static Bitboard BishopAttacksByWalking(Bitboard occupied, Square square) =>
        Slide(occupied, square, s_diagonalDirections);

    /// <summary>
    /// Rook attacks by walking each rank and file direction, as for the bishop.
    /// </summary>
    internal static Bitboard RookAttacksByWalking(Bitboard occupied, Square square) =>
        Slide(occupied, square, s_orthogonalDirections);

    private static Bitboard Slide(Bitboard occupied, Square square, (int File, int Rank)[] directions)
    {
        Bitboard attacks = Bitboard.Empty;
        foreach ((int fileStep, int rankStep) in directions)
        {
            int file = square.File + fileStep;
            int rank = square.Rank + rankStep;
            while (IsOnBoard(file, rank))
            {
                var to = Square.At(file, rank);
                attacks |= Bitboard.Of(to);
                if (occupied.Contains(to))
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
    private static Bitboard RelevantOccupancy(Square square, (int File, int Rank)[] directions)
    {
        Bitboard mask = Bitboard.Empty;
        foreach ((int fileStep, int rankStep) in directions)
        {
            int file = square.File + fileStep;
            int rank = square.Rank + rankStep;
            while (IsOnBoard(file + fileStep, rank + rankStep))
            {
                mask |= Bitboard.Of(Square.At(file, rank));
                file += fileStep;
                rank += rankStep;
            }
        }
        return mask;
    }

    private static Bitboard Leaps(Square square, (int File, int Rank)[] steps)
    {
        Bitboard attacks = Bitboard.Empty;
        foreach ((int fileStep, int rankStep) in steps)
        {
            int file = square.File + fileStep;
            int rank = square.Rank + rankStep;
            if (IsOnBoard(file, rank))
            {
                attacks |= Bitboard.Of(Square.At(file, rank));
            }
        }
        return attacks;
    }

    private static bool IsOnBoard(int file, int rank) => file is >= 0 and <= 7 && rank is >= 0 and <= 7;

    private static Bitboard[] BuildTable(Func<Square, Bitboard> attacksFrom)
    {
        var table = new Bitboard[Square.Count];
        foreach (Square square in Square.All)
        {
            table[square] = attacksFrom(square);
        }
        return table;
    }

    private static Bitboard[] BuildPairTable(bool betweenOnly)
    {
        var table = new Bitboard[Square.Count * Square.Count];
        foreach (Square from in Square.All)
        {
            foreach (Square to in Square.All)
            {
                int fileStep = Math.Sign(to.File - from.File);
                int rankStep = Math.Sign(to.Rank - from.Rank);
                int fileDistance = Math.Abs(to.File - from.File);
                int rankDistance = Math.Abs(to.Rank - from.Rank);
                bool aligned = from != to && (fileDistance == 0 || rankDistance == 0 || fileDistance == rankDistance);
                if (!aligned)
                {
                    continue;
                }
                table[(from.Index << 6) | to.Index] = betweenOnly
                    ? Walk(from, fileStep, rankStep, stopAt: to)
                    : Bitboard.Of(from)
                        | Walk(from, fileStep, rankStep, stopAt: Square.None)
                        | Walk(from, -fileStep, -rankStep, stopAt: Square.None);
            }
        }
        return table;
    }

    /// <summary>
    /// The squares from (but excluding) the start square in the given direction, up to the edge or
    /// up to (and excluding) the stop square.
    /// </summary>
    private static Bitboard Walk(Square from, int fileStep, int rankStep, Square stopAt)
    {
        Bitboard squares = Bitboard.Empty;
        int file = from.File + fileStep;
        int rank = from.Rank + rankStep;
        while (IsOnBoard(file, rank) && Square.At(file, rank) != stopAt)
        {
            squares |= Bitboard.Of(Square.At(file, rank));
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
    ///
    /// A slider's attacks from a square depend only on which squares of its rays are occupied, and
    /// not even on the last square of each ray (that one is attacked whether it is occupied or not).
    /// The squares that matter form the mask: a rook on a1 has 12, so there are 2^12 = 4096
    /// occupancies of them to tell apart. Multiplying the masked occupancy by a well-chosen 64-bit
    /// number gathers those scattered bits into the top bits of the product, and shifting the
    /// product down leaves a 12-bit index into this square's 4096 slots of attack sets:
    /// <code>index = ((occupied &amp; mask) * multiplier) &gt;&gt; (64 - 12)</code>
    /// A multiplier is "magic" when no two occupancies with different attack sets land on the same
    /// slot (two with the same attacks may share one). Nobody works these numbers out: random
    /// candidates with few bits set are tried until one works.
    /// See https://www.chessprogramming.org/Looking_for_Magics
    /// </summary>
    private sealed class SliderAttacks
    {
        // The random generator restarts at every square from its rank's seed. Any seeds find valid
        // magics, and the same attack sets; these eight are Stockfish's, for the same search, and
        // they find every magic in about 30 ms. The best single seed of 20,000 tried took three
        // times as long, an arbitrary one six times.
        private static readonly ulong[] s_seedByRank = [728, 10316, 55013, 32803, 12281, 15100, 16645, 255];

        private readonly Magic[] _magics = new Magic[Square.Count];
        private readonly Bitboard[] _table;

        public SliderAttacks((int File, int Rank)[] directions, Func<Bitboard, Square, Bitboard> attacksByWalking)
        {
            // Each square gets 2^(mask bits) slots of the one shared table
            int size = 0;
            foreach (Square square in Square.All)
            {
                Bitboard mask = RelevantOccupancy(square, directions);
                _magics[square] = new Magic(mask.Value, 0, 64 - mask.Count, size);
                size += 1 << mask.Count;
            }
            _table = new Bitboard[size];

            foreach (Square square in Square.All)
            {
                Magic magic = _magics[square];
                var random = new MagicCandidates(s_seedByRank[square.Rank]);
                Span<Bitboard> slots = _table.AsSpan(magic.Offset, 1 << (64 - magic.Shift));

                // Every occupancy of the mask and its attack set. The occupancies are the subsets
                // of the mask's bits, enumerated by the carry-rippler trick, which ends where it
                // started, at 0.
                var occupancies = new ulong[slots.Length];
                var attacks = new Bitboard[slots.Length];
                ulong occupancy = 0;
                int count = 0;
                do
                {
                    occupancies[count] = occupancy;
                    attacks[count] = attacksByWalking(new Bitboard(occupancy), square);
                    count++;
                    occupancy = (occupancy - magic.Mask) & magic.Mask;
                } while (occupancy != 0);

                // Try candidates until one fills the slots with no harmful collision. An attack set
                // is never empty, so an empty slot is one no occupancy has reached yet.
                while (true)
                {
                    ulong multiplier = random.Next();
                    if (BitOperations.PopCount((magic.Mask * multiplier) >> 56) < 6)
                    {
                        continue; // Too few of the mask's bits reach the top byte to spread them: skip it cheaply
                    }
                    slots.Clear();
                    bool isMagic = true;
                    for (int i = 0; i < count && isMagic; i++)
                    {
                        ref Bitboard slot = ref slots[(int)((occupancies[i] * multiplier) >> magic.Shift)];
                        isMagic = slot.IsEmpty || slot == attacks[i];
                        slot = attacks[i];
                    }
                    if (isMagic)
                    {
                        _magics[square] = magic with { Multiplier = multiplier };
                        break;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Bitboard Lookup(Bitboard occupied, Square square)
        {
            ref readonly Magic magic = ref _magics[square];
            return _table[magic.Offset + (int)(((occupied.Value & magic.Mask) * magic.Multiplier) >> magic.Shift)];
        }
    }

    /// <summary>
    /// Candidate multipliers for the magic search: random numbers with about one bit in eight set,
    /// among which magics are far more common. The generator is xorshift64* (Sebastiano Vigna), from
    /// a fixed seed, so every start-up finds the same multipliers in the same time.
    /// </summary>
    private struct MagicCandidates(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next() => NextRandom() & NextRandom() & NextRandom();

        private ulong NextRandom()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 2685821657736338717UL;
        }
    }
}
