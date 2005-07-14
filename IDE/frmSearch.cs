using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;

namespace NetLisp.IDE
{

public class SearchForm : Form
{
  System.Windows.Forms.TextBox txtFind;
  System.Windows.Forms.Label lblReplace;
  System.Windows.Forms.TextBox txtReplace;
  System.Windows.Forms.CheckBox chkCase;
  System.Windows.Forms.Button btnFind;
  System.Windows.Forms.Button btnReplace;
  System.Windows.Forms.Button btnReplaceAll;
  System.Windows.Forms.GroupBox grpDirection;
  System.Windows.Forms.RadioButton radUp;
  System.Windows.Forms.RadioButton radDown;
  System.Windows.Forms.Label lblFind;

	public SearchForm()
	{ InitializeComponent();
	}

	#region Windows Form Designer generated code
	void InitializeComponent()
	{
    System.Resources.ResourceManager resources = new System.Resources.ResourceManager(typeof(SearchForm));
    this.txtFind = new System.Windows.Forms.TextBox();
    this.lblFind = new System.Windows.Forms.Label();
    this.lblReplace = new System.Windows.Forms.Label();
    this.txtReplace = new System.Windows.Forms.TextBox();
    this.chkCase = new System.Windows.Forms.CheckBox();
    this.btnFind = new System.Windows.Forms.Button();
    this.btnReplace = new System.Windows.Forms.Button();
    this.btnReplaceAll = new System.Windows.Forms.Button();
    this.grpDirection = new System.Windows.Forms.GroupBox();
    this.radUp = new System.Windows.Forms.RadioButton();
    this.radDown = new System.Windows.Forms.RadioButton();
    this.grpDirection.SuspendLayout();
    this.SuspendLayout();
    // 
    // txtFind
    // 
    this.txtFind.Location = new System.Drawing.Point(76, 5);
    this.txtFind.Name = "txtFind";
    this.txtFind.Size = new System.Drawing.Size(244, 20);
    this.txtFind.TabIndex = 0;
    this.txtFind.Text = "";
    // 
    // lblFind
    // 
    this.lblFind.Location = new System.Drawing.Point(0, 7);
    this.lblFind.Name = "lblFind";
    this.lblFind.Size = new System.Drawing.Size(56, 16);
    this.lblFind.TabIndex = 1;
    this.lblFind.Text = "Find what:";
    this.lblFind.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
    // 
    // lblReplace
    // 
    this.lblReplace.Location = new System.Drawing.Point(0, 31);
    this.lblReplace.Name = "lblReplace";
    this.lblReplace.Size = new System.Drawing.Size(72, 16);
    this.lblReplace.TabIndex = 2;
    this.lblReplace.Text = "Replace with:";
    this.lblReplace.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
    // 
    // txtReplace
    // 
    this.txtReplace.Location = new System.Drawing.Point(75, 29);
    this.txtReplace.Name = "txtReplace";
    this.txtReplace.Size = new System.Drawing.Size(245, 20);
    this.txtReplace.TabIndex = 2;
    this.txtReplace.Text = "";
    // 
    // chkCase
    // 
    this.chkCase.Location = new System.Drawing.Point(8, 68);
    this.chkCase.Name = "chkCase";
    this.chkCase.Size = new System.Drawing.Size(88, 16);
    this.chkCase.TabIndex = 5;
    this.chkCase.Text = "Match &case";
    // 
    // btnFind
    // 
    this.btnFind.Location = new System.Drawing.Point(328, 5);
    this.btnFind.Name = "btnFind";
    this.btnFind.Size = new System.Drawing.Size(80, 23);
    this.btnFind.TabIndex = 1;
    this.btnFind.Text = "&Find next";
    // 
    // btnReplace
    // 
    this.btnReplace.Location = new System.Drawing.Point(328, 35);
    this.btnReplace.Name = "btnReplace";
    this.btnReplace.Size = new System.Drawing.Size(80, 23);
    this.btnReplace.TabIndex = 3;
    this.btnReplace.Text = "&Replace";
    // 
    // btnReplaceAll
    // 
    this.btnReplaceAll.Location = new System.Drawing.Point(328, 63);
    this.btnReplaceAll.Name = "btnReplaceAll";
    this.btnReplaceAll.Size = new System.Drawing.Size(80, 23);
    this.btnReplaceAll.TabIndex = 4;
    this.btnReplaceAll.Text = "Replace &All";
    // 
    // grpDirection
    // 
    this.grpDirection.Controls.Add(this.radDown);
    this.grpDirection.Controls.Add(this.radUp);
    this.grpDirection.Location = new System.Drawing.Point(208, 56);
    this.grpDirection.Name = "grpDirection";
    this.grpDirection.Size = new System.Drawing.Size(112, 40);
    this.grpDirection.TabIndex = 6;
    this.grpDirection.TabStop = false;
    this.grpDirection.Text = "Direction";
    // 
    // radUp
    // 
    this.radUp.Location = new System.Drawing.Point(8, 16);
    this.radUp.Name = "radUp";
    this.radUp.Size = new System.Drawing.Size(40, 16);
    this.radUp.TabIndex = 0;
    this.radUp.Text = "&Up";
    // 
    // radDown
    // 
    this.radDown.Location = new System.Drawing.Point(56, 16);
    this.radDown.Name = "radDown";
    this.radDown.Size = new System.Drawing.Size(52, 16);
    this.radDown.TabIndex = 1;
    this.radDown.Text = "&Down";
    // 
    // SearchForm
    // 
    this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
    this.ClientSize = new System.Drawing.Size(414, 98);
    this.Controls.Add(this.grpDirection);
    this.Controls.Add(this.btnReplaceAll);
    this.Controls.Add(this.btnReplace);
    this.Controls.Add(this.btnFind);
    this.Controls.Add(this.chkCase);
    this.Controls.Add(this.txtReplace);
    this.Controls.Add(this.lblReplace);
    this.Controls.Add(this.lblFind);
    this.Controls.Add(this.txtFind);
    this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
    this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
    this.MaximizeBox = false;
    this.MinimizeBox = false;
    this.Name = "SearchForm";
    this.Text = "Find";
    this.grpDirection.ResumeLayout(false);
    this.ResumeLayout(false);

  }
	#endregion
}

} // namespace NetLisp.IDE