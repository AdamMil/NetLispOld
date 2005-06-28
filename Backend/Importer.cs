/*
(module name base
  form ...)

(fluid-let ((*package* (get-package name)))
  (use-package base)
  form ...)
*/
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region Importer
public sealed class Importer
{ Importer() { }

  public static Module GetModule(object obj)
  { Module module;
    try
    { if(obj is Symbol) module = TopLevel.Current.GetModule(((Symbol)obj).Name);
      else if(obj is string) module = GetModule((string)obj);
      else if(obj is Pair)
      { Pair pair = (Pair)obj;
        switch(((Symbol)pair.Car).Name)
        { case "lib":
            if(Builtins.length.core(pair)!=3) goto bad;
            pair = (Pair)pair.Cdr;
            module = GetModule((string)pair.Car, (string)Ops.FastCadr(pair));
            break;
          default: goto bad;
        }
      }
      else goto bad;
    }
    catch { goto bad; }

    if(module==null) throw new SyntaxErrorException("module not found");
    return module;

    bad: throw new SyntaxErrorException("malformed module name");
  }
  
  public static Module GetModule(string path)
  { path = Path.GetFullPath(currentDir==null ? path : Path.Combine(currentDir, path));
    return GetModule(new ModulePath("%fs", path));
  }
  public static Module GetModule(string name, string collection) { return GetModule(new ModulePath(collection, name)); }

  struct ModulePath
  { public ModulePath(string collection, string name) { Collection=collection; Name=name; }

    public override bool Equals(object obj)
    { ModulePath mp = (ModulePath)obj;
      return mp.Name==Name && mp.Collection==Collection;
    }
    public override int GetHashCode() { return Collection.GetHashCode() ^ Name.GetHashCode(); }

    public string Collection, Name;
  }

  static Module GetModule(ModulePath mp)
  { Module module = (Module)modules[mp];

    if(module==null)
    { if(modules.Contains(mp)) throw new Exception("circular module requirements");
      modules[mp] = null;

      switch(mp.Collection)
      { case "%fs": module = LoadModuleFromFile(mp.Name); break;
        case "%builtin":
        { if(mp.Name=="Builtins") module = Builtins.Instance;
          else
          { Type type = Type.GetType("NetLisp.Mods."+mp.Name);
            if(type!=null) module = ModuleGenerator.Generate(type);
          }
          break;
        }
        default: module = LoadModuleFromFile("lib/"+mp.Name); break; // TODO: get the library path from elsewhere
      }

      if(module!=null) modules[mp] = module;
      else modules.Remove(mp);
    }
    return module;
  }

  static Module LoadModuleFromFile(string path)
  { string old = currentDir;
    try
    { currentDir = Path.GetDirectoryName(path);
      if(!File.Exists(path)) return null;
      Parser p = Parser.FromFile(path);
      object obj;
      ModuleNode mod;
      if((obj=p.ParseOne())==Parser.EOF || (mod=AST.Create(obj).Body as ModuleNode)==null ||
         (obj=p.ParseOne())!=Parser.EOF)
        throw new SyntaxErrorException("module file must contain a single module declaration");
      return ModuleGenerator.Generate(mod);
    }
    finally { currentDir = old; }
  }

  static string currentDir;
  static Hashtable modules = new Hashtable();
}
#endregion

#region ModuleGenerator
public sealed class ModuleGenerator
{ ModuleGenerator() { }

  public static Module Generate(ModuleNode mod)
  { TypeGenerator tg = SnippetMaker.Assembly.DefineType("module"+index.Next+"$"+mod.Name, typeof(Module));

    CodeGenerator cg = tg.DefineStaticMethod(MethodAttributes.Private, "Run", typeof(object),
                                             new Type[] { typeof(LocalEnvironment) });
    cg.Namespace = new TopLevelNamespace(cg);
    if(mod.MaxNames!=0)
    { cg.Namespace = new LocalNamespace(cg.Namespace, cg);
      cg.EmitArgGet(0);
      cg.EmitInt(mod.MaxNames);
      cg.EmitNew(typeof(LocalEnvironment), new Type[] { typeof(LocalEnvironment), typeof(int) });
      cg.EmitArgSet(0);
    }
    mod.Body.Emit(cg);
    cg.Finish();

    MethodBase run = cg.MethodBase;
    cg = tg.DefineConstructor(Type.EmptyTypes);
    cg.EmitThis();
    cg.EmitString(mod.Name);
    cg.EmitCall(typeof(Module).GetConstructor(new Type[] { typeof(string) }));

    ConstructorInfo sci=typeof(Module.Export).GetConstructor(new Type[] { typeof(string) }),
                   ssci=typeof(Module.Export).GetConstructor(new Type[] { typeof(string), typeof(string) }),
                   snci=typeof(Module.Export).GetConstructor(new Type[] { typeof(string), typeof(TopLevel.NS) }),
                  ssnci=typeof(Module.Export).GetConstructor(new Type[] { typeof(string), typeof(string), typeof(TopLevel.NS) });
    cg.EmitThis();
    cg.EmitNewArray(typeof(Module.Export), mod.Exports.Length);
    for(int i=0; i<mod.Exports.Length; i++)
    { Module.Export e = mod.Exports[i];
      cg.ILG.Emit(OpCodes.Dup);
      cg.EmitInt(i);
      cg.ILG.Emit(OpCodes.Ldelema, typeof(Module.Export));
      cg.EmitString(e.Name);
      if(e.Name!=e.AsName) cg.EmitString(e.AsName);
      if(e.NS==TopLevel.NS.Main) cg.EmitNew(e.Name==e.AsName ? sci : ssci);
      else
      { cg.EmitInt((int)e.NS);
        cg.EmitNew(e.Name==e.AsName ? snci : ssnci);
      }
      cg.ILG.Emit(OpCodes.Stobj, typeof(Module.Export));
    }
    cg.EmitFieldSet(typeof(Module), "Exports");

    Slot old = cg.AllocLocalTemp(typeof(TopLevel));
    cg.EmitFieldGet(typeof(TopLevel), "Current");
    old.EmitSet(cg);
    cg.ILG.BeginExceptionBlock();
    cg.EmitFieldGet(typeof(Builtins), "Instance");
    cg.EmitThis();
    cg.EmitFieldGet(typeof(Module), "TopLevel");
    cg.ILG.Emit(OpCodes.Dup);
    cg.EmitFieldSet(typeof(TopLevel), "Current");
    cg.EmitCall(typeof(Module), "ImportAll");
    cg.ILG.Emit(OpCodes.Ldnull);
    cg.EmitCall((MethodInfo)run);
    cg.ILG.Emit(OpCodes.Pop);
    cg.ILG.BeginFinallyBlock();
    old.EmitGet(cg);
    cg.EmitFieldSet(typeof(TopLevel), "Current");
    cg.ILG.EndExceptionBlock();
    cg.EmitReturn();
    cg.Finish();

    return (Module)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }

  public static Module Generate(Type type)
  { Module ret = new Module(type.FullName);
    ret.AddBuiltins(type);

    object[] attrs = type.GetCustomAttributes(typeof(LispCodeAttribute), false);
    if(attrs.Length!=0)
    { TopLevel old = TopLevel.Current;
      try
      { TopLevel.Current = ret.TopLevel;
        Builtins.eval(Parser.FromString(((LispCodeAttribute)attrs[0]).Code).Parse());
      }
      finally { TopLevel.Current = old; }
    }

    ret.CreateExports();
    return ret;
  }
  
  static Index index = new Index();
}
#endregion

} // namespace NetLisp.Backend