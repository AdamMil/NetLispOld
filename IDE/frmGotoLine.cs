using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;

namespace NetLisp.IDE
{

public class GotoLineForm : Form
{
  System.Windows.Forms.TextBox textBox;
  System.Windows.Forms.Label lblLine;
  System.Windows.Forms.Button btnGo;

	public GotoLineForm()
	{ InitializeComponent();
	  textBox.Focus();
	}

  public int Line
  { get
    { string text = textBox.Text.Trim();
      if(text=="") return -1;
      try { return int.Parse(text); }
      catch(FormatException) { return -1; }
    }
  }

	#region Windows Form Designer generated code
	void InitializeComponent()
	{
    this.lblLine = new System.Windows.Forms.Label();
    this.textBox = new System.Windows.Forms.TextBox();
    this.btnGo = new System.Windows.Forms.Button();
    this.SuspendLayout();
    // 
    // lblLine
    // 
    this.lblLine.Location = new System.Drawing.Point(4, 6);
    this.lblLine.Name = "lblLine";
    this.lblLine.Size = new System.Drawing.Size(32, 16);
    this.lblLine.TabIndex = 0;
    this.lblLine.Text = "Line:";
    this.lblLine.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
    // 
    // textBox
    // 
    this.textBox.Location = new System.Drawing.Point(40, 4);
    this.textBox.MaxLength = 9;
    this.textBox.Name = "textBox";
    this.textBox.TabIndex = 1;
    this.textBox.Text = "";
    this.textBox.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.textBox_KeyPress);
    // 
    // btnGo
    // 
    this.btnGo.DialogResult = System.Windows.Forms.DialogResult.OK;
    this.btnGo.Location = new System.Drawing.Point(152, 3);
    this.btnGo.Name = "btnGo";
    this.btnGo.Size = new System.Drawing.Size(40, 23);
    this.btnGo.TabIndex = 2;
    this.btnGo.Text = "&Go";
    // 
    // GotoLineForm
    // 
    this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
    this.ClientSize = new System.Drawing.Size(200, 29);
    this.Controls.Add(this.btnGo);
    this.Controls.Add(this.textBox);
    this.Controls.Add(this.lblLine);
    this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
    this.KeyPreview = true;
    this.MaximizeBox = false;
    this.MinimizeBox = false;
    this.Name = "GotoLineForm";
    this.ShowInTaskbar = false;
    this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
    this.Text = "Goto line";
    this.ResumeLayout(false);

  }
	#endregion

  protected override void OnKeyPress(KeyPressEventArgs e)
  { if(e.KeyChar=='\r') btnGo.PerformClick();
    base.OnKeyPress(e);
  }

  void textBox_KeyPress(object sender, System.Windows.Forms.KeyPressEventArgs e)
  { if(!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled=true;
  }
}

} // namespace NetLisp.IDE
