using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StoopidChess.Core;

namespace StoopidChess.Desktop;

/// <summary>
/// The piece bitmaps from the embedded assets, indexed by color and piece type.
/// </summary>
internal static class PieceImages
{
    private static readonly Dictionary<(Color, Piece), Bitmap> Images = Load();

    private static Dictionary<(Color, Piece), Bitmap> Load()
    {
        var images = new Dictionary<(Color, Piece), Bitmap>();
        foreach (Color color in new[] { Color.White, Color.Black })
        {
            foreach (Piece type in new[] { Piece.Pawn, Piece.Knight, Piece.Bishop, Piece.Rook, Piece.Queen, Piece.King })
            {
                var uri = new Uri($"avares://StoopidChess.Desktop/Assets/{color}{type}.png");
                images[(color, type)] = new Bitmap(AssetLoader.Open(uri));
            }
        }
        return images;
    }

    public static Bitmap Get(Color color, Piece type) => Images[(color, type)];
}
