using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using StoopidChess.Core;
using StoopidChess.Play;
using Color = StoopidChess.Core.Color;   // The chess color, not Avalonia.Media.Color

namespace StoopidChess.Desktop;

/// <summary>
/// The captured pieces, white's losses in the top row and black's below.
/// </summary>
internal sealed class CaptureBinControl : Control
{
    public GameController? Controller { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Controller is not { } controller || controller.IsEditing)
        {
            return;
        }
        int size = (int)Math.Min(Bounds.Width / 16, Bounds.Height / 2);
        if (size <= 0)
        {
            return;
        }
        foreach (Color color in new[] { Color.White, Color.Black })
        {
            int y = color == Color.White ? 0 : size;
            int x = 0;
            foreach (Piece captured in controller.Game.CapturedPieces(color))
            {
                context.DrawImage(PieceImages.Get(color, captured), new Rect(x, y, size, size));
                x += size;
            }
        }
    }
}
