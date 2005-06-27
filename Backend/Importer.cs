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

  public static Module GetModule(Node node)
  { Module module;
    try
    { if(node is VariableNode) module = TopLevel.Current.GetModule(((VariableNode)node).Name.String);
      else if(node is LiteralNode) module = GetModule((string)((LiteralNode)node).Value);
      else
      { CallNode cn = (CallNode)node;
        switch(((VariableNode)cn.Function).Name.String)
        { case "lib":
            if(cn.Args.Length!=2) goto bad;
            module = GetModule((string)((LiteralNode)cn.Args[0]).Value, (string)((LiteralNode)cn.Args[1]).Value);
            break;
          default: goto bad;
        }
      }
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
          { Type type = Type.GetType("NetLisp.Backend.Mods."+mp.Name);
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
      return ModuleGenerator.Generate(Parser.FromFile(path));
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

  public static Module Generate(Pair module)
  { Module ret = new Module("<code>");
    Builtins.eval(code.Parse());
    return ret;
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
}
#endregion

} // namespace NetLisp.Backend