using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StoopidChess
{
    public partial class IllegalPositionForm : Form
    {
        public IllegalPositionForm()
        {
            InitializeComponent();
            this.ControlBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
