using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class EvaluatorTests
{
    [Fact]
    public void StartPositionIsBalanced()
    {
        Assert.Equal(Evaluator.TempoBonus, new Evaluator().Evaluate(Board.StartPosition()));
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
        var board = Board.FromFen(fen);
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
    public void EndgameFollowsTheSimplifiedEvaluationRule()
    {
        Assert.True(Evaluator.IsEndgame(Board.FromFen("4k3/8/8/8/8/8/8/3QK3 w - - 0 1")));               // Queen alone
        Assert.True(Evaluator.IsEndgame(Board.FromFen("4k3/8/8/8/8/8/8/2NQK3 w - - 0 1")));              // Queen and one minor
        Assert.False(Evaluator.IsEndgame(Board.FromFen("4k3/8/8/8/8/8/8/1BNQK3 w - - 0 1")));            // Queen and two minors
        Assert.False(Evaluator.IsEndgame(Board.FromFen("4k3/8/8/8/8/8/8/2RQK3 w - - 0 1")));             // Queen and a rook
        Assert.True(Evaluator.IsEndgame(Board.FromFen("4k3/8/8/8/8/8/8/RNBK3R w - - 0 1")));             // No queens at all
        Assert.False(Evaluator.IsEndgame(Board.FromFen("3qk3/8/8/8/8/8/8/1BNQK3 w - - 0 1")));          // One side over the limit is enough
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
    [Theory]
    [InlineData(Board.StartFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w - - 0 1")]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("r2q1rk1/ppp2ppp/2n1bn2/2b1p3/3pP3/1BP1B1N1/PP3PPP/RN1QR1K1 w - - 0 11")]
    [InlineData("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1")]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1")]
    [InlineData("6k1/8/P7/8/7p/8/8/1K6 w - - 0 1")]
    public void IsSymmetricUnderFileMirror(string fen)
    {
        // Crafty's evtest check: the position mirrored file by file (a-h swapped) must score the same,
        // which catches terms that lean to one side of the board by mistake
        var board = Board.FromFen(fen);
        var evaluator = new Evaluator();
        Assert.Equal(evaluator.Evaluate(board), evaluator.Evaluate(FileMirror(board)));
    }

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
        string flippedCastling = castling == "-" ? "-" : new string([.. castling.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))]);
        string side = board.SideToMove == Colour.White ? "b" : "w";
        return Board.FromFen($"{fen} {side} {flippedCastling} - 0 1");
    }

    private static Board FileMirror(Board board)
    {
        var fen = new System.Text.StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 7; file >= 0; file--)
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
                fen.Append(type.ToChar(colour));
            }
            if (empty > 0)
            {
                fen.Append(empty);
            }
            if (rank != 0)
            {
                fen.Append('/');
            }
        }
        string side = board.SideToMove == Colour.White ? "w" : "b";
        return Board.FromFen($"{fen} {side} - - 0 1");   // Castling rights cannot be mirrored and the evaluation ignores them
    }
}
