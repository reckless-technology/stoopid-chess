using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// When a search should stop, as expressed by the UCI "go" command. Any combination may be given;
/// the search stops at the first limit reached. With no limits at all the search runs until it is
/// cancelled or reaches the maximum depth.
/// </summary>
public sealed record SearchLimits
{
    /// <summary>Stop after completing this many plies of iterative deepening.</summary>
    public int? Depth { get; init; }

    /// <summary>Think for at most this long: no new iteration starts after half of it, and the one running stops when it is up.</summary>
    public TimeSpan? MoveTime { get; init; }

    /// <summary>Stop after roughly this many nodes.</summary>
    public long? Nodes { get; init; }

    /// <summary>Time remaining on white's clock.</summary>
    public TimeSpan? WhiteTime { get; init; }

    /// <summary>Time remaining on black's clock.</summary>
    public TimeSpan? BlackTime { get; init; }

    /// <summary>White's increment per move.</summary>
    public TimeSpan? WhiteIncrement { get; init; }

    /// <summary>Black's increment per move.</summary>
    public TimeSpan? BlackIncrement { get; init; }

    /// <summary>Moves until the next time control, if the clock time is for a fixed number of moves.</summary>
    public int? MovesToGo { get; init; }

    /// <summary>Search until told to stop, ignoring every other limit.</summary>
    public bool IsInfinite { get; init; }

    public static readonly TimeSpan MoveOverhead = TimeSpan.FromMilliseconds(30);

    public static readonly TimeSpan MinimumThinkingTime = TimeSpan.FromMilliseconds(5);

    public static SearchLimits ForDepth(int depth) => new() { Depth = depth };

    public static SearchLimits ForMoveTime(TimeSpan time) => new() { MoveTime = time };

    /// <summary>
    /// How long the side to move should think (the budget), and the most a move may take when the
    /// search is extended (the cap), or null when the search is not time limited. A fixed move time
    /// is both. On a clock, the time left is shared out over the moves to go (assumed to be 20 when
    /// not given) with most of the increment added; the cap is most of the clock, so that a move
    /// never flags, and the budget never exceeds it. Both lose a small allowance for communication
    /// overhead.
    /// </summary>
    public (TimeSpan Budget, TimeSpan Cap)? TimeAllocation(Color sideToMove)
    {
        if (IsInfinite)
        {
            return null;
        }
        if (MoveTime is TimeSpan moveTime)
        {
            return (moveTime, moveTime);
        }
        TimeSpan? remaining = sideToMove == Color.White ? WhiteTime : BlackTime;
        if (remaining is null)
        {
            return null;
        }
        TimeSpan clock = AtLeastZero(remaining.Value);
        TimeSpan increment = AtLeastZero(
            (sideToMove == Color.White ? WhiteIncrement : BlackIncrement) ?? TimeSpan.Zero
        );
        int movesToGo = MovesToGo is > 0 ? MovesToGo.Value : 20;
        TimeSpan mostOfTheClock = clock * 0.8;
        TimeSpan budget = (clock / movesToGo) + (increment * 0.8);
        if (budget > mostOfTheClock)
        {
            budget = mostOfTheClock;
        }
        return (AtLeastMinimum(budget - MoveOverhead), AtLeastMinimum(mostOfTheClock - MoveOverhead));
    }

    private static TimeSpan AtLeastMinimum(TimeSpan time) => time < MinimumThinkingTime ? MinimumThinkingTime : time;

    private static TimeSpan AtLeastZero(TimeSpan time) => time < TimeSpan.Zero ? TimeSpan.Zero : time;
}
