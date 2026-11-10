using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class MoveListTests
{
    private static readonly Move s_e2e4 = new(
        Square.Parse("e2"),
        Square.Parse("e4"),
        Piece.Pawn,
        Piece.None,
        MoveKind.DoublePawnPush
    );
    private static readonly Move s_g1f3 = new(Square.G1, Square.Parse("f3"), Piece.Knight);
    private static readonly Move s_e1g1 = new(Square.E1, Square.G1, Piece.King, Piece.None, MoveKind.CastleKingSide);

    [Fact]
    public void AddsIndexesEnumeratesAndClears()
    {
        MoveList moves = default;
        Assert.Equal(0, moves.Count);
        moves.Add(s_e2e4);
        moves.Add(s_g1f3);
        Assert.Equal(2, moves.Count);
        Assert.Equal(s_e2e4, moves[0]);
        Assert.Equal(s_g1f3, moves[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => moves[2]);
        Assert.True(moves.Contains(s_g1f3));
        Assert.False(moves.Contains(s_e1g1));
        Assert.Equal([s_e2e4, s_g1f3], [.. moves]);
        var seen = new List<Move>();
        foreach (Move move in moves)
        {
            seen.Add(move);
        }
        Assert.Equal([s_e2e4, s_g1f3], seen);
        Assert.Equal([s_e2e4, s_g1f3], moves.ToArray());
        moves.Clear();
        Assert.Equal(0, moves.Count);
        Assert.Empty(moves.ToArray());
    }

    [Fact]
    public void SpanReordersInPlace()
    {
        MoveList moves = default;
        moves.Add(s_e2e4);
        moves.Add(s_g1f3);
        Span<Move> span = moves.AsSpan();
        Assert.Equal(2, span.Length);
        (span[0], span[1]) = (span[1], span[0]);
        Assert.Equal(s_g1f3, moves[0]);
        Assert.Equal(s_e2e4, moves[1]);
    }

    [Fact]
    public void HoldsTheMostMobilePosition()
    {
        // 218 legal moves, the most known for any position
        MoveList moves = Position.FromFen("R6R/3Q4/1Q4Q1/4Q3/2Q4Q/Q4Q2/pp1Q4/kBNN1KB1 w - - 0 1").GenerateLegalMoves();
        Assert.Equal(218, moves.Count);
        Assert.True(moves.Count <= MoveList.Capacity);
    }

    [Fact]
    public void IsACopyWhenAssigned()
    {
        MoveList original = default;
        original.Add(s_e2e4);
        MoveList copy = original;
        copy.Add(s_g1f3);
        Assert.Equal(1, original.Count);
        Assert.Equal(2, copy.Count);
    }
}
