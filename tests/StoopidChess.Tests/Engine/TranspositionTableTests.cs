using System.Runtime.CompilerServices;
using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class TranspositionTableTests
{
    private static readonly Move SomeMove = new(Square.E1, Square.G1, Piece.King, Piece.None, MoveKind.CastleKingSide);

    [Fact]
    public void EntriesAreSixteenBytes()
    {
        Assert.Equal(16, Unsafe.SizeOf<TranspositionEntry>());
        Assert.Equal(4, Unsafe.SizeOf<Move>());
    }

    [Fact]
    public void StoresAndRetrievesByKey()
    {
        var table = new TranspositionTable(1);
        table.Store(0x1234, SomeMove, 42, 5, NodeType.Exact);
        Assert.True(table.TryGet(0x1234, out TranspositionEntry entry));
        Assert.Equal(SomeMove, entry.BestMove);
        Assert.Equal(42, entry.Score);
        Assert.Equal(5, entry.Depth);
        Assert.Equal(NodeType.Exact, entry.Type);
        Assert.False(table.TryGet(0x4321, out _));
        // Keys that share the index bits but differ elsewhere are distinct entries
        Assert.False(table.TryGet(0x1234 | (1UL << 63), out _));
    }

    [Fact]
    public void ClampsScoreAndDepthToTheEntryRange()
    {
        var entry = new TranspositionEntry(1, SomeMove, Searcher.MateScore + 1000, 300, NodeType.LowerBound);
        Assert.Equal(short.MaxValue, entry.Score);
        Assert.Equal(byte.MaxValue, entry.Depth);
        Assert.Equal(-Searcher.MateScore, new TranspositionEntry(1, SomeMove, -Searcher.MateScore, 0, NodeType.Exact).Score);
    }

    [Fact]
    public void KeepsDeeperEntryAndTheMostRecentOne()
    {
        var table = new TranspositionTable(1);
        int bits = (int)Math.Log2(table.Capacity);
        // Three keys that collide on the same index (same top bits) but differ elsewhere
        ulong key1 = 0xABCDUL << (64 - 16);
        ulong key2 = key1 | 1;
        ulong key3 = key1 | 2;
        Assert.True(bits <= 16);
        table.Store(key1, SomeMove, 10, 8, NodeType.Exact);
        table.Store(key2, SomeMove, 20, 3, NodeType.LowerBound);
        table.Store(key3, SomeMove, 30, 2, NodeType.UpperBound);
        Assert.True(table.TryGet(key1, out TranspositionEntry deep));
        Assert.Equal(8, deep.Depth);
        Assert.False(table.TryGet(key2, out _), "shallow entry displaced by a more recent shallow one");
        Assert.True(table.TryGet(key3, out TranspositionEntry recent));
        Assert.Equal(2, recent.Depth);
    }

    [Fact]
    public void ClearRemovesEverything()
    {
        var table = new TranspositionTable(1);
        table.Store(1, SomeMove, 3, 4, NodeType.Exact);
        table.Clear();
        Assert.False(table.TryGet(1, out _));
    }

    [Theory]
    [InlineData(0, TranspositionTable.MinimumSizeInMegabytes)]
    [InlineData(1, 1)]
    [InlineData(64, 64)]
    [InlineData(5000, TranspositionTable.MaximumSizeInMegabytes)]
    public void ResizeClampsAndUsesPowerOfTwoCapacity(int requested, int expected)
    {
        var table = new TranspositionTable(requested);
        Assert.Equal(expected, table.SizeInMegabytes);
        Assert.Equal(1, System.Numerics.BitOperations.PopCount((uint)table.Capacity));
        Assert.Equal(expected * 1024L * 1024L / 16 / 2, table.Capacity);
    }
}
