using System.Globalization;

namespace StoopidChess.Play;

/// <summary>
/// A chess clock's setting, the Fischer control: each side starts with the base time, the clock of the
/// side to move counts down, and a side gains the increment after every move it completes. A side whose
/// clock reaches zero loses on time.
/// </summary>
public sealed record TimeControl(TimeSpan Base, TimeSpan Increment)
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
    /// The customary short form: "5+3" is five minutes with a three second increment.
    /// </summary>
    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{(int)Base.TotalMinutes}+{(int)Increment.TotalSeconds}");
    }
}
