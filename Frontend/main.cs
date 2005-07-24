using System;
using System.Collections;
using NetLisp.Backend;

namespace NetLisp.Frontend
{

public class App
{ int foo()
  { try { return 5; } catch { throw; }
  }
  static void Main()
  { Options.Debug = true;
    Options.Optimize = true;

    TopLevel.Current = new TopLevel();
    Builtins.Instance.ImportAll(TopLevel.Current);
    
    while(true)
    { string code = null;
      int  parens = 0;
      do
      { Console.Write(code==null ? ">>> " : "... ");
        string line = Console.ReadLine();
        if(line==null) goto done;
        for(int i=0; i<line.Length; i++)
          if(line[i]=='(') parens++;
          else if(line[i]==')') parens--;
        code += line;
      } while(parens>0);

      if(code.Trim().Length==0) continue;
      try { Console.WriteLine(Ops.Repr(Builtins.eval(Parser.FromString(code).Parse()))); }
      catch(Exception e) { Console.WriteLine("ERROR: "+e.ToString()); }
    }
    done:
    SnippetMaker.DumpAssembly();
  }
}

} // namespace NetLisp.Frontend