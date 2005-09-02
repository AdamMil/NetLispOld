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
using System.Reflection.Emit;

namespace NetLisp.Backend
{

public abstract class BuiltinModule : LispModule
{ public BuiltinModule(Type type, bool importBuiltins) : base(type.FullName)
  { if(importBuiltins) Builtins.Instance.Import(TopLevel);
    ReflectedType.FromType(type).Import(TopLevel);
  }
}

public sealed class ModuleGenerator
{ ModuleGenerator() { }

  // TODO: this is only a temporary solution. replace it with a better one
  public static string CachePath = "c:/dllcache/";

  /*
  #region Generate from a ModuleNode
  public static Module Generate(ModuleNode mod)
  { TypeGenerator tg = SnippetMaker.Assembly.DefineType("module"+index.Next+"$"+mod.Name, typeof(Module));

    CodeGenerator cg = tg.DefineStaticMethod(MethodAttributes.Private, "Run", typeof(object),
                                             new Type[] { typeof(LocalEnvironment) });
    cg.SetupNamespace(mod.MaxNames);
    mod.Body.Emit(cg);
    cg.Finish();

    MethodBase run = cg.MethodBase;
    cg = tg.DefineConstructor(Type.EmptyTypes);
    cg.EmitThis();
    cg.EmitString(mod.Name);
    cg.EmitCall(typeof(Module).GetConstructor(new Type[] { typeof(string) }));

    cg.EmitThis();
    EmitExports(cg, mod.Exports);
    cg.EmitFieldSet(typeof(Module), "Exports");

    Slot old = cg.AllocLocalTemp(typeof(TopLevel));
    cg.EmitFieldGet(typeof(TopLevel), "Current");
    old.EmitSet(cg);
    cg.ILG.BeginExceptionBlock();
    cg.EmitPropGet(typeof(Builtins), "Instance");
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
  #endregion
  */

  #region Generate from builtin type
  public static BuiltinModule Generate(Type type) { return Generate(type, type==typeof(Builtins)); }
  public static BuiltinModule Generate(Type type, bool parseOneByOne)
  { // TODO: come up with a better naming scheme (replacing '+' with '.' can create collisions)
    string filename = CachePath+type.FullName.Replace('+', '.')+".dll";
    #if !DEBUG
    if(File.Exists(filename))
      try
      { Assembly ass = Assembly.LoadFrom(filename);
        Type mtype = ass.GetType("module");
        if(mtype!=null && mtype.IsSubclassOf(typeof(BuiltinModule)))
          return (BuiltinModule)mtype.GetConstructor(Type.EmptyTypes).Invoke(null);
      }
      catch { }
    #endif

    AssemblyGenerator ag = new AssemblyGenerator(type.FullName, filename);
    TopLevel oldTL = TopLevel.Current;
    bool debug=Options.Debug, optimize=Options.Optimize;
    try
    { TopLevel.Current = new TopLevel();
      Options.Debug=false; Options.Optimize=true;
      if(type!=typeof(Builtins)) Builtins.Instance.Import(TopLevel.Current);
      ReflectedType.FromType(type).Import(TopLevel.Current);

      TypeGenerator tg = ag.DefineType("module", typeof(BuiltinModule));
      CodeGenerator cg;

      MethodInfo run;
      object[] attrs = type.GetCustomAttributes(typeof(LispCodeAttribute), false);
      if(attrs.Length==0) run=null;
      else
      { cg = tg.DefineStaticMethod(MethodAttributes.Private, "Run", typeof(void),
                                   new Type[] { typeof(LocalEnvironment), typeof(TopLevel) });
        Slot tmp=cg.AllocLocalTemp(typeof(TopLevel));
        Slot topSlot=new ArgSlot((MethodBuilder)cg.MethodBase, 1, "topLevel", typeof(TopLevel));
        cg.EmitFieldGet(typeof(TopLevel), "Current");
        tmp.EmitSet(cg);
        cg.ILG.BeginExceptionBlock();
        topSlot.EmitGet(cg);
        cg.EmitFieldSet(typeof(TopLevel), "Current");

        Parser parser = Parser.FromString(((LispCodeAttribute)attrs[0]).Code);
        if(!parseOneByOne)
        { LambdaNode node = AST.Create(Ops.Call("expand", parser.Parse()));
          cg.SetupNamespace(node.MaxNames, topSlot);
          SnippetMaker.Generate(node).Run(null);
          node.Body.MarkTail(false);
          node.Body.EmitVoid(cg);
        }
        else
        { cg.Namespace = new LocalNamespace(new TopLevelNamespace(cg, topSlot), cg);
          int lastMax = 0;
          while(true)
          { object obj = Ops.Call("expand", parser.ParseOne());
            if(obj==Parser.EOF) break;
            LambdaNode node = AST.Create(obj);
            if(node.MaxNames!=lastMax)
            { if(node.MaxNames==0) cg.ILG.Emit(OpCodes.Ldnull);
              else
              { cg.EmitArgGet(0);
                cg.EmitInt(node.MaxNames);
                cg.EmitNew(typeof(LocalEnvironment), new Type[] { typeof(LocalEnvironment), typeof(int) });
              }
              cg.EmitArgSet(0);
              lastMax = node.MaxNames;
            }
            SnippetMaker.Generate(node).Run(null);
            node.Body.MarkTail(false);
            node.Body.EmitVoid(cg);
          }
        }
        cg.ILG.BeginFinallyBlock();
        tmp.EmitGet(cg);
        cg.EmitFieldSet(typeof(TopLevel), "Current");
        cg.ILG.EndExceptionBlock();
        cg.EmitReturn();
        cg.FreeLocalTemp(tmp);
        cg.Finish();

        run = (MethodInfo)cg.MethodBase;
      }

      cg = tg.DefineConstructor(Type.EmptyTypes);
      cg.EmitThis();
      cg.EmitTypeOf(type);
      cg.EmitBool(run!=null && type!=typeof(Builtins));
      cg.EmitCall(typeof(BuiltinModule).GetConstructor(new Type[] { typeof(Type), typeof(bool) }));
      if(run!=null)
      { cg.ILG.Emit(OpCodes.Ldnull);
        cg.EmitThis();
        cg.EmitFieldGet(typeof(LispModule), "TopLevel");
        cg.EmitCall(run);
      }
      cg.EmitReturn();
      cg.Finish();

      type = tg.FinishType();
      try { ag.Save(); } catch { }

      return (BuiltinModule)type.GetConstructor(Type.EmptyTypes).Invoke(null);
    }
    finally { TopLevel.Current=oldTL; Options.Debug=debug; Options.Optimize=optimize; }
  }
  #endregion
  
