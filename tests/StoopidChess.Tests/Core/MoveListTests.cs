using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class MoveListTests
{
    private static readonly Move E2E4 = new(Square.Parse("e2"), Square.Parse("e4"), PieceType.Pawn, PieceType.None, MoveKind.DoublePawnPush);
    private static readonly Move G1F3 = new(Square.G1, Square.Parse("f3"), PieceType.Knight);
    private static readonly Move E1G1 = new(Square.E1, Square.G1, PieceType.King, PieceType.None, MoveKind.CastleKingSide);

    [Fact]
    public void AddsIndexesEnumeratesAndClears()
    {
        MoveList moves = default;
        Assert.Equal(0, moves.Count);
        moves.Add(E2E4);
        moves.Add(G1F3);
        Assert.Equal(2, moves.Count);
        Assert.Equal(E2E4, moves[0]);
        Assert.Equal(G1F3, moves[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => moves[2]);
        Assert.True(moves.Contains(G1F3));
        Assert.False(moves.Contains(E1G1));
        Assert.Equal([E2E4, G1F3], [.. moves]);
        var seen = new List<Move>();
        foreach (Move move in moves)
        {
            seen.Add(move);
        }
        Assert.Equal([E2E4, G1F3], seen);
        Assert.Equal([E2E4, G1F3], moves.ToList());       // Through IEnumerable<Move>, for LINQ
        moves.Clear();
        Assert.Equal(0, moves.Count);
        Assert.Empty(moves.ToArray());
    }

    [Fact]
    public void SpanReordersInPlace()
    {
        MoveList moves = default;
        moves.Add(E2E4);
        moves.Add(G1F3);
        Span<Move> span = moves.AsSpan();
        Assert.Equal(2, span.Length);
        (span[0], span[1]) = (span[1], span[0]);
        Assert.Equal(G1F3, moves[0]);
        Assert.Equal(E2E4, moves[1]);
    }

    [Fact]
    public void HoldsTheMostMobilePosition()
    {
        // 218 legal moves, the most known for any position
        MoveList moves = MoveGenerator.GenerateLegalMoves(Board.FromFen("R6R/3Q4/1Q4Q1/4Q3/2Q4Q/Q4Q2/pp1Q4/kBNN1KB1 w - - 0 1"));
        Assert.Equal(218, moves.Count);
        Assert.True(moves.Count <= MoveList.Capacity);
    }

    [Fact]
    public void IsACopyWhenAssigned()
    {
        MoveList original = default;
        original.Add(E2E4);
        MoveList copy = original;
        copy.Add(G1F3);
        Assert.Equal(1, original.Count);
        Assert.Equal(2, copy.Count);
    }
}
