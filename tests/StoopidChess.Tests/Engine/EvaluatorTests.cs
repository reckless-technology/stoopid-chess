using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class EvaluatorTests
{
    [Fact]
    public void StartPositionIsBalanced()
    {
        Assert.Equal(Evaluator.TempoBonus, new Evaluator().Evaluate(Position.Start()));
    }

    [Theory]
    [InlineData(Position.StartFen)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1")]
    [InlineData("r2q1rk1/ppp2ppp/2n1bn2/2b1p3/3pP3/1BP1B1N1/PP3PPP/RN1QR1K1 w - - 0 11")]
    [InlineData("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1")]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1")]
    public void IsSymmetricUnderColorFlip(string fen)
    {
        var position = Position.FromFen(fen);
        Position flipped = ColorFlip(position);
        var evaluator = new Evaluator();
        // Both scores are from the side to move's point of view, and the side to move is flipped too,
        // so the two must be identical
        Assert.Equal(evaluator.Evaluate(position), evaluator.Evaluate(flipped));
    }

    [Fact]
    public void MaterialCounts()
    {
        var evaluator = new Evaluator();
        int withQueen = evaluator.Evaluate(Position.FromFen("4k3/8/8/8/8/8/8/3QK3 w - - 0 1"));
        int withRook = evaluator.Evaluate(Position.FromFen("4k3/8/8/8/8/8/8/3RK3 w - - 0 1"));
        Assert.True(withQueen > withRook);
        Assert.True(withRook > 0);
        // From black's point of view the same positions are bad
        Assert.True(evaluator.Evaluate(Position.FromFen("4k3/8/8/8/8/8/8/3QK3 b - - 0 1")) < 0);
    }

    [Fact]
    public void EndgameFollowsTheSimplifiedEvaluationRule()
    {
        Assert.True(Evaluator.IsEndgame(Position.FromFen("4k3/8/8/8/8/8/8/3QK3 w - - 0 1")));               // Queen alone
        Assert.True(Evaluator.IsEndgame(Position.FromFen("4k3/8/8/8/8/8/8/2NQK3 w - - 0 1")));              // Queen and one minor
        Assert.False(Evaluator.IsEndgame(Position.FromFen("4k3/8/8/8/8/8/8/1BNQK3 w - - 0 1")));            // Queen and two minors
        Assert.False(Evaluator.IsEndgame(Position.FromFen("4k3/8/8/8/8/8/8/2RQK3 w - - 0 1")));             // Queen and a rook
        Assert.True(Evaluator.IsEndgame(Position.FromFen("4k3/8/8/8/8/8/8/RNBK3R w - - 0 1")));             // No queens at all
        Assert.False(Evaluator.IsEndgame(Position.FromFen("3qk3/8/8/8/8/8/8/1BNQK3 w - - 0 1")));          // One side over the limit is enough
    }

    [Fact]
    public void KingHeadsForTheCentreInTheEndgame()
    {
        var evaluator = new Evaluator();
        Assert.True(Evaluator.IsEndgame(Position.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1")));
        Assert.False(Evaluator.IsEndgame(Position.Start()));
        int centre = evaluator.Evaluate(Position.FromFen("4k3/8/8/8/3K4/8/8/8 w - - 0 1"));
        int corner = evaluator.Evaluate(Position.FromFen("4k3/8/8/8/8/8/8/K7 w - - 0 1"));
        Assert.True(centre > corner);
    }

    /// <summary>
    /// Mirror the board vertically and swap colors, giving the same position from the other side.
    /// </summary>
    [Theory]
    [InlineData(Position.StartFen)]
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
        var position = Position.FromFen(fen);
        var evaluator = new Evaluator();
        Assert.Equal(evaluator.Evaluate(position), evaluator.Evaluate(FileMirror(position)));
    }

    [Fact]
    public void ParametersCanBeSetByNameAndChangeTheEvaluation()
    {
        var parameters = new EvaluationParameters();
        var evaluator = new Evaluator(parameters);
        var position = Position.FromFen("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3");
        int before = evaluator.Evaluate(position);
        Assert.Equal(Evaluator.KnightMobility, parameters.Get("KnightMobility"));
        Assert.True(parameters.TrySet("KnightMobility", Evaluator.KnightMobility + 10));
        Assert.NotEqual(before, evaluator.Evaluate(position));
        Assert.True(parameters.TrySet("KnightMobility", Evaluator.KnightMobility));
        Assert.Equal(before, evaluator.Evaluate(position));
        // Material changes rebuild the piece-square tables
        Assert.True(parameters.TrySet("QueenValue", Evaluator.QueenValue + 200));
        Assert.Equal(before + 200, evaluator.Evaluate(Position.FromFen("4k3/8/8/8/8/8/8/3QK3 w - - 0 1")) - new Evaluator().Evaluate(Position.FromFen("4k3/8/8/8/8/8/8/3QK3 w - - 0 1")) + before);
        // Unknown names are refused, values are clamped, and every default matches the constant it came from
        Assert.False(parameters.TrySet("NoSuchParameter", 1));
        Assert.Null(parameters.Get("NoSuchParameter"));
        Assert.True(parameters.TrySet("TempoBonus", 999));
        Assert.Equal(EvaluationParameters.Describe("TempoBonus")!.Value.Max, parameters.Get("TempoBonus"));
        Assert.Equal(Evaluator.QueenValue, EvaluationParameters.Describe("QueenValue")!.Value.Default);
        Assert.Equal(Evaluator.DefaultPassedPawnBonus[6], EvaluationParameters.Describe("PassedRank7")!.Value.Default);
        Assert.Equal(31, EvaluationParameters.Names.Count());
    }

    private static Position ColorFlip(Position position)
    {
        var fen = new System.Text.StringBuilder();
        for (int rank = 0; rank < 8; rank++)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                (Color color, Piece type) = position[file, rank];
                if (type == Piece.None)
                {
                    empty++;
                    continue;
                }
                if (empty > 0)
                {
                    fen.Append(empty);
                    empty = 0;
                }
                fen.Append(type.ToChar(color.Opponent()));
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
        string castling = position.CastlingRights.ToFenString();
        string flippedCastling = castling == "-" ? "-" : new string([.. castling.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))]);
        string side = position.SideToMove == Color.White ? "b" : "w";
        return Position.FromFen($"{fen} {side} {flippedCastling} - 0 1");
    }

    private static Position FileMirror(Position position)
    {
        var fen = new System.Text.StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 7; file >= 0; file--)
            {
                (Color color, Piece type) = position[file, rank];
                if (type == Piece.None)
                {
                    empty++;
                    continue;
                }
                if (empty > 0)
                {
                    fen.Append(empty);
                    empty = 0;
                }
                fen.Append(type.ToChar(color));
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
        string side = position.SideToMove == Color.White ? "w" : "b";
        return Position.FromFen($"{fen} {side} - - 0 1");   // Castling rights cannot be mirrored and the evaluation ignores them
    }
}
