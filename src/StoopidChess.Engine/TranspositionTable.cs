using System.Runtime.InteropServices;
using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// What a transposition table score tells us about the true score of the node. Alpha-beta often
/// learns only a bound: a move that reaches beta proves the node is worth at least that much and
/// stops the search there (a lower bound), and a node where no move reaches alpha is worth at most
/// alpha (an upper bound). Only a score strictly between the two is exact.
/// </summary>
public enum NodeType : byte
{
    None = 0,
    Exact = 1,          // The score is the true minimax score
    LowerBound = 2,     // The search failed high: the true score is at least this (beta cutoff)
    UpperBound = 3,     // The search failed low: the true score is at most this (no move improved alpha)
}

/// <summary>
/// One transposition table entry: 16 bytes, so that a 64 MB table holds four million positions and
/// an entry is small enough to be read and written in two aligned words.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public readonly struct TranspositionEntry(ulong key, Move bestMove, int score, int depth, NodeType type)
{
    private readonly uint move = bestMove.ToBits();
    private readonly short score = (short)Math.Clamp(score, short.MinValue, short.MaxValue);
    private readonly byte depth = (byte)Math.Clamp(depth, 0, byte.MaxValue);
    private readonly NodeType type = type;

    public ulong Key { get; } = key;

    public Move BestMove => Move.FromBits(move);

    public int Score => score;

    public int Depth => depth;

    public NodeType Type => type;

    public bool IsEmpty => type == NodeType.None;
}

/// <summary>
/// A two-tier transposition table (https://www.chessprogramming.org/Transposition_Table): a cache
/// of search results keyed by the Zobrist key. The same position is reached again and again by
/// different move orders (1. e4 e5 2. Nf3 and 1. Nf3 e5 2. e4 transpose to one position), and in
/// every iteration of the deepening; a result stored the first time - the score, whether it is
/// exact or a bound, the depth it was searched to, and the best move - can settle the node the
/// next time, or at least order its best move first. The index is the key's top bits. Each
/// index has a depth-preferred slot, which keeps the deepest search result seen, and an
/// always-replace slot, so that recent shallow results are not lost behind old deep ones.
/// Entries are verified with the full 64-bit Zobrist key. The two slots of an index sit next to
/// each other in one array, so a probe reads one cache line instead of two random ones.
/// </summary>
public sealed class TranspositionTable
{
    public const int DefaultSizeInMegabytes = 64;
    public const int MinimumSizeInMegabytes = 1;
    public const int MaximumSizeInMegabytes = 1024;

    private const int EntrySize = 16;

    private TranspositionEntry[] entries;   // [2 * index] depth-preferred, [2 * index + 1] always-replace
    private int shift;
    private ulong touched;   // Accumulator that keeps the Prefetch read alive; never used

    public TranspositionTable(int sizeInMegabytes = DefaultSizeInMegabytes)
    {
        entries = [];
        Resize(sizeInMegabytes);
    }

    public int SizeInMegabytes { get; private set; }

    /// <summary>
    /// Number of index positions; each holds two entries.
    /// </summary>
    public int Capacity => entries.Length / 2;

    /// <summary>
    /// Discard all entries and allocate for the new size (rounded down to a power of two of entries).
    /// </summary>
    public void Resize(int sizeInMegabytes)
    {
        sizeInMegabytes = Math.Clamp(sizeInMegabytes, MinimumSizeInMegabytes, MaximumSizeInMegabytes);
        long entriesPerTable = sizeInMegabytes * 1024L * 1024L / EntrySize / 2;
        int bits = (int)Math.Log2(entriesPerTable);
        int capacity = 1 << bits;
        SizeInMegabytes = sizeInMegabytes;
        shift = 64 - bits;
        entries = new TranspositionEntry[2L * capacity];
    }

    public void Clear()
    {
        Array.Clear(entries);
    }

    /// <summary>
    /// Start fetching the bucket for the key, so its cache line is on the way while the caller does
    /// other work before probing. The value read is never used; only the memory access matters.
    /// </summary>
    public void Prefetch(ulong key)
    {
        int index = (int)(key >> shift) << 1;
        touched ^= entries[index].Key;
    }

    public bool TryGet(ulong key, out TranspositionEntry entry)
    {
        int index = (int)(key >> shift) << 1;
        entry = entries[index];
        if (Matches(entry, key))
        {
            return true;
        }
        entry = entries[index + 1];
        return Matches(entry, key);
    }

    public void Store(ulong key, Move bestMove, int score, int depth, NodeType type)
    {
        int index = (int)(key >> shift) << 1;
        var entry = new TranspositionEntry(key, bestMove, score, depth, type);
        ref TranspositionEntry preferred = ref entries[index];
        if (preferred.IsEmpty || preferred.Depth <= depth || preferred.Key == key)
        {
            preferred = entry;
        }
        else
        {
            entries[index + 1] = entry;
        }
    }

    private static bool Matches(in TranspositionEntry entry, ulong key)
    {
        return !entry.IsEmpty && entry.Key == key;
    }
}
