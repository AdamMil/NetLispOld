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
using sys=NetLisp.Mods.sys;

namespace NetLisp.Backend
{

public sealed class Importer
{ Importer() { }
  
  public static void Import(TopLevel top, IDictionary dict, TopLevel env,
                            string[] names, string[] asNames, string myName)
  { if(names==null)
      foreach(DictionaryEntry de in dict) top.Globals.Bind((string)de.Key, de.Value, env);
    else
      for(int i=0; i<names.Length; i++)
      { object obj = dict[names[i]];
        if(obj==null && !dict.Contains(names[i]))
          throw new ArgumentException(myName+" does not contain a member called '"+names[i]+"'");
        top.Globals.Bind(asNames[i], obj, env);
      }
  }

  public static MemberContainer Load(string name) { return Load(name, true, false); }
  public static MemberContainer Load(string name, bool throwOnError) { return Load(name, throwOnError, false); }
  public static MemberContainer Load(string name, bool throwOnError, bool returnTop)
  { MemberContainer module, top;
    bool returnNow = false;

    lock(sys.modules) module = (MemberContainer)sys.modules[name];
    if(module!=null) return module;

    // TODO: optimize this so loading a dotted name from a file doesn't compile the file multiple times
    string[] bits = name.Split('.');
    top = LoadFromPath(bits[0]);
    if(top==null) top = LoadBuiltin(bits[0]);
    if(top==null)
    { top = LoadFromDotNet(bits, returnTop);
      returnNow = true;
    }

    if(top!=null) lock(sys.modules) sys.modules[bits[0]] = top;
    if(returnNow) return top;

    module = top;
    for(int i=1; i<bits.Length && module!=null; i++)
    { object obj;
      if(!module.GetMember(bits[i], out obj)) goto error;
      module = obj as MemberContainer;
    }
    if(returnTop) module = top;
    if(module!=null || !throwOnError) return module;

    error: throw new ModuleLoadException("Unable to load module: "+name);
  }

  public static MemberContainer Load(Type type)
  { MemberContainer module = (MemberContainer)builtinTypes[type];
    if(module==null) builtinTypes[type] = module = ModuleGenerator.Generate(type);
    return module;
  }

  static MemberContainer LoadBuiltin(string name)
  { if(builtinNames==null)
    { builtinNames = new Hashtable();
      foreach(Type type in Assembly.GetExecutingAssembly().GetTypes())
        if(type.IsPublic && type.Namespace=="NetLisp.Mods")
        { object[] attrs = type.GetCustomAttributes(typeof(SymbolNameAttribute), false);
          if(attrs.Length!=0) builtinNames[((SymbolNameAttribute)attrs[0]).Name] = type;
        }
    }

    { Type type = (Type)builtinNames[name];
      if(type==null) type = Type.GetType("NetLisp.Mods."+name);
      return type==null ? null : Load(type);
    }
  }

  static MemberContainer LoadFromDotNet(string[] bits, bool returnTop)
  { return ReflectedNamespace.FromName(bits, returnTop);
  }

  static MemberContainer LoadFromPath(string name) { return null; } // TODO: implement this

  static readonly Hashtable builtinTypes=new Hashtable();
  static Hashtable builtinNames;
}

} // namespace NetLisp.Backend