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

    /// <summary>Spend exactly this long on the move.</summary>
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
    public bool Infinite { get; init; }

    public static readonly TimeSpan MoveOverhead = TimeSpan.FromMilliseconds(30);

    public static readonly TimeSpan MinimumThinkingTime = TimeSpan.FromMilliseconds(5);

    public static SearchLimits ForDepth(int depth) => new() { Depth = depth };

    public static SearchLimits ForMoveTime(TimeSpan time) => new() { MoveTime = time };

    /// <summary>
    /// How long the side to move should think, or null when the search is not time limited.
    /// Clock time is shared out over the moves to go (assumed to be 30 when not given) with most of
    /// the increment added, less a small allowance for communication overhead, and never more than
    /// most of the clock.
    /// </summary>
    public TimeSpan? TimeBudget(Colour sideToMove)
    {
        if (Infinite)
        {
            return null;
        }
        if (MoveTime is not null)
        {
            return MoveTime;
        }
        TimeSpan? remaining = sideToMove == Colour.White ? WhiteTime : BlackTime;
        if (remaining is null)
        {
            return null;
        }
        TimeSpan clock = AtLeastZero(remaining.Value);
        TimeSpan increment = AtLeastZero((sideToMove == Colour.White ? WhiteIncrement : BlackIncrement) ?? TimeSpan.Zero);
        int movesToGo = MovesToGo is > 0 ? MovesToGo.Value : 20;
        TimeSpan budget = (clock / movesToGo) + (increment * 0.8);
        TimeSpan cap = clock * 0.8;
        if (budget > cap)
        {
            budget = cap;
        }
        budget -= MoveOverhead;
        return budget < MinimumThinkingTime ? MinimumThinkingTime : budget;
    }

    /// <summary>
    /// The most a single move may take when the search is extended: most of the clock, so that it never flags.
    /// </summary>
    public TimeSpan? TimeCap(Colour sideToMove)
    {
        if (Infinite)
        {
            return null;
        }
        if (MoveTime is not null)
        {
            return MoveTime;
        }
        TimeSpan? remaining = sideToMove == Colour.White ? WhiteTime : BlackTime;
        if (remaining is null)
        {
            return null;
        }
        TimeSpan cap = (AtLeastZero(remaining.Value) * 0.8) - MoveOverhead;
        return cap < MinimumThinkingTime ? MinimumThinkingTime : cap;
    }

    private static TimeSpan AtLeastZero(TimeSpan time) => time < TimeSpan.Zero ? TimeSpan.Zero : time;
}
