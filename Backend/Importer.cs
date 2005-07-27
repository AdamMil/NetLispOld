/*
NetLisp is the reference implementation for a language similar to
Scheme, also called NetLisp. This implementation is both interpreted
and compiled, targetting the Microsoft .NET Framework.

http://www.adammil.net/
Copyright (C) 2005 Adam Milazzo

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using System;
using System.Collections;
using System.IO;
using System.Reflection;

namespace NetLisp.Backend
{

public sealed class Importer
{ Importer() { }
  static Importer()
  { builtins["srfi/srfi-1"] = typeof(Mods.Srfi1);
  }

  // TODO: this is only a temporary solution. replace it with a better one
  public static string LibPath = "lib/";

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

    if(module==null) throw new ModuleLoadException("module not found");
    return module;

    bad: throw new SyntaxErrorException("malformed module name");
  }
  
  public static Module GetModule(Type type)
  { ModulePath mp = new ModulePath(type.FullName, "%_builtin");
    lock(modules)
    { Module module = (Module)modules[mp];
      if(module==null) modules[mp] = module = ModuleGenerator.Generate(type);
      return module;
    }
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
  { lock(modules)
    { Module module = (Module)modules[mp];

      if(module==null)
      { if(modules.Contains(mp)) throw new ModuleLoadException("circular module requirements"); // TODO: improve this message
        modules[mp] = null;

        try
        { switch(mp.Collection)
          { case "%fs": module = LoadFromFile(mp.Name); break;
            case "%builtin":
            { if(mp.Name=="Builtins") module = Builtins.Instance;
              else
              { Type type = Type.GetType("NetLisp.Mods."+mp.Name);
                if(type!=null) module = ModuleGenerator.Generate(type);
              }
              break;
            }
            case ".net": module = LoadFromDotNet(mp.Name); break;
            default:
            { string name = mp.Collection+"/"+mp.Name;
              Type type = (Type)builtins[name];
              // TODO: get the library path from elsewhere
              module = type==null ? LoadFromFile(LibPath+name) : ModuleGenerator.Generate(type);
              break;
            }
          }
        }
        finally
        { if(module!=null) modules[mp] = module;
          else modules.Remove(mp);
        }
      }
      return module;
    }
  }

  static Module LoadFromDotNet(string ns)
  { Module mod = new Module(ns);
    foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())
      foreach(Type type in a.GetTypes())
        if(type.IsPublic && type.Namespace==ns)
          Interop.Import(mod.TopLevel, type);
    mod.CreateExports();
    return mod;
  }

  static Module LoadFromFile(string path)
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
  static System.Collections.Specialized.ListDictionary builtins = new System.Collections.Specialized.ListDictionary();
}

} // namespace NetLisp.Backend