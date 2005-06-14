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

  public static Snippet Generate(Node body) { return Generate(body, "code_"+index.Next); }
  public static Snippet Generate(Node body, string typeName)
  { TypeGenerator tg = Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed, typeName, typeof(Snippet));
    CodeGenerator cg = tg.DefineMethodOverride("Run", true);
    cg.Namespace = new TopLevelNamespace(cg);
    body.Emit(cg);
    cg.Finish();
    return (Snippet)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }

  public static AssemblyGenerator Assembly = new AssemblyGenerator("snippets", "snippets.dll");
  
  static Index index = new Index();
}

} // namespace NetLisp.Backend
