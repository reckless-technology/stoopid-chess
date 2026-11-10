using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Engine.Uci;

namespace StoopidChess.Tests.Engine;

public class SearchLimitsTests
{
    [Fact]
    public void NoLimitsMeansNoTimeBudget()
    {
        Assert.Null(new SearchLimits().TimeAllocation(Color.White)?.Budget);
        Assert.Null(SearchLimits.ForDepth(5).TimeAllocation(Color.White)?.Budget);
        Assert.Null(
            new SearchLimits { Infinite = true, MoveTime = TimeSpan.FromSeconds(1) }
                .TimeAllocation(Color.White)
                ?.Budget
        );
    }

    [Fact]
    public void MoveTimeIsUsedExactly()
    {
        Assert.Equal(
            (TimeSpan.FromMilliseconds(1234), TimeSpan.FromMilliseconds(1234)),
            SearchLimits.ForMoveTime(TimeSpan.FromMilliseconds(1234)).TimeAllocation(Color.Black)
        );
    }

    [Fact]
    public void TheCapIsMostOfTheClock()
    {
        var limits = new SearchLimits { WhiteTime = TimeSpan.FromSeconds(10), BlackTime = TimeSpan.FromSeconds(1) };
        Assert.Equal(
            TimeSpan.FromSeconds(8) - SearchLimits.MoveOverhead,
            limits.TimeAllocation(Color.White)!.Value.Cap
        );
        Assert.Equal(
            TimeSpan.FromMilliseconds(800) - SearchLimits.MoveOverhead,
            limits.TimeAllocation(Color.Black)!.Value.Cap
        );
    }

    [Fact]
    public void ClockTimeIsSharedOverTheRemainingMoves()
    {
        var limits = new SearchLimits
        {
            WhiteTime = TimeSpan.FromSeconds(60),
            BlackTime = TimeSpan.FromSeconds(6),
            WhiteIncrement = TimeSpan.FromSeconds(1),
        };
        TimeSpan white = limits.TimeAllocation(Color.White)!.Value.Budget;
        TimeSpan black = limits.TimeAllocation(Color.Black)!.Value.Budget;
        // 60s / 20 moves + 80% of the increment, less overhead
        Assert.InRange(white.TotalMilliseconds, 3700, 3800);
        Assert.InRange(black.TotalMilliseconds, 250, 300);
        Assert.True(white > black);
    }

    [Fact]
    public void MovesToGoShortensOrLengthensTheBudget()
    {
        var limits = new SearchLimits { WhiteTime = TimeSpan.FromSeconds(30), MovesToGo = 3 };
        TimeSpan budget = limits.TimeAllocation(Color.White)!.Value.Budget;
        Assert.InRange(budget.TotalMilliseconds, 9900, 10000);
    }

    [Fact]
    public void NeverSpendsMoreThanMostOfTheClock()
    {
        var limits = new SearchLimits
        {
            WhiteTime = TimeSpan.FromMilliseconds(100),
            WhiteIncrement = TimeSpan.FromSeconds(5),
        };
        Assert.True(limits.TimeAllocation(Color.White)!.Value.Budget <= TimeSpan.FromMilliseconds(80));
        var tiny = new SearchLimits { WhiteTime = TimeSpan.FromMilliseconds(1) };
        Assert.Equal(SearchLimits.MinimumThinkingTime, tiny.TimeAllocation(Color.White)?.Budget);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(-1000, -1000, 5)]
    [InlineData(1000, 0, 0)]
    public void DegenerateClocksGiveTheMinimumBudget(int clockMs, int incrementMs, int movesToGo)
    {
        var limits = new SearchLimits
        {
            WhiteTime = TimeSpan.FromMilliseconds(clockMs),
            WhiteIncrement = TimeSpan.FromMilliseconds(incrementMs),
            MovesToGo = movesToGo,
        };
        TimeSpan budget = limits.TimeAllocation(Color.White)!.Value.Budget;
        Assert.True(budget >= SearchLimits.MinimumThinkingTime);
        Assert.True(
            budget
                <= TimeSpan.FromMilliseconds(Math.Max(clockMs, 0) + SearchLimits.MinimumThinkingTime.TotalMilliseconds)
        );
    }
}
