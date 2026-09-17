using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Engine.Uci;

namespace StoopidChess.Tests.Engine;

public class SearchLimitsTests
{
    [Fact]
    public void NoLimitsMeansNoTimeBudget()
    {
        Assert.Null(new SearchLimits().TimeBudget(Colour.White));
        Assert.Null(SearchLimits.ForDepth(5).TimeBudget(Colour.White));
        Assert.Null(new SearchLimits { Infinite = true, MoveTime = TimeSpan.FromSeconds(1) }.TimeBudget(Colour.White));
    }

    [Fact]
    public void MoveTimeIsUsedExactly()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1234), SearchLimits.ForMoveTime(TimeSpan.FromMilliseconds(1234)).TimeBudget(Colour.Black));
    }

    [Fact]
    public void ClockTimeIsSharedOverTheRemainingMoves()
    {
        var limits = new SearchLimits { WhiteTime = TimeSpan.FromSeconds(60), BlackTime = TimeSpan.FromSeconds(6), WhiteIncrement = TimeSpan.FromSeconds(1) };
        TimeSpan white = limits.TimeBudget(Colour.White)!.Value;
        TimeSpan black = limits.TimeBudget(Colour.Black)!.Value;
        // 60s / 30 moves + 80% of the increment, less overhead
        Assert.InRange(white.TotalMilliseconds, 2700, 2800);
        Assert.InRange(black.TotalMilliseconds, 150, 200);
        Assert.True(white > black);
    }

    [Fact]
    public void MovesToGoShortensOrLengthensTheBudget()
    {
        var limits = new SearchLimits { WhiteTime = TimeSpan.FromSeconds(30), MovesToGo = 3 };
        TimeSpan budget = limits.TimeBudget(Colour.White)!.Value;
        Assert.InRange(budget.TotalMilliseconds, 9900, 10000);
    }

    [Fact]
    public void NeverSpendsMoreThanMostOfTheClock()
    {
        var limits = new SearchLimits { WhiteTime = TimeSpan.FromMilliseconds(100), WhiteIncrement = TimeSpan.FromSeconds(5) };
        Assert.True(limits.TimeBudget(Colour.White)!.Value <= TimeSpan.FromMilliseconds(80));
        var tiny = new SearchLimits { WhiteTime = TimeSpan.FromMilliseconds(1) };
        Assert.Equal(SearchLimits.MinimumThinkingTime, tiny.TimeBudget(Colour.White));
    }

    [Fact]
    public void GoCommandRoundTripsThroughFormatAndParse()
    {
        var limits = new SearchLimits
        {
            Depth = 7,
            MoveTime = TimeSpan.FromMilliseconds(500),
            Nodes = 123456,
            WhiteTime = TimeSpan.FromMilliseconds(60000),
            BlackTime = TimeSpan.FromMilliseconds(59000),
            WhiteIncrement = TimeSpan.FromMilliseconds(1000),
            BlackIncrement = TimeSpan.FromMilliseconds(2000),
            MovesToGo = 40,
        };
        string text = UciEngineClient.FormatGo(limits);
        SearchLimits parsed = UciSession.ParseGo(("go " + text).Split(' '));
        Assert.Equal(limits, parsed);
        Assert.Equal("infinite", UciEngineClient.FormatGo(new SearchLimits { Infinite = true }));
        Assert.Equal("infinite", UciEngineClient.FormatGo(new SearchLimits()));
        Assert.True(UciSession.ParseGo(["go", "infinite", "ponder"]).Infinite);
        Assert.Equal(new SearchLimits(), UciSession.ParseGo(["go", "depth", "x"]));
    }
}
