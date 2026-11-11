using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop;

/// <summary>
/// The piece pictures from the embedded assets (the cburnett set, 64 px PNGs), decoded once, by color and piece type.
/// </summary>
internal static class PieceImages
{
    private static readonly Dictionary<(Color, Piece), SKImage> Images = Load();

    private static Dictionary<(Color, Piece), SKImage> Load()
    {
        var images = new Dictionary<(Color, Piece), SKImage>();
        foreach (Color color in new[] { Color.White, Color.Black })
        {
            foreach (
                Piece type in new[] { Piece.Pawn, Piece.Knight, Piece.Bishop, Piece.Rook, Piece.Queen, Piece.King }
            )
            {
                images[(color, type)] = Assets.Image($"{color}{type}.png");
            }
        }
        return images;
    }

    /// <summary>
    /// The picture of a <paramref name="color"/> <paramref name="type"/>.
    /// </summary>
    public static SKImage Get(Color color, Piece type) => Images[(color, type)];
}
