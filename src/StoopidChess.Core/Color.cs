using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// The color of a piece or of the side to move. White is 0 and black 1, so a color indexes a
/// two-entry table directly and the opponent is the other bit.
/// </summary>
public enum Color : byte
{
    White = 0,
    Black = 1,
}

public static class ColorExtensions
{
    /// <summary>
    /// The two sides, for iterating over both.
    /// </summary>
    public static ReadOnlySpan<Color> Sides => [Color.White, Color.Black];

    /// <summary>
    /// The other side.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Opponent(this Color color) => (Color)((int)color ^ 1);
}
