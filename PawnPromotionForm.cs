using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StoopidChess
{
    /// <summary>
    /// Class to present the modal dialog which allows the user to choose promotion of
    /// a pawn to either a knight or a queen
    /// </summary>
    public partial class PawnPromotionForm : Form
    {
        private Colour colour;
        private MainForm parent;

        internal PawnPromotionForm(Colour colour, MainForm parent)
        {
            InitializeComponent();
            this.parent = parent;
            this.colour = colour;
            this.ControlBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
        }

        private void knightPictureBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImage(parent.pieceBitmaps[(int)colour, (int)PieceType.KNIGHT], 0, 0, 64, 64);
        }

        private void queenPictureBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImage(parent.pieceBitmaps[(int)colour, (int)PieceType.QUEEN], 0, 0, 64, 64);
        }

        private void knightPictureBox_Click(object sender, EventArgs e)
        {
            parent.promotedPiece = PieceType.KNIGHT;
            this.Close();
        }

        private void queenPictureBox_Click(object sender, EventArgs e)
        {
            parent.promotedPiece = PieceType.QUEEN;
            this.Close();
        }
    }
}