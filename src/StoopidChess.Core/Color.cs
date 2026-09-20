using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// The color of a piece or of the side to move.
/// </summary>
public enum Color : byte
{
    None = 0,
    White = 1,
    Black = 2,
}

public static class ColorExtensions
{
    /// <summary>
    /// The two sides, for iterating over both.
    /// </summary>
    public static ReadOnlySpan<Color> Sides => [Color.White, Color.Black];

    /// <summary>
    /// The other side. <see cref="Color.None"/> is returned unchanged.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Opponent(this Color color) => color switch
    {
        Color.White => Color.Black,
        Color.Black => Color.White,
        _ => Color.None,
    };
}
