using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class FenTests
{
    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppp1ppp/8/3Pp3/8/8/PPP1PPPP/RNBQKBNR w KQkq e6 0 3")]
    [InlineData("8/8/8/8/8/8/8/K6k b - - 99 150")]
    [InlineData("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1")]
    public void RoundTrips(string fen)
    {
        Assert.Equal(fen, Position.FromFen(fen).ToFen());
    }

    [Fact]
    public void ParsesFields()
    {
        var position = Position.FromFen("rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b Kq e3 7 12");
        Assert.Equal(Color.Black, position.SideToMove);
        Assert.Equal(CastlingRights.WhiteKingSide | CastlingRights.BlackQueenSide, position.CastlingRights);
        Assert.Equal(Square.Parse("e3"), position.EnPassantSquare);
        Assert.Equal(7, position.HalfMoveClock);
        Assert.Equal(12, position.FullMoveNumber);
        Assert.Equal((Color.White, Piece.Pawn), position[Square.Parse("e4")]);
        Assert.Equal((Color.Black, Piece.King), position[Square.E8]);
        Assert.Equal(32, position.PieceCount);
    }

    [Fact]
    public void EnPassantSquareIsKeptOnlyWhenACaptureIsPossible()
    {
        // No white pawn beside e5, so the square is dropped and the key equals the plain position
        var dropped = Position.FromFen("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2");
        Assert.Equal(Square.None, dropped.EnPassantSquare);
        Assert.Equal(Position.FromFen("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2").Key, dropped.Key);
        // The same applies to double pushes made on the board
        var position = Position.Start();
        position.MakeMove(BoardTests.FindMove(position, "e2e4")!.Value);
        Assert.Equal(Square.None, position.EnPassantSquare);
        position.MakeMove(BoardTests.FindMove(position, "d7d5")!.Value);
        Assert.Equal(Square.None, position.EnPassantSquare);
        position.MakeMove(BoardTests.FindMove(position, "e4e5")!.Value);
        position.MakeMove(BoardTests.FindMove(position, "f7f5")!.Value);
        Assert.Equal(Square.Parse("f6"), position.EnPassantSquare);
        // Reaching the same position by a different order gives the same key
        var other = Position.Start();
        foreach (string uci in new[] { "e2e4", "g8f6", "g1f3", "f6g8", "f3g1" })
        {
            other.MakeMove(BoardTests.FindMove(other, uci)!.Value);
        }
        var direct = Position.Start();
        direct.MakeMove(BoardTests.FindMove(direct, "e2e4")!.Value);
        Assert.Equal(direct.Key, other.Key);
        Assert.Equal(1, other.RepetitionCount());
    }

    [Fact]
    public void HugeClockValuesAreClampedNotOverflowed()
    {
        var position = Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 2147483647 99999999999");
        Assert.Equal(Position.MaxHalfMoveClock, position.HalfMoveClock);
        Assert.Equal(int.MaxValue, position.FullMoveNumber);
        var game = new Game(position);
        Assert.True(game.TryMakeMove("g1f3"));
        Assert.Equal(GameStatus.DrawByFiftyMoveRule, game.Status);
        Assert.Equal(Position.MaxHalfMoveClock, game.Position.HalfMoveClock);
        Assert.Equal(position.ToFen(), Position.FromFen(position.ToFen()).ToFen());
    }

    [Fact]
    public void CastlingAdvancesTheClockInTheFen()
    {
        // Each castle adds a ply to the half-move clock, and the FEN written reads back to the same position
        var position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 7 20");
        position.MakeMove(BoardTests.FindMove(position, "e1g1")!.Value);
        position.MakeMove(BoardTests.FindMove(position, "e8c8")!.Value);
        string fen = position.ToFen();
        Assert.Equal("2kr3r/8/8/8/8/8/8/R4RK1 w - - 9 21", fen);
        var read = Position.FromFen(fen);
        Assert.Equal(9, read.HalfMoveClock);
        Assert.Equal(position.Key, read.Key);
        Assert.Equal(fen, read.ToFen());
    }

    [Fact]
    public void ClockFieldsAreOptional()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - -");
        Assert.Equal(0, position.HalfMoveClock);
        Assert.Equal(1, position.FullMoveNumber);
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", position.ToFen());
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
        Assert.Throws<FormatException>(() => Position.FromFen(fen));
        Assert.Null(Fen.TryParse(fen));
    }

    [Fact]
    public void BoardToStringShowsRankEightAtTop()
    {
        string text = Position.Start().ToString();
        Assert.StartsWith("8 rnbqkbnr", text, StringComparison.Ordinal);
        Assert.EndsWith("  abcdefgh", text, StringComparison.Ordinal);
    }
}
