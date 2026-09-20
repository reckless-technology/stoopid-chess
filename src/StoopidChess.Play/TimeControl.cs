namespace StoopidChess.Play;

/// <summary>
/// A Fischer time control: each side starts with the base time and gains the increment after every
/// move it completes. Where a nullable <see cref="TimeControl"/> appears, null means untimed play,
/// with the engine limited by <see cref="Level"/> instead of a clock.
/// </summary>
public sealed record TimeControl(TimeSpan Base, TimeSpan Increment)
{
    public static TimeControl OfMinutes(int baseMinutes, int incrementSeconds)
    {
        return new TimeControl(TimeSpan.FromMinutes(baseMinutes), TimeSpan.FromSeconds(incrementSeconds));
    }

    /// <summary>
    /// The customary short form, base minutes "+" increment seconds: "5+3", "10+0".
    /// </summary>
    public override string ToString()
    {
        string baseTime = Base.TotalMinutes >= 1 && Base.Seconds == 0
            ? ((int)Base.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : Base.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s";
        return $"{baseTime}+{Increment.TotalSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
