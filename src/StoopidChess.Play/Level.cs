using StoopidChess.Engine;

namespace StoopidChess.Play;

/// <summary>
/// Playing strength, expressed as the limits given to the engine for each move.
/// </summary>
public enum Level
{
    Beginner,
    Easy,
    Medium,
    Hard,
    Best,
}

public static class Levels
{
    /// <summary>
    /// The engine stops at whichever of the depth and time limits it reaches first.
    /// </summary>
    public static SearchLimits Limits(this Level level) => level switch
    {
        Level.Beginner => new SearchLimits { Depth = 1, MoveTime = TimeSpan.FromSeconds(1) },
        Level.Easy => new SearchLimits { Depth = 3, MoveTime = TimeSpan.FromSeconds(3) },
        Level.Medium => new SearchLimits { Depth = 6, MoveTime = TimeSpan.FromSeconds(10) },
        Level.Hard => new SearchLimits { Depth = 8, MoveTime = TimeSpan.FromSeconds(20) },
        Level.Best => new SearchLimits { Depth = 13, MoveTime = TimeSpan.FromSeconds(40) },
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}
