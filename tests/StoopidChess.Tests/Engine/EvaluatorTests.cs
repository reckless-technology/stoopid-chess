using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class EvaluatorTests
{
    [Fact]
    public void StartPositionIsBalanced()
    {
        Assert.Equal(0, new Evaluator().Evaluate(Board.StartPosition()));
    }

    [Theory]
    [InlineData(Board.StartFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("r2q1rk1/ppp2ppp/2n1bn2/2b1p3/3pP3/1BP1B1N1/PP3PPP/RN1QR1K1 w - - 0 11")]
    [InlineData("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1")]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1")]
    public void IsSymmetricUnderColourFlip(string fen)
    {
        Board board = Board.FromFen(fen);
        Board flipped = ColourFlip(board);
        var evaluator = new Evaluator();
        // Both scores are from the side to move's point of view, and the side to move is flipped too,
        // so the two must be identical
        Assert.Equal(evaluator.Evaluate(board), evaluator.Evaluate(flipped));
    }

    [Fact]
    public void MaterialCounts()
    {
        var evaluator = new Evaluator();
        int withQueen = evaluator.Evaluate(Board.FromFen("4k3/8/8/8/8/8/8/3QK3 w - - 0 1"));
        int withRook = evaluator.Evaluate(Board.FromFen("4k3/8/8/8/8/8/8/3RK3 w - - 0 1"));
        Assert.True(withQueen > withRook);
        Assert.True(withRook > 0);
        // From black's point of view the same positions are bad
        Assert.True(evaluator.Evaluate(Board.FromFen("4k3/8/8/8/8/8/8/3QK3 b - - 0 1")) < 0);
    }

    [Fact]
    public void PassedPawnsAreWorthMore()
    {
        var evaluator = new Evaluator();
        int passed = evaluator.Evaluate(Board.FromFen("4k3/8/8/8/8/8/3P4/4K3 w - - 0 1"));
        int blocked = evaluator.Evaluate(Board.FromFen("4k3/8/8/8/8/3p4/3P4/4K3 w - - 0 1"));
        Assert.True(passed > blocked + Evaluator.PawnValue, $"passed {passed} blocked {blocked}");
    }

    [Fact]
    public void PawnHashCachesStructureScores()
    {
        var evaluator = new Evaluator();
        Board board = Board.StartPosition();
        evaluator.Evaluate(board);
        Assert.Equal(0, evaluator.PawnHashHits);
        evaluator.Evaluate(board);
        Assert.Equal(1, evaluator.PawnHashHits);
    }

    [Fact]
    public void KingHeadsForTheCentreInTheEndgame()
    {
        var evaluator = new Evaluator();
        Assert.True(Evaluator.IsEndgame(Board.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1")));
        Assert.False(Evaluator.IsEndgame(Board.StartPosition()));
        int centre = evaluator.Evaluate(Board.FromFen("4k3/8/8/8/3K4/8/8/8 w - - 0 1"));
        int corner = evaluator.Evaluate(Board.FromFen("4k3/8/8/8/8/8/8/K7 w - - 0 1"));
        Assert.True(centre > corner);
    }

    /// <summary>
    /// Mirror the board vertically and swap colours, giving the same position from the other side.
    /// </summary>
    private static Board ColourFlip(Board board)
    {
        var fen = new System.Text.StringBuilder();
        for (int rank = 0; rank < 8; rank++)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                (Colour colour, PieceType type) = board[file, rank];
                if (type == PieceType.None)
                {
                    empty++;
                    continue;
                }
                if (empty > 0)
                {
                    fen.Append(empty);
                    empty = 0;
                }
                fen.Append(type.ToChar(colour.Opponent()));
            }
            if (empty > 0)
            {
                fen.Append(empty);
            }
            if (rank != 7)
            {
                fen.Append('/');
            }
        }
        string castling = board.CastlingRights.ToFenString();
        string flippedCastling = castling == "-" ? "-" : new string(castling.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
        string side = board.SideToMove == Colour.White ? "b" : "w";
        return Board.FromFen($"{fen} {side} {flippedCastling} - 0 1");
    }
}
