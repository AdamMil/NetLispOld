using System;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

public abstract class Snippet
{ public abstract object Run(LocalEnvironment env);
}

public sealed class SnippetMaker
{ private SnippetMaker() { }

  public static void DumpAssembly()
  { Assembly.Save();
    string bn = "snippets"+index.Next;
    Assembly = new AssemblyGenerator(bn, bn+".dll");
  }

  public static Snippet Generate(LambdaNode body) { return Assembly.GenerateSnippet(body); }
  public static Snippet Generate(LambdaNode body, string typeName)
  { return Assembly.GenerateSnippet(body, typeName);
  }

  public static AssemblyGenerator Assembly = new AssemblyGenerator("snippets", "snippets.dll");
  
  static Index index = new Index();
}

} // namespace NetLisp.Backend
