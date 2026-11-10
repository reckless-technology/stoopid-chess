using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

/// <summary>
/// Each pawn-structure term is pinned by the pawns it finds: the exact squares each pattern picks
/// out, for both sides.
/// </summary>
public class PawnStructureTests
{
    private static (Evaluator.PawnPatterns White, Evaluator.PawnPatterns Black) Patterns(string fen)
    {
        var position = Position.FromFen(fen);
        return (Evaluator.FindPawnPatterns(position, Color.White), Evaluator.FindPawnPatterns(position, Color.Black));
    }

    private static Bitboard Squares(params string[] names)
    {
        Bitboard set = Bitboard.Empty;
        foreach (string name in names)
        {
            set |= Bitboard.Of(Square.Parse(name));
        }
        return set;
    }

    [Fact]
    public void AnIsolatedPawnIsCounted()
    {
        // White's a-pawn has no neighbours; black's pawns are all connected
        (Evaluator.PawnPatterns white, Evaluator.PawnPatterns black) = Patterns("4k3/5ppp/8/8/8/8/P4PPP/4K3 w - - 0 1");
        Assert.Equal(Squares("a2"), white.Isolated);
        Assert.True(black.Isolated.IsEmpty);
    }

    [Fact]
    public void ADoubledPawnIsCountedOncePerExtraPawn()
    {
        // White has doubled e-pawns (and both are isolated, which is the isolated term's business):
        // the one with an own pawn behind it is counted
        (Evaluator.PawnPatterns white, Evaluator.PawnPatterns black) = Patterns("4k3/5ppp/8/8/4P3/8/4P3/4K3 w - - 0 1");
        Assert.Equal(Squares("e4"), white.Doubled);
        Assert.True(black.Doubled.IsEmpty);
    }

    [Fact]
    public void ABackwardPawnIsCounted()
    {
        // White's e3 pawn: its stop square e4 is covered by the black d5 pawn, and the white d4
        // pawn stands ahead of it, so no neighbour can ever step up beside it. Black's d5 is not
        // backward: d4 blocks its stop, but nothing controls it.
        (Evaluator.PawnPatterns white, Evaluator.PawnPatterns black) = Patterns("4k3/8/8/3p4/3P4/4P3/8/4K3 w - - 0 1");
        Assert.Equal(Squares("e3"), white.Backward);
        Assert.True(black.Backward.IsEmpty);
    }

    [Fact]
    public void ConnectedPawnsAreCounted()
    {
        // White's e4 and f4 stand side by side: two connected pawns, and black has none
        (Evaluator.PawnPatterns white, Evaluator.PawnPatterns black) = Patterns("4k3/8/8/8/4PP2/8/8/4K3 w - - 0 1");
        Assert.Equal(Squares("e4", "f4"), white.Connected);
        Assert.True(black.Connected.IsEmpty);
    }

    [Fact]
    public void TheStartingPositionIsBalanced()
    {
        (Evaluator.PawnPatterns white, Evaluator.PawnPatterns black) = Patterns(Position.StartFen);
        Assert.True(white.Isolated.IsEmpty && white.Doubled.IsEmpty && white.Backward.IsEmpty);
        Assert.True(black.Isolated.IsEmpty && black.Doubled.IsEmpty && black.Backward.IsEmpty);
        Assert.Equal(Bitboard.Rank2, white.Connected);
        Assert.Equal(Bitboard.Rank7, black.Connected);
    }
}
