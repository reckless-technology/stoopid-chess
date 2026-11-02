using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Dialogs;

/// <summary>
/// Asks which piece a pawn promotes to: a plate for each, the piece drawn on it. The choice is in
/// <see cref="Choice"/> once the dialog closes; closing it any other way (Escape) promotes to a queen.
/// </summary>
internal sealed class PromotionDialog : DialogView
{
    private const float PlateSide = 76.0f;
    private const float Gap = 12.0f;

    private readonly List<PlateButton> pieces = [];

    public PromotionDialog(Color color) : base("Promote pawn to")
    {
        foreach (Piece type in PieceExtensions.PromotionTypes)
        {
            var button = new PlateButton(type.ToString()) { Image = PieceImages.Get(color, type) };
            button.Clicked += () =>
            {
                Choice = type;
                Close();
            };
            pieces.Add(button);
            AddChild(button);
        }
    }

    /// <summary>The piece chosen.</summary>
    public Piece Choice { get; private set; } = Piece.Queen;

    /// <summary>The plates, in <see cref="PieceExtensions.PromotionTypes"/> order.</summary>
    public IReadOnlyList<PlateButton> Pieces => pieces;

    protected override SKSize ContentSize => new((pieces.Count * PlateSide) + ((pieces.Count - 1) * Gap), PlateSide);

    protected override void LayoutContent(SKRect area)
    {
        float x = area.Left + ((area.Width - ContentSize.Width) / 2);
        foreach (PlateButton piece in pieces)
        {
            piece.SetBounds(SKRect.Create(x, area.Top, PlateSide, PlateSide));
            x += PlateSide + Gap;
        }
    }
}
