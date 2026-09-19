using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class StaticExchangeTests
{
    [Theory]
    [InlineData("4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1", "e4d5", 100)]                 // Free pawn
    [InlineData("4k3/8/2p5/3p4/4P3/8/8/4K3 w - - 0 1", "e4d5", 0)]                // Pawn takes pawn, pawn takes back
    [InlineData("4k3/8/2p5/3p4/8/8/8/3RK3 w - - 0 1", "d1d5", -400)]              // Rook takes a defended pawn
    [InlineData("4k3/8/8/3p4/8/8/8/3RK3 w - - 0 1", "d1d5", 100)]                 // Rook takes an undefended pawn
    [InlineData("4k3/4n3/8/3p4/8/8/8/3RK3 w - - 0 1", "d1d5", -400)]              // Knight defends
    [InlineData("3rk3/4n3/8/3p4/8/8/8/3RK3 w - - 0 1", "d1d5", -400)]             // Two defenders, one attacker
    [InlineData("3rk3/4n3/8/3p4/8/8/8/3RK2R w - - 0 1", "d1d5", -400)]            // The h1 rook does not reach d5
    [InlineData("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1", "d1d5", 900)]                 // Free queen
    [InlineData("4k3/8/8/3q4/4P3/8/8/4K3 w - - 0 1", "e4d5", 900)]                // Pawn takes queen
    [InlineData("4k3/8/3b4/8/5N2/8/8/4K3 b - - 0 1", "d6f4", 320)]                // Bishop takes a free knight
    public void ScoresTheExchange(string fen, string uci, int expected)
    {
        var board = Board.FromFen(fen);
        Move move = MoveGenerator.GenerateLegalMoves(board).Single(m => m.ToUci() == uci);
        Assert.Equal(expected, StaticExchange.Evaluate(board, move));
    }

    [Fact]
    public void SeesThroughXRays()
    {
        // Rook takes pawn; knight takes rook; the second rook, uncovered by the first, takes the knight:
        // white loses 500, wins 100 + 320: net -80
        var board = Board.FromFen("4k3/4n3/8/3p4/8/8/3R4/3RK3 w - - 0 1");
        Move move = MoveGenerator.GenerateLegalMoves(board).Single(m => m.ToUci() == "d2d5");
        Assert.Equal(-80, StaticExchange.Evaluate(board, move));
    }

    [Fact]
    public void KingCapturesOnlyWhenNothingCanRecapture()
    {
        // King takes a pawn defended by a rook: the king would be lost, so the capture is refused. The
        // generator never produces this move, so it is built by hand; the exchange evaluation does
        // not need legality.
        var board = Board.FromFen("4k3/8/8/8/8/8/r3p3/4K3 w - - 0 1");
        var move = new Move(Square.E1, Square.Parse("e2"), PieceType.King, PieceType.Pawn);
        Assert.True(StaticExchange.Evaluate(board, move) < 0);
        var safe = Board.FromFen("4k3/8/8/8/8/8/4p3/4K3 w - - 0 1");
        Move capture = MoveGenerator.GenerateLegalMoves(safe).Single(m => m.ToUci() == "e1e2");
        Assert.Equal(100, StaticExchange.Evaluate(safe, capture));
    }

    [Fact]
    public void EnPassantCapturesThePawn()
    {
        var board = Board.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");
        Move move = MoveGenerator.GenerateLegalMoves(board).Single(m => m.ToUci() == "e5d6");
        Assert.Equal(100, StaticExchange.Evaluate(board, move));
    }
}
