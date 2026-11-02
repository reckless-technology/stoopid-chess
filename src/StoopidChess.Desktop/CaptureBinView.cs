using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Desktop;

/// <summary>
/// The captured pieces under the board, white's losses in the top row and black's below.
/// </summary>
internal sealed class CaptureBinView : View
{
    /// <summary>The game shown.</summary>
    public GameController? Controller { get; set; }

    /// <summary>A piece's side: sixteen to a row, two rows.</summary>
    public float PieceSize => MathF.Floor(Math.Min(Width / 16, Height / 2));

    protected override void Draw(SKCanvas canvas)
    {
        float size = PieceSize;
        if (Controller is not { } controller || controller.IsEditing || size <= 0)
        {
            return;
        }
        foreach (Color color in new[] { Color.White, Color.Black })
        {
            float y = color == Color.White ? 0 : size;
            float x = 0;
            foreach (Piece captured in controller.Game.CapturedPieces(color))
            {
                Drawing.Image(canvas, PieceImages.Get(color, captured), SKRect.Create(x, y, size, size));
                x += size;
            }
        }
    }
}
