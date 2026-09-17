using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class FenTests
{
    [Theory]
    [InlineData(Board.StartFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppp1ppp/8/3Pp3/8/8/PPP1PPPP/RNBQKBNR w KQkq e6 0 3")]
    [InlineData("8/8/8/8/8/8/8/K6k b - - 99 150")]
    [InlineData("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1")]
    public void RoundTrips(string fen)
    {
        Assert.Equal(fen, Board.FromFen(fen).ToFen());
    }

    [Fact]
    public void ParsesFields()
    {
        Board board = Board.FromFen("rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b Kq e3 7 12");
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
    public void EnPassantSquareIsKeptOnlyWhenACaptureIsPossible()
    {
        // No white pawn beside e5, so the square is dropped and the key equals the plain position
        Board dropped = Board.FromFen("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2");
        Assert.Equal(Squares.None, dropped.EnPassantSquare);
        Assert.Equal(Board.FromFen("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2").Key, dropped.Key);
        // The same applies to double pushes made on the board
        Board board = Board.StartPosition();
        board.MakeMove(BoardTests.FindMove(board, "e2e4")!.Value);
        Assert.Equal(Squares.None, board.EnPassantSquare);
        board.MakeMove(BoardTests.FindMove(board, "d7d5")!.Value);
        Assert.Equal(Squares.None, board.EnPassantSquare);
        board.MakeMove(BoardTests.FindMove(board, "e4e5")!.Value);
        board.MakeMove(BoardTests.FindMove(board, "f7f5")!.Value);
        Assert.Equal(Squares.Parse("f6"), board.EnPassantSquare);
        // Reaching the same position by a different order gives the same key
        Board other = Board.StartPosition();
        foreach (string uci in new[] { "e2e4", "g8f6", "g1f3", "f6g8", "f3g1" })
        {
            other.MakeMove(BoardTests.FindMove(other, uci)!.Value);
        }
        Board direct = Board.StartPosition();
        direct.MakeMove(BoardTests.FindMove(direct, "e2e4")!.Value);
        Assert.Equal(direct.Key, other.Key);
        Assert.Equal(1, other.RepetitionCount());
    }

    [Fact]
    public void HugeClockValuesAreClampedNotOverflowed()
    {
        Board board = Board.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 2147483647 99999999999");
        Assert.Equal(Board.MaxHalfMoveClock, board.HalfMoveClock);
        Assert.Equal(int.MaxValue, board.FullMoveNumber);
        var game = new Game(board);
        Assert.True(game.TryMakeMove("g1f3"));
        Assert.Equal(GameStatus.DrawByFiftyMoveRule, game.Status);
        Assert.Equal(Board.MaxHalfMoveClock, game.Board.HalfMoveClock);
        Assert.Equal(board.ToFen(), Board.FromFen(board.ToFen()).ToFen());
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
    [InlineData("P3k3/8/8/8/8/8/8/4K3 w - - 0 1")]                       // Pawn on the eighth rank
    [InlineData("4k3/8/8/8/8/8/8/3pK3 w - - 0 1")]                       // Pawn on the first rank
    [InlineData("4k3/8/8/8/8/8/3P4/4K3 w - e3 0 1")]                     // En passant square with no pawn behind it
    [InlineData("4k3/8/8/8/4P3/8/8/4K3 w - e3 0 1")]                     // White to move cannot capture its own pawn
    [InlineData("4k3/8/8/8/4P3/8/8/4K3 b - e6 0 1")]                     // Black to move: the square must be on rank 3
    [InlineData("4k3/8/4p3/8/4P3/8/8/4K3 w - e6 0 1")]                   // En passant square occupied
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
