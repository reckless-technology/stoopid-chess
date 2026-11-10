using System.Globalization;

namespace StoopidChess.Play;

/// <summary>
/// A chess clock's setting. Each side starts with the base time, the clock of the side to move counts down, and a
/// side gains the increment after every move it completes. With <paramref name="MovesPerPeriod"/> set ("40 moves in 5
/// minutes") a side also gets the base time added again each time it completes that many moves, and the period repeats.
/// A side whose clock reaches zero loses on time.
/// </summary>
public sealed record TimeControl(TimeSpan Base, TimeSpan Increment, int? MovesPerPeriod = null)
{
    /// <summary>
    /// The control every game starts under: ten minutes each, no increment.
    /// </summary>
    public static readonly TimeControl Default = OfMinutes(10, 0);

    public static TimeControl OfMinutes(int baseMinutes, int incrementSeconds)
    {
        return new TimeControl(TimeSpan.FromMinutes(baseMinutes), TimeSpan.FromSeconds(incrementSeconds));
    }

    /// <summary>
    /// The classic repeating control: the given number of moves in the given minutes, again and again.
    /// </summary>
    public static TimeControl Periods(int moves, int minutes) =>
        new(TimeSpan.FromMinutes(minutes), TimeSpan.Zero, moves);

    /// <summary>
    /// The customary short forms: "40/5" is forty moves in five minutes, "5+3" is five minutes with a three second
    /// increment.
    /// </summary>
    public override string ToString()
    {
        int minutes = (int)Base.TotalMinutes;
        return MovesPerPeriod is int moves
            ? string.Create(CultureInfo.InvariantCulture, $"{moves}/{minutes}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}+{(int)Increment.TotalSeconds}");
    }
}
