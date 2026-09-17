using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class TranspositionTableTests
{
    private static readonly Move SomeMove = new(Squares.E1, Squares.G1, PieceType.King, PieceType.None, MoveKind.CastleKingSide);

    [Fact]
    public void StoresAndRetrievesByBothKeys()
    {
        var table = new TranspositionTable(1);
        table.Store(0x1234, 0x5678, SomeMove, 42, 5, NodeType.Exact);
        Assert.True(table.TryGet(0x1234, 0x5678, out TranspositionEntry entry));
        Assert.Equal(SomeMove, entry.BestMove);
        Assert.Equal(42, entry.Score);
        Assert.Equal(5, entry.Depth);
        Assert.Equal(NodeType.Exact, entry.Type);
        Assert.False(table.TryGet(0x1234, 0x9999, out _), "secondary key must match");
        Assert.False(table.TryGet(0x4321, 0x5678, out _));
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
        table.Store(key1, 1, SomeMove, 10, 8, NodeType.Exact);
        table.Store(key2, 2, SomeMove, 20, 3, NodeType.LowerBound);
        table.Store(key3, 3, SomeMove, 30, 2, NodeType.UpperBound);
        Assert.True(table.TryGet(key1, 1, out TranspositionEntry deep));
        Assert.Equal(8, deep.Depth);
        Assert.False(table.TryGet(key2, 2, out _), "shallow entry displaced by a more recent shallow one");
        Assert.True(table.TryGet(key3, 3, out TranspositionEntry recent));
        Assert.Equal(2, recent.Depth);
    }

    [Fact]
    public void ClearRemovesEverything()
    {
        var table = new TranspositionTable(1);
        table.Store(1, 2, SomeMove, 3, 4, NodeType.Exact);
        table.Clear();
        Assert.False(table.TryGet(1, 2, out _));
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
        Assert.True(table.Capacity >= 1024);
    }
}
