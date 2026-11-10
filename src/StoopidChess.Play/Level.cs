using StoopidChess.Engine;

namespace StoopidChess.Play;

/// <summary>
/// Playing strength: how hard the engine thinks about each move. The game is played on the clock (see
/// <see cref="TimeControl"/>); the level caps how deep the engine may search, so the lower levels answer at once.
/// </summary>
public enum Level
{
    Beginner,
    Easy,
    Medium,
    Hard,
    Best,
}

/// <summary>
/// The search limits each <see cref="Level"/> stands for.
/// </summary>
public static class Levels
{
    /// <summary>
    /// The search limits of a level: the engine stops at whichever of the depth and time limits it reaches first.
    /// (<see cref="GameController"/> uses the depth, and lets the clocks decide the time.)
    /// </summary>
    public static SearchLimits Limits(this Level level) =>
        level switch
        {
            Level.Beginner => new SearchLimits { Depth = 1, MoveTime = TimeSpan.FromSeconds(1) },
            Level.Easy => new SearchLimits { Depth = 3, MoveTime = TimeSpan.FromSeconds(3) },
            Level.Medium => new SearchLimits { Depth = 6, MoveTime = TimeSpan.FromSeconds(10) },
            Level.Hard => new SearchLimits { Depth = 8, MoveTime = TimeSpan.FromSeconds(20) },
            Level.Best => new SearchLimits { Depth = 13, MoveTime = TimeSpan.FromSeconds(40) },
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
}
