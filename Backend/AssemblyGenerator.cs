using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics.SymbolStore;

namespace NetLisp.Backend
{

public sealed class AssemblyGenerator
{ public AssemblyGenerator(string moduleName) : this(moduleName, Options.Debug) { }
  public AssemblyGenerator(string moduleName, bool debug)
    : this(moduleName, "_assembly"+index.Next.ToString()+".dll", debug) { }
  public AssemblyGenerator(string moduleName, string outFileName) : this(moduleName, outFileName, Options.Debug) { }
  public AssemblyGenerator(string moduleName, string outFileName, bool debug)
  { string dir = System.IO.Path.GetDirectoryName(outFileName);
    if(dir=="") dir=null;
    outFileName = System.IO.Path.GetFileName(outFileName);

    AssemblyName an = new AssemblyName();
    an.Name  = moduleName;
    Assembly = AppDomain.CurrentDomain
                 .DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave, dir, null, null, null, null, true);
    Module   = Assembly.DefineDynamicModule(outFileName, outFileName, debug);
    Symbols  = debug ? Module.DefineDocument(outFileName, Guid.Empty, Guid.Empty, SymDocumentType.Text) : null;
    OutFileName = outFileName;
  }

  public TypeGenerator DefineType(string name) { return DefineType(TypeAttributes.Public, name, null); }
  public TypeGenerator DefineType(string name, Type parent)
  { return DefineType(TypeAttributes.Public, name, parent);
  }
  public TypeGenerator DefineType(TypeAttributes attrs, string name, Type parent)
  { return new TypeGenerator(this, Module.DefineType(name, attrs, parent));
  }

  public Snippet GenerateSnippet(LambdaNode body) { return GenerateSnippet(body, "code_"+index.Next); }
  public Snippet GenerateSnippet(LambdaNode body, string typeName)
  { TypeGenerator tg = DefineType(TypeAttributes.Public|TypeAttributes.Sealed, typeName, typeof(Snippet));

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

  public void Save() { Assembly.Save(OutFileName); }

  public AssemblyBuilder Assembly;
  public ModuleBuilder   Module;
  public ISymbolDocumentWriter Symbols;
  public string OutFileName;
  
  static Index index = new Index();
}

} // namespace NetLisp.Backend
