using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace StoopidChess.Gui;

public partial class HelpForm : Form
{
    public HelpForm()
    {
        InitializeComponent();
        ControlBox = false;
        StartPosition = FormStartPosition.CenterParent;
        okButton.Focus();
        textBox.Select(0, 0);
        Text = "Stoopid Chess " + Application.ProductVersion.Split('+')[0];
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        Close();
    }
}
