namespace StoopidChess
{
    partial class PawnPromotionForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.knightPictureBox = new System.Windows.Forms.PictureBox();
            this.queenPictureBox = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.knightPictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.queenPictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // knightPictureBox
            // 
            this.knightPictureBox.Location = new System.Drawing.Point(12, 12);
            this.knightPictureBox.Name = "knightPictureBox";
            this.knightPictureBox.Size = new System.Drawing.Size(64, 64);
            this.knightPictureBox.TabIndex = 0;
            this.knightPictureBox.TabStop = false;
            this.knightPictureBox.Click += new System.EventHandler(this.knightPictureBox_Click);
            this.knightPictureBox.Paint += new System.Windows.Forms.PaintEventHandler(this.knightPictureBox_Paint);
            // 
            // queenPictureBox
            // 
            this.queenPictureBox.Location = new System.Drawing.Point(107, 12);
            this.queenPictureBox.Name = "queenPictureBox";
            this.queenPictureBox.Size = new System.Drawing.Size(64, 64);
            this.queenPictureBox.TabIndex = 1;
            this.queenPictureBox.TabStop = false;
            this.queenPictureBox.Click += new System.EventHandler(this.queenPictureBox_Click);
            this.queenPictureBox.Paint += new System.Windows.Forms.PaintEventHandler(this.queenPictureBox_Paint);
            // 
            // PawnPromotionForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(183, 92);
            this.Controls.Add(this.queenPictureBox);
            this.Controls.Add(this.knightPictureBox);
            this.Name = "PawnPromotionForm";
            this.Text = "Choose";
            ((System.ComponentModel.ISupportInitialize)(this.knightPictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.queenPictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.PictureBox knightPictureBox;
        private System.Windows.Forms.PictureBox queenPictureBox;

    }
}