  #region Generate compiled file
  public static void Generate(string name, string filename, LambdaNode body, PEFileKinds fileKind)
  { System.Diagnostics.Debug.Assert(TopLevel.Current==null); // TODO: currently this can only be called once. improve that...

    AssemblyGenerator ag = new AssemblyGenerator(name, filename);
    SnippetMaker.Assembly = ag;

    TopLevel.Current = new TopLevel();
    Builtins.Instance.Import(TopLevel.Current);

    TypeGenerator tg = ag.DefineType(TypeAttributes.Public|TypeAttributes.Sealed, name);

    CodeGenerator cg = tg.GetInitializer();
    cg.EmitPropGet(typeof(Builtins), "Instance");
    cg.EmitNew(typeof(TopLevel));
    cg.ILG.Emit(OpCodes.Dup);
    cg.EmitFieldSet(typeof(TopLevel), "Current");
    cg.EmitCall(typeof(Module), "ImportAll");

    cg = tg.DefineStaticMethod(MethodAttributes.Private, "Run", typeof(object),
                               new Type[] { typeof(LocalEnvironment) });
    cg.SetupNamespace(body.MaxNames);
    body.Body.Emit(cg);
    cg.Finish();
    MethodInfo run = (MethodInfo)cg.MethodBase;

    cg = tg.DefineStaticMethod("Main", typeof(void), new Type[] { typeof(string[]) });
    // TODO: set argv
    cg.ILG.Emit(OpCodes.Ldnull);
    cg.EmitCall(run);
    cg.ILG.Emit(OpCodes.Pop);
    cg.EmitReturn();
    cg.Finish();

    tg.FinishType();
    ag.Assembly.SetEntryPoint((MethodInfo)cg.MethodBase, fileKind);
    if(Options.Debug) ag.Module.SetUserEntryPoint((MethodInfo)cg.MethodBase);

    ag.Save();
  }
  #endregion
  
  static Index index = new Index();
}

} // namespace NetLisp.Backend