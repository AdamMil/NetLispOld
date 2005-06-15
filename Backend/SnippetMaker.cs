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

  public static Snippet Generate(LambdaNode body) { return Generate(body, "code_"+index.Next); }
  public static Snippet Generate(LambdaNode body, string typeName)
  { TypeGenerator tg = Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed, typeName, typeof(Snippet));

    CodeGenerator cg = tg.DefineMethodOverride("Run", true);
    cg.Namespace = new TopLevelNamespace(cg);

    if(body.MaxNames!=0)
    { cg.Namespace = new LocalNamespace(cg.Namespace, cg);
      cg.EmitArgGet(0);
      cg.EmitInt(body.MaxNames);
      cg.EmitNew(typeof(LocalEnvironment), new Type[] { typeof(LocalEnvironment), typeof(int) });
      cg.EmitArgSet(0);
    }

    body.Body.Emit(cg);
    cg.Finish();
    return (Snippet)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }

  public static AssemblyGenerator Assembly = new AssemblyGenerator("snippets", "snippets.dll");
  
  static Index index = new Index();
}

} // namespace NetLisp.Backend
