using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

/// <summary>
/// Scores of the Simplified Evaluation Function baseline (docs/EVALUATION.md), recorded when the
/// evaluation was reset to it. A change here means the evaluation changed, which should only ever
/// be deliberate: an accepted term updates these values in the same commit.
/// </summary>
public class EvaluatorRegressionTests
{
    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", 10)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 146)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", -49)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 75)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 102)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 10)]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3", -9)]
    [InlineData("r2q1rk1/ppp2ppp/2n1bn2/2b1p3/3pP3/1BP1B1N1/PP3PPP/RN1QR1K1 w - - 0 11", -197)]
    [InlineData("8/8/1p1r1k2/p1pPN1p1/P3P1P1/1P1R1K2/8/8 b - - 0 1", -544)]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1", 624)]
    [InlineData("2r3k1/pp2Rppp/8/3q4/8/1PN5/P4PPP/3Q2K1 w - - 0 1", 320)]
    [InlineData("8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1", 115)]
    [InlineData("4k3/8/8/8/8/8/3P4/4K3 w - - 0 1", 91)]
    [InlineData("4k3/8/8/8/8/3p4/3P4/4K3 w - - 0 1", -40)]
    [InlineData("4k3/8/8/8/3K4/8/8/8 w - - 0 1", 14)]
    [InlineData("4k3/8/8/8/8/8/8/K7 w - - 0 1", 9)]
    [InlineData("r1bq1rk1/pp2bppp/2n2n2/3p4/3P4/2NBPN2/PP3PPP/R2QK2R b KQ - 4 9", 256)]
    [InlineData("8/pp3k2/2p2p2/3p2p1/3P2P1/2P2P2/PP3K2/8 w - - 0 1", 10)]
    public void ScoresMatchTheRecordedValues(string fen, int expected)
    {
        Assert.Equal(expected, new Evaluator().Evaluate(Position.FromFen(fen)));
    }
}
