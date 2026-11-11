namespace StoopidChess.Play;

/// <summary>
/// Playing strength, as a handicap on the chess clock. The game is played on the clock (see <see cref="TimeControl"/>)
/// and both sides' clocks run in real time, but at a level other than <see cref="Best"/> the engine budgets its moves
/// as though it had only a fraction of its clock (<see cref="LevelSetting.ClockRatio"/>); <see cref="Easy"/> also
/// limits the search depth. See <see cref="Levels"/> for the numbers.
/// </summary>
public enum Level
{
    Easy,
    Medium,
    Hard,
    Best,
}

/// <summary>
/// What a <see cref="Level"/> stands for.
/// </summary>
/// <param name="ClockRatio">
/// How many times more clock time the human has than the engine is allowed to plan with: 1 is equal time, 100 means
/// the engine budgets its moves as though it had a hundredth of its clock.
/// </param>
/// <param name="MaxDepth">The deepest the engine may search, or null for no limit (the clock alone decides).</param>
public readonly record struct LevelSetting(int ClockRatio, int? MaxDepth);

/// <summary>
/// The difficulty ladder: the one place where the levels' strengths are defined.
/// </summary>
public static class Levels
{
    /// <summary>
    /// The clock ratio and depth cap of a level.
    /// </summary>
    public static LevelSetting Setting(this Level level) =>
        level switch
        {
            Level.Easy => new LevelSetting(ClockRatio: 100, MaxDepth: 3),
            Level.Medium => new LevelSetting(ClockRatio: 10, MaxDepth: null),
            Level.Hard => new LevelSetting(ClockRatio: 3, MaxDepth: null),
            Level.Best => new LevelSetting(ClockRatio: 1, MaxDepth: null),
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
}
