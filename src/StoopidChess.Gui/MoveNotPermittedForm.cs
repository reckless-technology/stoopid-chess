using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StoopidChess.Gui
{
    /// <summary>
    /// Simple dialog box to inform the user of an illegal move attempt
    /// This is preferable to a simple MessageBox as the position of the form can be
    /// set to the center of the main form.
    /// </summary>
    public partial class MoveNotPermittedForm : Form
    {
        public MoveNotPermittedForm()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterParent;
            this.ControlBox = false;
        }

        private void okButton_Click(object? sender, EventArgs e)
        {
            this.Close();
        }
    }
}