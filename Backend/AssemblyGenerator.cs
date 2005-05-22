using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics.SymbolStore;

namespace NetLisp.Backend
{

public sealed class AssemblyGenerator
{ public AssemblyGenerator(string moduleName, string outFileName) : this(moduleName, outFileName, Options.Debug) { }
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

  public void Save() { Assembly.Save(OutFileName); }

  public AssemblyBuilder Assembly;
  public ModuleBuilder   Module;
  public ISymbolDocumentWriter Symbols;
  public string OutFileName;
}

} // namespace NetLisp.Backend
