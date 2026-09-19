using System.Runtime.InteropServices;
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
/// A two-tier transposition table (https://www.chessprogramming.org/Transposition_Table). Each
/// index has a depth-preferred slot, which keeps the deepest search result seen, and an
/// always-replace slot, so that recent shallow results are not lost behind old deep ones.
/// Entries are verified with the full 64-bit Zobrist key.
/// </summary>
public sealed class TranspositionTable
{
    public const int DefaultSizeInMegabytes = 64;
    public const int MinimumSizeInMegabytes = 1;
    public const int MaximumSizeInMegabytes = 1024;

    private const int EntrySize = 16;

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

    public bool TryGet(ulong key, out TranspositionEntry entry)
    {
        int index = (int)(key >> shift);
        entry = depthPreferred[index];
        if (Matches(entry, key))
        {
            return true;
        }
        entry = alwaysReplace[index];
        return Matches(entry, key);
    }

    public void Store(ulong key, Move bestMove, int score, int depth, NodeType type)
    {
        int index = (int)(key >> shift);
        var entry = new TranspositionEntry(key, bestMove, score, depth, type);
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

    private static bool Matches(in TranspositionEntry entry, ulong key)
    {
        return !entry.IsEmpty && entry.Key == key;
    }
}
