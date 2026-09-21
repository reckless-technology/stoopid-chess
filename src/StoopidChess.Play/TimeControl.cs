using System.Globalization;

namespace StoopidChess.Play;

/// <summary>
/// A time control. With <paramref name="MovesPerSession"/> null it is a Fischer control: each side
/// starts with the base time and gains the increment after every move it completes. With a session
/// ("40 moves in 5 minutes") each side gains the base time again whenever it completes a session's
/// worth of moves, unused time carrying over. Where a nullable <see cref="TimeControl"/> appears,
/// null means untimed play, with the engine limited by <see cref="Level"/> instead of a clock.
/// </summary>
public sealed record TimeControl(TimeSpan Base, TimeSpan Increment, int? MovesPerSession = null)
{
    public static TimeControl OfMinutes(int baseMinutes, int incrementSeconds)
    {
        return new TimeControl(TimeSpan.FromMinutes(baseMinutes), TimeSpan.FromSeconds(incrementSeconds));
    }

    /// <summary>
    /// The classic repeating control: the given number of moves in the given minutes, again and again.
    /// </summary>
    public static TimeControl Standard(int movesPerSession, int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(movesPerSession, 1);
        return new TimeControl(TimeSpan.FromMinutes(minutes), TimeSpan.Zero, movesPerSession);
    }

    /// <summary>
    /// The customary short forms: "40/5" for forty moves in five minutes, "5+3" for five minutes
    /// with a three second increment.
    /// </summary>
    public override string ToString()
    {
        string baseTime = Base.TotalMinutes >= 1 && Base.Seconds == 0
            ? ((int)Base.TotalMinutes).ToString(CultureInfo.InvariantCulture)
            : Base.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + "s";
        string increment = Increment.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        if (MovesPerSession is int moves)
        {
            string session = $"{moves.ToString(CultureInfo.InvariantCulture)}/{baseTime}";
            return Increment > TimeSpan.Zero ? $"{session}+{increment}" : session;
        }
        return $"{baseTime}+{increment}";
    }
}
