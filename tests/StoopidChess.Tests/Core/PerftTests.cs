using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

/// <summary>
/// Move generation regression against the 128 standard perft positions embedded in the Core library
/// (the Ethereal set, also used by pawnstar and zenith). To keep the suite fast, each position is
/// searched to the deepest listed depth whose node count is below a budget; "StoopidChess.Uci perft"
/// runs the full set to every listed depth.
/// </summary>
public class PerftTests
{
    private const long NodeBudget = 300_000;

    public static TheoryData<string, int, long> Positions
    {
        get
        {
            var data = new TheoryData<string, int, long>();
            foreach (PerftPosition position in Perft.StandardPositions)
            {
                (int depth, long nodes) = position.Depths.Last(d => d.Nodes <= NodeBudget || d.Depth == 1);
                data.Add(position.Fen, depth, nodes);
            }
            return data;
        }
    }

    [Fact]
    public void StandardPositionsAreEmbedded()
    {
        Assert.Equal(128, Perft.StandardPositions.Count);
        Assert.Equal(Position.StartFen, Perft.StandardPositions[0].Fen);
        Assert.Equal((6, 119060324L), Perft.StandardPositions[0].Depths[^1]);
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void LeafNodeCountMatchesReference(string fen, int depth, long expected)
    {
        var position = Position.FromFen(fen);
        string normalisedFen = position.ToFen();
        Assert.Equal(expected, Perft.Count(position, depth));
        // The board must be restored exactly by the make/unmake pairs
        Assert.Equal(normalisedFen, position.ToFen());
    }

    [Fact]
    public void DivideSumsToTheFullCount()
    {
        var position = Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
        List<(Move Move, long Nodes)> divided = [.. Perft.Divide(position, 3)];
        Assert.Equal(48, divided.Count);
        Assert.Equal(97862, divided.Sum(d => d.Nodes));
    }

    [Theory]
    [InlineData(";20")]
    [InlineData(";D1 99999999999999999999")]
    [InlineData(";D99999999999 20")]
    [InlineData(";D1 -20")]
    [InlineData("")]
    public void RejectsMalformedEpd(string suffix)
    {
        Assert.Throws<FormatException>(() => Perft.ParseEpd([Position.StartFen + suffix]).ToList());
        Assert.Throws<FormatException>(() => Perft.ParseEpd([";D1 20"]).ToList());
    }
}
