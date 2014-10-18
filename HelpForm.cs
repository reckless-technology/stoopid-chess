using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace StoopidChess
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
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            this.Text = "Stoopid Chess " + version.ToString();
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
