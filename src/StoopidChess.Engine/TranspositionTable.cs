using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// What a transposition table score tells us about the true score of the node.
/// </summary>
public enum NodeType : byte
{
    None = 0,
    Exact = 1,          // The score is the true minimax score
    LowerBound = 2,     // The search failed high: the true score is at least this (beta cutoff)
    UpperBound = 3,     // The search failed low: the true score is at most this (no move improved alpha)
}

public readonly record struct TranspositionEntry(ulong Key, ulong SecondaryKey, Move BestMove, int Score, int Depth, NodeType Type)
{
    public bool IsEmpty => Type == NodeType.None;
}

/// <summary>
/// A two-tier transposition table (https://www.chessprogramming.org/Transposition_Table). Each
/// index has a depth-preferred slot, which keeps the deepest search result seen, and an
/// always-replace slot, so that recent shallow results are not lost behind old deep ones.
/// Entries are verified with both Zobrist keys so that false hits are negligible.
/// </summary>
public sealed class TranspositionTable
{
    public const int DefaultSizeInMegabytes = 64;
    public const int MinimumSizeInMegabytes = 1;
    public const int MaximumSizeInMegabytes = 1024;

    private const int EntrySize = 48;   // Approximate managed size of a TranspositionEntry

    private TranspositionEntry[] depthPreferred;
    private TranspositionEntry[] alwaysReplace;
    private int shift;

    public TranspositionTable(int sizeInMegabytes = DefaultSizeInMegabytes)
    {
        depthPreferred = [];
        alwaysReplace = [];
        Resize(sizeInMegabytes);
    }

    public int SizeInMegabytes { get; private set; }

    /// <summary>
    /// Number of index positions; each holds two entries.
    /// </summary>
    public int Capacity => depthPreferred.Length;

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
        depthPreferred = new TranspositionEntry[capacity];
        alwaysReplace = new TranspositionEntry[capacity];
    }

    public void Clear()
    {
        Array.Clear(depthPreferred);
        Array.Clear(alwaysReplace);
    }

    public bool TryGet(ulong key, ulong secondaryKey, out TranspositionEntry entry)
    {
        int index = (int)(key >> shift);
        entry = depthPreferred[index];
        if (Matches(entry, key, secondaryKey))
        {
            return true;
        }
        entry = alwaysReplace[index];
        return Matches(entry, key, secondaryKey);
    }

    public void Store(ulong key, ulong secondaryKey, Move bestMove, int score, int depth, NodeType type)
    {
        int index = (int)(key >> shift);
        var entry = new TranspositionEntry(key, secondaryKey, bestMove, score, depth, type);
        ref TranspositionEntry preferred = ref depthPreferred[index];
        if (preferred.IsEmpty || preferred.Depth <= depth || preferred.Key == key)
        {
            preferred = entry;
        }
        else
        {
            alwaysReplace[index] = entry;
        }
    }

    private static bool Matches(in TranspositionEntry entry, ulong key, ulong secondaryKey)
    {
        return !entry.IsEmpty && entry.Key == key && entry.SecondaryKey == secondaryKey;
    }
}
