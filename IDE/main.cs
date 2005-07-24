using System;
using System.Collections;
using System.IO;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyKeyFile("../../../NetLisp.snk")]

namespace NetLisp.IDE
{

class App
{ public static readonly MainForm MainForm = new MainForm();

  public static string[] GetRawLines(TextBoxBase box)
  { ArrayList list = new ArrayList();
    string text = box.Text;
    int pos=0;
    while(pos<text.Length)
    { int index = text.IndexOf('\n', pos);
      if(index==-1) { list.Add(text.Substring(pos)); break; }
      else { list.Add(text.Substring(pos, index-pos+1)); pos=index+1; }
    }
    return (string[])list.ToArray(typeof(string));
  }

  [STAThread]
  static void Main()
  { NetLisp.Backend.Options.Debug = true;
    NetLisp.Backend.Options.Optimize = true;

    Application.ThreadException += new System.Threading.ThreadExceptionEventHandler(Application_ThreadException);
    Application.Run(MainForm);
    
    NetLisp.Backend.SnippetMaker.DumpAssembly();
  }

  static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
  { ExceptionForm form = new ExceptionForm(e.Exception);
    form.ShowDialog();
  }
}

} // namespace NetLisp.IDE