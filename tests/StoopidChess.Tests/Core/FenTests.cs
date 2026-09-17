using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class FenTests
{
    [Theory]
    [InlineData(Board.StartFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2")]
    [InlineData("8/8/8/8/8/8/8/K6k b - - 99 150")]
    [InlineData("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1")]
    public void RoundTrips(string fen)
    {
        Assert.Equal(fen, Board.FromFen(fen).ToFen());
    }

    [Fact]
    public void ParsesFields()
    {
        Board board = Board.FromFen("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR b Kq e3 7 12");
        Assert.Equal(Colour.Black, board.SideToMove);
        Assert.Equal(CastlingRights.WhiteKingSide | CastlingRights.BlackQueenSide, board.CastlingRights);
        Assert.Equal(Squares.Parse("e3"), board.EnPassantSquare);
        Assert.Equal(7, board.HalfMoveClock);
        Assert.Equal(12, board.FullMoveNumber);
        Assert.Equal(Piece.WhitePawn, board[Squares.Parse("e4")]);
        Assert.Equal(Piece.BlackKing, board[Squares.E8]);
        Assert.Equal(32, board.PieceCount);
    }

    [Fact]
    public void ClockFieldsAreOptional()
    {
        Board board = Board.FromFen("4k3/8/8/8/8/8/8/4K3 w - -");
        Assert.Equal(0, board.HalfMoveClock);
        Assert.Equal(1, board.FullMoveNumber);
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", board.ToFen());
    }

    [Theory]
    [InlineData("")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkz - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq e4 0 1")]
    [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNX w KQkq - 0 1")]
    [InlineData("rnbq1bnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - x 1")]
    public void RejectsInvalidFen(string fen)
    {
        Assert.Throws<FormatException>(() => Board.FromFen(fen));
        Assert.False(Fen.TryParse(fen, out _));
    }

    [Fact]
    public void BoardToStringShowsRankEightAtTop()
    {
        string text = Board.StartPosition().ToString();
        Assert.StartsWith("8 rnbqkbnr", text, StringComparison.Ordinal);
        Assert.EndsWith("  abcdefgh", text, StringComparison.Ordinal);
    }
}
