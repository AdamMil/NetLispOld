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
    string bn = "snippets"+AST.NextIndex;
    Assembly = new AssemblyGenerator(bn, bn+".dll");
  }

  public static Snippet Generate(Node body) { return Generate(body, "code_"+AST.NextIndex); }
  public static Snippet Generate(Node body, string typeName)
  { TypeGenerator tg = Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed, typeName, typeof(Snippet));
    CodeGenerator cg = tg.DefineMethodOverride(typeof(Snippet).GetMethod("Run"), true);
    cg.Namespace = new TopLevelNamespace(cg);
    body.Emit(cg);
    cg.Finish();
    return (Snippet)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }

  public static AssemblyGenerator Assembly = new AssemblyGenerator("snippets", "snippets.dll");
}

} // namespace NetLisp.Backend
