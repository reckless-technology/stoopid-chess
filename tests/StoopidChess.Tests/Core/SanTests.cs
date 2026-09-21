using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class SanTests
{
    [Fact]
    public void EveryLegalMoveRoundTripsOnTheStandardPositionsAndTheirChildren()
    {
        foreach (PerftPosition entry in Perft.StandardPositions)
        {
            var position = Position.FromFen(entry.Fen);
            AssertRoundTrips(position);
            foreach (Move move in position.GenerateLegalMoves())
            {
                position.MakeMove(move);
                AssertRoundTrips(position);
                position.UnmakeMove();
            }
        }
    }

    private static void AssertRoundTrips(Position position)
    {
        string fen = position.ToFen();
        foreach (Move move in position.GenerateLegalMoves())
        {
            string san = San.Format(position, move);
            Assert.Equal(fen, position.ToFen());    // Formatting must leave the position untouched
            Assert.Equal(move, San.Parse(position, san));
        }
    }

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e2e4", "e4")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "g1f3", "Nf3")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", "O-O")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1", "O-O-O")]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", "e5d6", "exd6")]
    [InlineData("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1", "b7b8q", "b8=Q+")]
    [InlineData("4k3/8/8/8/8/8/4K3/R6R w - - 0 1", "a1d1", "Rad1")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", "a1d1", "Rd1")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1", "h1h8", "Rh8+")]
    [InlineData("k7/8/K7/8/8/8/8/7R w - - 0 1", "h1h8", "Rh8#")]
    public void FormatsTheKnownCases(string fen, string uci, string expected)
    {
        var position = Position.FromFen(fen);
        Move move = position.GenerateLegalMoves().Single(m => m.ToUci() == uci);
        Assert.Equal(expected, San.Format(position, move));
    }

    [Theory]
    [InlineData("0-0", "e1g1")]
    [InlineData("O-O+", "e1g1")]
    [InlineData("0-0-0", "e1c1")]
    public void ParsesCastlingVariants(string san, string uci)
    {
        var position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Assert.Equal(uci, San.Parse(position, san)?.ToUci());
    }

    [Fact]
    public void ParsesLenientPromotionsAndAnnotations()
    {
        var position = Position.FromFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
        Assert.Equal("b7b8q", San.Parse(position, "b8Q")?.ToUci());
        Assert.Equal("b7b8n", San.Parse(position, "b8=N!?")?.ToUci());
        Assert.Null(San.Parse(position, "b8"));         // A promotion must name its piece
        Assert.Null(San.Parse(position, "Zb8"));
        Assert.Null(San.Parse(position, ""));
    }

    [Fact]
    public void RefusesAmbiguousText()
    {
        // With the king off the first rank both rooks reach d1, so "Rd1" names no unique move.
        // With the king on e1 blocking the h-rook (the castling position), plain "Rd1" is unique.
        var open = Position.FromFen("4k3/8/8/8/8/8/4K3/R6R w - - 0 1");
        Assert.Null(San.Parse(open, "Rd1"));
        Assert.Equal("a1d1", San.Parse(open, "Rad1")?.ToUci());
        Assert.Equal("h1d1", San.Parse(open, "Rhd1")?.ToUci());
        var blocked = Position.FromFen("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1");
        Assert.Equal("a1d1", San.Parse(blocked, "Rd1")?.ToUci());
    }
}
