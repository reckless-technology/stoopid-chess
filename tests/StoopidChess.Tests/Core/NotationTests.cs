using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class NotationTests
{
    [Theory]
    [InlineData("e2e4", "e2", "e4", PieceType.None)]
    [InlineData("e7e8q", "e7", "e8", PieceType.Queen)]
    [InlineData("a2a1n", "a2", "a1", PieceType.Knight)]
    public void ParsesUciMoves(string text, string from, string to, PieceType promotion)
    {
        Assert.True(MoveNotation.TryParseUci(text, out int parsedFrom, out int parsedTo, out PieceType parsedPromotion));
        Assert.Equal(Squares.Parse(from), parsedFrom);
        Assert.Equal(Squares.Parse(to), parsedTo);
        Assert.Equal(promotion, parsedPromotion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e2")]
    [InlineData("e2e9")]
    [InlineData("e7e8k")]
    [InlineData("e7e8qq")]
    [InlineData("0000")]
    public void RejectsMalformedUciMoves(string text)
    {
        Assert.False(MoveNotation.TryParseUci(text, out _, out _, out _));
    }

    [Fact]
    public void WritesUciAndLongAlgebraic()
    {
        Board board = Board.FromFen("r3k2r/1P2p3/8/3pP3/8/8/8/R3K2R w KQkq d6 0 1");
        Dictionary<string, Move> moves = MoveGenerator.GenerateLegalMoves(board).ToDictionary(m => m.ToUci());
        Assert.Equal("O-O", MoveNotation.ToLongAlgebraic(moves["e1g1"]));
        Assert.Equal("O-O-O", MoveNotation.ToLongAlgebraic(moves["e1c1"]));
        Assert.Equal("e5xd6 e.p.", MoveNotation.ToLongAlgebraic(moves["e5d6"]));
        Assert.Equal("b7xa8=Q", MoveNotation.ToLongAlgebraic(moves["b7a8q"]));
        Assert.Equal("b7-b8=N", MoveNotation.ToLongAlgebraic(moves["b7b8n"]));
        Assert.Equal("Ra1-b1", MoveNotation.ToLongAlgebraic(moves["a1b1"]));
        Assert.Equal("Ke1-d2", MoveNotation.ToLongAlgebraic(moves["e1d2"]));
        Assert.Equal("e5-e6", MoveNotation.ToLongAlgebraic(moves["e5e6"]));
        Assert.Equal("0000", Move.None.ToString());
    }

    [Fact]
    public void SquareNamesRoundTrip()
    {
        foreach (int square in Squares.All)
        {
            Assert.True(Squares.IsOnBoard(square));
            Assert.Equal(square, Squares.Parse(Squares.Name(square)));
        }
        Assert.Equal(64, Squares.All.Count());
        Assert.False(Squares.IsOnBoard(Squares.None));
        Assert.False(Squares.IsOnBoard(0x08));
        Assert.False(Squares.IsOnBoard(0x78));
        Assert.False(Squares.IsOnBoard(0x80));
        Assert.Equal("-", Squares.Name(Squares.None));
    }

    [Fact]
    public void PieceCharactersRoundTrip()
    {
        foreach (char c in "PNBRQKpnbrqk")
        {
            Assert.True(Piece.TryFromChar(c, out Piece piece));
            Assert.Equal(c, piece.ToChar());
        }
        Assert.False(Piece.TryFromChar('x', out _));
        Assert.Equal('.', Piece.None.ToChar());
    }
}
