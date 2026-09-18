namespace StoopidChess.Core;

/// <summary>
/// The colour of a piece or of the side to move.
/// </summary>
public enum Colour : byte
{
    None = 0,
    White = 1,
    Black = 2,
}

public static class ColourExtensions
{
    /// <summary>
    /// The two sides, for iterating over both.
    /// </summary>
    public static ReadOnlySpan<Colour> Sides => [Colour.White, Colour.Black];

    /// <summary>
    /// The other side. <see cref="Colour.None"/> is returned unchanged.
    /// </summary>
    public static Colour Opponent(this Colour colour) => colour switch
    {
        Colour.White => Colour.Black,
        Colour.Black => Colour.White,
        _ => Colour.None,
    };
}
