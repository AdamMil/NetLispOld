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
    IsDebug  = debug;
    Assembly = AppDomain.CurrentDomain
                 .DefineDynamicAssembly(an, AssemblyBuilderAccess.RunAndSave, dir, null, null, null, null, true);
    Module   = Assembly.DefineDynamicModule(outFileName, outFileName, debug);
    OutFileName = outFileName;
  }

  public TypeGenerator DefineType(string name) { return DefineType(TypeAttributes.Public, name, null); }
  public TypeGenerator DefineType(string name, Type parent)
  { return DefineType(TypeAttributes.Public, name, parent);
  }
  public TypeGenerator DefineType(TypeAttributes attrs, string name) { return DefineType(attrs, name, null); }
  public TypeGenerator DefineType(TypeAttributes attrs, string name, Type parent)
  { return new TypeGenerator(this, Module.DefineType(name, attrs, parent));
  }

  public Snippet GenerateSnippet(LambdaNode body) { return GenerateSnippet(body, "code_"+index.Next); }
  public Snippet GenerateSnippet(LambdaNode body, string typeName)
  { TypeGenerator tg = DefineType(TypeAttributes.Public|TypeAttributes.Sealed, typeName, typeof(Snippet));

    CodeGenerator cg = tg.DefineMethodOverride("Run", true);
    cg.SetupNamespace(body.MaxNames);
    body.Body.Emit(cg);
    cg.Finish();
    return (Snippet)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }

  public void Save() { Assembly.Save(OutFileName); }

  public readonly AssemblyBuilder Assembly;
  public readonly ModuleBuilder   Module;
  public ISymbolDocumentWriter Symbols;
  public readonly string OutFileName;
  public readonly bool IsDebug;
  
  static Index index = new Index();
}

} // namespace NetLisp.Backend
