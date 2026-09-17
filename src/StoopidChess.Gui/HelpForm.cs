using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace StoopidChess.Gui
{
    public partial class HelpForm : Form
    {
        public HelpForm()
        {
            InitializeComponent();
            this.ControlBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            okButton.Focus();
            textBox.Select(0, 0);
            this.Text = "Stoopid Chess " + Application.ProductVersion.Split('+')[0];
        }

        private void okButton_Click(object? sender, EventArgs e)
        {
            this.Close();
        }
    }
}
