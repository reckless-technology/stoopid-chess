using StoopidChess.Core;

namespace StoopidChess.Gui;

/// <summary>
/// Modal dialog asking which piece a pawn should promote to. The choice is in <see cref="Choice"/>
/// once the dialog closes; closing it any other way promotes to a queen.
/// </summary>
internal sealed class PawnPromotionForm : Form
{
    private const int ImageSize = 64;
    private const int Gap = 12;

    public PawnPromotionForm(Colour colour, PieceImages images)
    {
        ArgumentNullException.ThrowIfNull(images);
        Text = "Promote pawn to";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        int x = Gap;
        foreach (PieceType type in PieceTypeExtensions.PromotionTypes)
        {
            var picture = new PictureBox
            {
                Image = images[new Piece(colour, type)],
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(x, Gap),
                Size = new Size(ImageSize, ImageSize),
                Cursor = Cursors.Hand,
                BorderStyle = BorderStyle.FixedSingle,
                Tag = type,
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

    public PieceType Choice { get; private set; } = PieceType.Queen;
}
