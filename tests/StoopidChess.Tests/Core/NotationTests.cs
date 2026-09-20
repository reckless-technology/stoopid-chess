using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class NotationTests
{
    [Theory]
    [InlineData("e2e4", "e2", "e4", Piece.None)]
    [InlineData("e7e8q", "e7", "e8", Piece.Queen)]
    [InlineData("a2a1n", "a2", "a1", Piece.Knight)]
    public void ParsesUciMoves(string text, string from, string to, Piece promotion)
    {
        Assert.Equal((Square.Parse(from), Square.Parse(to), promotion), Move.ParseUci(text));
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
        Assert.Null(Move.ParseUci(text));
    }

    [Fact]
    public void WritesUciAndLongAlgebraic()
    {
        var position = Position.FromFen("r3k2r/1P2p3/8/3pP3/8/8/8/R3K2R w KQkq d6 0 1");
        var moves = position.GenerateLegalMoves().ToDictionary(m => m.ToUci());
        Assert.Equal("O-O", moves["e1g1"].ToLongAlgebraic());
        Assert.Equal("O-O-O", moves["e1c1"].ToLongAlgebraic());
        Assert.Equal("e5xd6 e.p.", moves["e5d6"].ToLongAlgebraic());
        Assert.Equal("b7xa8=Q", moves["b7a8q"].ToLongAlgebraic());
        Assert.Equal("b7-b8=N", moves["b7b8n"].ToLongAlgebraic());
        Assert.Equal("Ra1-b1", moves["a1b1"].ToLongAlgebraic());
        Assert.Equal("Ke1-d2", moves["e1d2"].ToLongAlgebraic());
        Assert.Equal("e5-e6", moves["e5e6"].ToLongAlgebraic());
        Assert.Equal("0000", Move.None.ToString());
    }

    [Fact]
    public void MovesPackAndUnpackWithoutLoss()
    {
        foreach (Square from in Square.All)
        {
            foreach (Piece promotion in new[] { Piece.None, Piece.Queen, Piece.Knight })
            {
                var move = new Move(from, Square.H8 - from + Square.A1, Piece.Pawn, Piece.Rook, MoveKind.Promotion, promotion);
                var unpacked = Move.FromBits(move.ToBits());
                Assert.Equal(move, unpacked);
                Assert.Equal(from, unpacked.From);
                Assert.Equal(Square.H8 - from, unpacked.To);
                Assert.Equal(Piece.Pawn, unpacked.Piece);
                Assert.Equal(Piece.Rook, unpacked.Captured);
                Assert.Equal(MoveKind.Promotion, unpacked.Kind);
                Assert.Equal(promotion, unpacked.Promotion);
            }
        }
        Assert.Equal(0u, Move.None.ToBits());
        Assert.True(Move.FromBits(0).IsNone);
    }

    [Fact]
    public void SquareNamesRoundTrip()
    {
        foreach (Square square in Square.All)
        {
            Assert.True(square.IsValid);
            Assert.Equal(square, Square.Parse(square.Name));
            Assert.Equal(square, new Square(square.Index));
        }
        Assert.Equal(64, Square.All.Count());
        Assert.False(Square.None.IsValid);
        Assert.Equal(Square.None, new Square(Square.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Square(Square.Count + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Square(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Square.At(8, 0));
        Assert.Equal("-", Square.None.Name);
        Assert.Equal(4, (int)Square.E1);
        Assert.Equal(Square.Parse("e2"), Square.E1 + 8);
    }

    [Fact]
    public void PieceCharactersRoundTrip()
    {
        foreach (char c in "PNBRQKpnbrqk")
        {
            Piece type = PieceExtensions.FromChar(c);
            Assert.NotEqual(Piece.None, type);
            Assert.Equal(c, type.ToChar(char.IsUpper(c) ? Color.White : Color.Black));
        }
        Assert.Equal(Piece.None, PieceExtensions.FromChar('x'));
        Assert.Equal('.', Piece.None.ToChar(Color.None));
        Assert.Equal('.', Piece.None.ToChar());
    }
}
