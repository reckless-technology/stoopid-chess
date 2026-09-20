using StoopidChess.Core;
using Color = StoopidChess.Core.Color;   // The chess colour, not System.Drawing.Color

namespace StoopidChess.Gui;

/// <summary>
/// Modal dialog asking which piece a pawn should promote to. The choice is in <see cref="Choice"/>
/// once the dialog closes; closing it any other way promotes to a queen.
/// </summary>
internal sealed class PawnPromotionForm : Form
{
    private const int ImageSize = 64;
    private const int Gap = 12;

    public PawnPromotionForm(Color color)
    {
        Text = "Promote pawn to";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        int x = Gap;
        foreach (Piece type in PieceExtensions.PromotionTypes)
        {
            var picture = new PictureBox
            {
                Image = PieceImages.Get(color, type),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(x, Gap),
                Size = new Size(ImageSize, ImageSize),
                Cursor = Cursors.Hand,
                BorderStyle = BorderStyle.FixedSingle,
            };
            picture.Click += (_, _) =>
            {
                Choice = type;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(picture);
            x += ImageSize + Gap;
        }
        ClientSize = new Size(x, ImageSize + (2 * Gap));
    }

    public Piece Choice { get; private set; } = Piece.Queen;
}
