using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

/// <summary>
/// Each pawn-structure term is pinned by turning only its parameter up and measuring the change:
/// the difference is the term's weight times the white-minus-black count of the pattern.
/// </summary>
public class PawnStructureTests
{
    private static int Delta(string fen, string parameter, int increase)
    {
        var position = Position.FromFen(fen);
        var parameters = new EvaluationParameters();
        var evaluator = new Evaluator(parameters);
        int before = evaluator.Evaluate(position);
        Assert.True(parameters.TrySet(parameter, parameters.Get(parameter)!.Value + increase));
        return evaluator.Evaluate(position) - before;
    }

    [Fact]
    public void AnIsolatedPawnIsCounted()
    {
        // White's a-pawn has no neighbours; black's pawns are all connected
        Assert.Equal(-10, Delta("4k3/5ppp/8/8/8/8/P4PPP/4K3 w - - 0 1", "IsolatedPawnPenalty", 10));
    }

    [Fact]
    public void ADoubledPawnIsCountedOncePerExtraPawn()
    {
        // White has doubled e-pawns (and both are isolated, which is the isolated term's business)
        Assert.Equal(-10, Delta("4k3/5ppp/8/8/4P3/8/4P3/4K3 w - - 0 1", "DoubledPawnPenalty", 10));
    }

    [Fact]
    public void ABackwardPawnIsCounted()
    {
        // White's e3 pawn: its stop square e4 is covered by the black d5 pawn, and the white d4
        // pawn stands ahead of it, so no neighbour can ever step up beside it. Black's d5 is not
        // backward: d4 blocks its stop, but nothing controls it.
        Assert.Equal(-10, Delta("4k3/8/8/3p4/3P4/4P3/8/4K3 w - - 0 1", "BackwardPawnPenalty", 10));
    }

    [Fact]
    public void ConnectedPawnsAreCounted()
    {
        // White's e4 and f4 stand side by side: two connected pawns, and black has none
        Assert.Equal(20, Delta("4k3/8/8/8/4PP2/8/8/4K3 w - - 0 1", "ConnectedPawnBonus", 10));
    }

    [Fact]
    public void TheStartingPositionIsBalanced()
    {
        foreach (string parameter in new[] { "IsolatedPawnPenalty", "DoubledPawnPenalty", "BackwardPawnPenalty", "ConnectedPawnBonus" })
        {
            Assert.Equal(0, Delta(Position.StartFen, parameter, 10));
        }
    }
}
