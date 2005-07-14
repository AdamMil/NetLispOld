using System;
using System.IO;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;

namespace NetLisp.IDE
{

public class OutputForm : System.Windows.Forms.Form
{ public OutputForm()
	{ InitializeComponent();
		Console.SetOut(new Writer(textBox));
	}

  sealed class Writer : TextWriter
  { public Writer(System.Windows.Forms.TextBox textBox) { box=textBox; }

    public override System.Text.Encoding Encoding
    { get { return System.Text.Encoding.Unicode; }
    }

    public override void Write(char value)
    { if(App.MainForm.RedirectStdout)
      { if(value=='\r') return; // avoid a bug in ICSharpCode.TextEditor that causes two newlines to be added
        EditForm form = App.MainForm.ActiveMdiChild as EditForm;
        if(form!=null)
        { ICSharpCode.TextEditor.Document.IDocument doc = form.immediate.Document;
          doc.Insert(doc.TextLength, value.ToString());
          return;
        }
      }

      bool end = box.SelectionStart==box.TextLength;
      box.AppendText(value.ToString());
      if(end)
      { box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
      }
    }
    
    public override void Write(string value)
    { if(App.MainForm.RedirectStdout)
      { EditForm form = App.MainForm.ActiveMdiChild as EditForm;
        if(form!=null)
        { ICSharpCode.TextEditor.Document.IDocument doc = form.immediate.Document;
          doc.Insert(doc.TextLength, value);
          return;
        }
      }

      bool end = box.SelectionStart==box.TextLength;
      box.AppendText(value);
      if(end)
      { box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
      }
    }

    System.Windows.Forms.TextBox box;
  }

  System.Windows.Forms.TextBox textBox;

	#region Windows Form Designer generated code
	void InitializeComponent()
	{
    System.Resources.ResourceManager resources = new System.Resources.ResourceManager(typeof(OutputForm));
    this.textBox = new System.Windows.Forms.TextBox();
    this.SuspendLayout();
    // 
    // textBox
    // 
    this.textBox.Dock = System.Windows.Forms.DockStyle.Fill;
    this.textBox.Font = new System.Drawing.Font("Courier New", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));
    this.textBox.Location = new System.Drawing.Point(0, 0);
    this.textBox.Multiline = true;
    this.textBox.Name = "textBox";
    this.textBox.Size = new System.Drawing.Size(576, 197);
    this.textBox.TabIndex = 0;
    this.textBox.Text = "";
    // 
    // OutputForm
    // 
    this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
    this.ClientSize = new System.Drawing.Size(576, 197);
    this.Controls.Add(this.textBox);
    this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
    this.Name = "OutputForm";
    this.Text = "Console Output";
    this.ResumeLayout(false);
  }
	#endregion
	
  protected override void OnClosing(CancelEventArgs e)
  { Hide();
    e.Cancel = true;
    base.OnClosing(e);
  }

}

} // namespace NetLisp.IDE