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
using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region Attributes
public class DocStringAttribute : Attribute
{ public DocStringAttribute(string docs) { Docs=docs.Replace("\r\n", "\n"); }
  public string Docs;
}

[AttributeUsage(AttributeTargets.Parameter)]
public class RestListAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class WantListAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class|AttributeTargets.Struct)]
public class LispCodeAttribute : Attribute
{ public LispCodeAttribute(string code) { Code=code; }
  public readonly string Code;
}

public class SymbolNameAttribute : Attribute
{ public SymbolNameAttribute(string name) { Name=name; }
  public readonly string Name;
}
#endregion

// FIXME: if a lambda accesses top-level environment (by calling EVAL), it will get the TL of the caller,
//        not of where it was defined
// FIXME: implement tail-call elimination in interpreted mode
#region Procedures
public interface IProcedure
{ int MinArgs { get; }
  int MaxArgs { get; }
  bool NeedsFreshArgs { get; }

  object Call(params object[] args);
}

public abstract class Lambda : IProcedure
{ public int MinArgs { get { return Template.NumParams; } }
  public int MaxArgs { get { return Template.HasList ? -1 : Template.NumParams; } }
  public bool NeedsFreshArgs { get { return Template.ArgsClosed; } }

  public abstract object Call(params object[] args);
  public override string ToString() { return Template.Name==null ? "#<lambda>" : "#<lambda '"+Template.Name+"'>"; }

  public Template Template;
}

public abstract class Closure : Lambda
{ public LocalEnvironment Environment;
}

public sealed class InterpretedProcedure : Lambda
{ public InterpretedProcedure(string name, string[] paramNames, bool hasList, Node body)
  { Template   = new Template(IntPtr.Zero, name, paramNames.Length, hasList, false);
    ParamNames = paramNames;
    Body       = body;
  }

  public override object Call(object[] args)
  { args = Template.FixArgs(args);
    TopLevel oldt = TopLevel.Current;
    if(ParamNames.Length==0)
      try
      { TopLevel.Current = Template.TopLevel;
        return Body.Evaluate();
      }
      finally { TopLevel.Current = oldt; }
    else
    { InterpreterEnvironment ne, oldi=InterpreterEnvironment.Current;
      try
      { InterpreterEnvironment.Current = ne = new InterpreterEnvironment(oldi);
        TopLevel.Current = Template.TopLevel;
        for(int i=0; i<ParamNames.Length; i++) ne.Bind(ParamNames[i], args[i]);
        return Body.Evaluate();
      }
      finally
      { InterpreterEnvironment.Current = oldi;
        TopLevel.Current = oldt;
      }
    }
  }

  public readonly Node Body;
  public readonly string[] ParamNames;
}

public abstract class SimpleProcedure : IProcedure
{ public SimpleProcedure(string name, int min, int max) { this.name=name; this.min=min; this.max=max; }

  public int MinArgs { get { return min; } }
  public int MaxArgs { get { return max; } }
  public string Name { get { return name; } }
  public bool NeedsFreshArgs { get { return needsFreshArgs; } }

  public abstract object Call(object[] args);
  public override string ToString() { return name; }

  protected void CheckArity(object[] args) { Ops.CheckArity(name, args.Length, min, max); }

  protected string name;
  protected int min, max;
  protected bool needsFreshArgs;
}

public abstract class Primitive : SimpleProcedure
{ public Primitive(string name, int min, int max) : base(name, min, max) { }
  public override string ToString() { return string.Format("#<primitive procedure '{0}'>", name); }
}
#endregion

#region Enums
[Flags]
public enum Conversion
{ None=0,
  // 1/3 and 8/12 are chosen to make it clear that those are mutually exclusive
  Unsafe=1, Safe=2, Reference=3, Identity=4, UnsafeAPA=8, SafeAPA=16, RefAPA=24, PacksPA=32,
  QualityMask=31
}
#endregion

#region Binding
public sealed class Binding
{ public Binding(string name, TopLevel env) { Value=Unbound; Name=name; Environment=env; }

  public override bool Equals(object obj) { return this==obj; }
  public override int GetHashCode() { return Name.GetHashCode(); }

  public object Value;
  public string Name;
  public TopLevel Environment;

  public readonly static object Unbound = new Singleton("<UNBOUND>");
}
#endregion

public sealed class Disambiguator
{ public Disambiguator(Type type, object value) { Type=type; Value=value; }

  public Type Type;
  public object Value;
  
  public readonly static Type ClassType = typeof(Disambiguator);
}

#region RG (stuff that can't be written in C#)
public sealed class RG
{ static RG()
  { if(System.IO.File.Exists(ModuleGenerator.CachePath+"NetLisp.Backend.LowLevel.dll"))
      try
      { Assembly ass = Assembly.LoadFrom("NetLisp.Backend.LowLevel.dll");
        ClosureType = ass.GetType("NetLisp.Backend.ClosureF", true);
        return;
      }
      catch { }

    AssemblyGenerator ag = new AssemblyGenerator("NetLisp.Backend.LowLevel",
                                                 ModuleGenerator.CachePath+"NetLisp.Backend.LowLevel.dll", false);
    TypeGenerator tg;
    CodeGenerator cg;

    #region Closure
    { tg = ag.DefineType(TypeAttributes.Public|TypeAttributes.Sealed, "NetLisp.Backend.ClosureF", typeof(Closure));
      cg = tg.DefineConstructor(new Type[] { typeof(Template), typeof(LocalEnvironment) });
      cg.EmitThis();
      cg.EmitArgGet(0);
      cg.EmitFieldSet(typeof(Lambda), "Template");
      cg.EmitThis();
      cg.EmitArgGet(1);
      cg.EmitFieldSet(typeof(Closure), "Environment");
      cg.EmitReturn();
      cg.Finish();

      cg = tg.DefineMethodOverride(typeof(Lambda), "Call", true);
      cg.EmitThis();
      cg.EmitFieldGet(typeof(Closure), "Environment");

      cg.EmitThis();
      cg.EmitFieldGet(typeof(Lambda), "Template");
      cg.EmitArgGet(0);
      cg.EmitCall(typeof(Template), "FixArgs");

      cg.EmitThis();
      cg.EmitFieldGet(typeof(Lambda), "Template");
      cg.EmitFieldGet(typeof(Template), "FuncPtr");
      cg.ILG.Emit(OpCodes.Tailcall);
      cg.ILG.EmitCalli(OpCodes.Calli, CallingConventions.Standard, typeof(object),
                       new Type[] { typeof(LocalEnvironment), typeof(object[]) }, null);
      cg.EmitReturn();
      cg.Finish();

      ClosureType = tg.FinishType();
    }
    #endregion

    try { ag.Save(); } catch { }
  }

  public static readonly Type ClosureType;
}
#endregion

#region Environments
public sealed class InterpreterEnvironment
{ public InterpreterEnvironment(InterpreterEnvironment parent) { this.parent=parent; dict=new ListDictionary(); }

  public void Bind(string name, object value) { dict[name]=value; }

  public object Get(string name)
  { object ret = dict[name];
    if(ret==null && !dict.Contains(name)) return parent!=null ? parent.Get(name) : TopLevel.Current.Get(name);
    return ret;
  }

  public void Set(string name, object value)
  { if(dict.Contains(name)) dict[name] = value;
    else if(parent!=null) parent.Set(name, value);
    else TopLevel.Current.Set(name, value);
  }

  InterpreterEnvironment parent;
  ListDictionary dict;

  [ThreadStatic] public static InterpreterEnvironment Current;
}

public sealed class LocalEnvironment
{ public LocalEnvironment(LocalEnvironment parent, object[] values) { Parent=parent; Values=values; }
  public LocalEnvironment(LocalEnvironment parent, int length) { Parent=parent; Values=new object[length]; }
  public LocalEnvironment(LocalEnvironment parent, object[] values, int length)
  { Parent=parent; Values=new object[length];
    Array.Copy(values, Values, values.Length);
  }

  public readonly LocalEnvironment Parent;
  public readonly object[] Values;
}

public sealed class TopLevel
{ public enum NS { Main, Macro }

  #region BindingSpace
  public sealed class BindingSpace
  { public void Bind(string name, object value, TopLevel env)
    { Binding bind;
      lock(Dict)
      { bind = (Binding)Dict[name];
        if(bind==null) Dict[name] = bind = new Binding(name, env);
        else bind.Environment = env;
      }
      bind.Value = value;
    }

    public bool Contains(string name)
    { Binding obj;
      lock(Dict) obj = (Binding)Dict[name];
      return obj!=null && obj.Value!=Binding.Unbound;
    }

    public object Get(string name)
    { Binding obj;
      lock(Dict) obj = (Binding)Dict[name];
      if(obj==null || obj.Value==Binding.Unbound) throw new NameException("no such name: "+name);
      return obj.Value;
    }

    public bool Get(string name, out object value)
    { Binding obj;
      lock(Dict) obj = (Binding)Dict[name];
      if(obj==null || obj.Value==Binding.Unbound) { value=null; return false; }
      value = obj.Value;
      return true;
    }

    public Binding GetBinding(string name, TopLevel env)
    { Binding obj;
      lock(Dict) obj = (Binding)Dict[name];
      if(obj==null) Dict[name] = obj = new Binding(name, env);
      return obj;
    }

    public void Set(string name, object value)
    { Binding obj;
      lock(Dict) obj = (Binding)Dict[name];
      if(obj==null) throw new NameException("no such name: "+name);
      obj.Value = value;
    }
    
    public readonly Hashtable Dict = new Hashtable();
  }
  #endregion

  public void AddMacro(string name, IProcedure value) { Macros.Bind(name, value, this); }

  public void Bind(string name, object value) { Globals.Bind(name, value, this); }

  public bool Contains(string name) { return Globals.Contains(name); }
  public bool ContainsMacro(string name) { return Macros.Contains(name); }

  public object Get(string name) { return Globals.Get(name); }
  public bool Get(string name, out object value) { return Globals.Get(name, out value); }
  public Binding GetBinding(string name) { return Globals.GetBinding(name, this); }
  public IProcedure GetMacro(string name) { return (IProcedure)Macros.Get(name); }

  public void Set(string name, object value) { Globals.Set(name, value); }

  public BindingSpace Globals=new BindingSpace(), Macros=new BindingSpace();

  [ThreadStatic] public static TopLevel Current;
}
#endregion

#region LispComparer
public sealed class LispComparer : IComparer
{ public LispComparer(IProcedure proc)
  { if(proc!=null) { this.proc=proc; args=new object[2]; }
  }

  public int Compare(object a, object b)
  { if(proc==null) return Ops.Compare(a, b);
    else
    { args[0] = a;
      args[1] = b;
      return Ops.ToInt(proc.Call(args));
    }
  }

  public static readonly LispComparer Default = new LispComparer(null);

  IProcedure proc;
  object[] args;
}
#endregion

#region LispModule
public class LispModule : MemberContainer
{ public LispModule(string name) { Name=name; TopLevel=new TopLevel(); }
  public LispModule(string name, TopLevel top) { Name=name; TopLevel=top; }

  public override object GetMember(string name) { return TopLevel.Globals.Get(name); }
  public override bool GetMember(string name, out object ret) { return TopLevel.Globals.Get(name, out ret); }
  public override ICollection GetMemberNames() { return TopLevel.Globals.Dict.Keys; }

  public override void Import(TopLevel top, string[] names, string[] asNames)
  { if(names==null)
    { Import(top.Globals, TopLevel.Globals, TopLevel);
      Import(top.Macros, TopLevel.Macros, TopLevel);
    }
    else
      for(int i=0; i<names.Length; i++)
      { object ret;
        bool found = false;
        if(TopLevel.Globals.Get(names[i], out ret))
        { top.Globals.Bind(asNames[i], ret, TopLevel);
          found = true;
        }
        if(TopLevel.Macros.Get(names[i], out ret))
        { top.Macros.Bind(asNames[i], ret, TopLevel);
          found = true;
        }
        if(!found) throw new ArgumentException("'"+names[i]+"' not found in module "+Name);
      }
  }

  public override string ToString() { return "#<module '"+Name+"'>"; }

  public readonly TopLevel TopLevel;
  public readonly string Name;

  static void Import(TopLevel.BindingSpace to, TopLevel.BindingSpace from, TopLevel env)
  { foreach(DictionaryEntry de in from.Dict)
    { string key = (string)de.Key;
      if(!key.StartsWith("#_"))
      { Binding bind = (Binding)de.Value;
        to.Bind(key, bind.Value, env);
      }
    }
  }
}
#endregion

#region MemberContainer
public abstract class MemberContainer
{ public abstract object GetMember(string name);
  public abstract bool GetMember(string name, out object ret);
  public abstract ICollection GetMemberNames();

  public static MemberContainer FromObject(object obj)
  { MemberContainer mc = obj as MemberContainer;
    return mc!=null ? mc : obj==null ? ReflectedType.NullType : ReflectedType.FromType(obj.GetType());
  }

  public void Import(TopLevel top) { Import(top, null, null); }
  public void Import(TopLevel top, string[] names) { Import(top, names, names); }
  public abstract void Import(TopLevel top, string[] names, string[] asNames);

  public void Import(TopLevel top, Pair bindings)
  { if(bindings==null)
    { object obj;
      if(GetMember("*BINDINGS*", out obj)) bindings = obj as Pair;
    }
    if(bindings==null) Import(top, null, null);
    else
    { ArrayList names=new ArrayList(), asNames=new ArrayList();
      do
      { string name = bindings.Car as string, asName;
        if(name!=null) asName=name;
        else
        { Pair pair = bindings.Car as Pair;
          if(pair!=null)
          { name = pair.Car as string;
            asName = pair.Cdr as string;
          }
          else asName=null;
          if(name==null || asName==null)
            throw new ArgumentException("export list should be composed of strings and (name . asName) pairs");
        }
        names.Add(name);
        asNames.Add(asName);
        
        bindings = bindings.Cdr as Pair;
      } while(bindings!=null);

      Import(top, (string[])names.ToArray(typeof(string)), (string[])asNames.ToArray(typeof(string)));
    }
  }
}
#endregion

#region MultipleValues
public sealed class MultipleValues
{ public MultipleValues(params object[] values) { Values=values; }

  public override string ToString()
  { System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.Append('{');
    bool sep=false;
    for(int i=0; i<Values.Length; i++)
    { if(sep) sb.Append(", ");
      else sep=true;
      sb.Append(Ops.Repr(Values[i]));
    }
    return sb.Append('}').ToString();
  }

  public object[] Values;
}
#endregion

#region Ops
public sealed class Ops
{ Ops() { }

  public static Pair Append(Pair list1, Pair list2)
  { if(list1==null) return list2;
    else
    { if(list2!=null) Mods.Srfi1.lastPair.core(list1).Cdr=list2;
      return list1;
    }
  }

  public static object AreEqual(object a, object b) { return FromBool(EqvP(a, b)); }

  public static object Add(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte: return IntOps.Add((int)(byte)a, b);
      case TypeCode.Char:
        if(b is string) return (char)a+(string)b;
        break;
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a + (Decimal)b;
        try { return (Decimal)a + Convert.ToDecimal(b); }
        catch(InvalidCastException) { break; }
      case TypeCode.Double: return FloatOps.Add((double)a, b);
      case TypeCode.Int16: return IntOps.Add((int)(short)a, b);
      case TypeCode.Int32: return IntOps.Add((int)a, b);
      case TypeCode.Int64: return LongOps.Add((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Add((Integer)a, b);
        if(a is Complex) return ComplexOps.Add((Complex)a, b);
        break;
      case TypeCode.SByte: return IntOps.Add((int)(sbyte)a, b);
      case TypeCode.Single: return FloatOps.Add((float)a, b);
      case TypeCode.String:
        if(b is string) return (string)a + (string)b;
        if(b is char) return (string)a + (char)b;
        break;
      case TypeCode.UInt16: return IntOps.Add((int)(short)a, b);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Add((int)v, b) : LongOps.Add((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Add((long)v, b) : IntegerOps.Add(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for +: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static object BitwiseAnd(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:  return IntOps.BitwiseAnd((byte)a, b);
      case TypeCode.Int16: return IntOps.BitwiseAnd((short)a, b);
      case TypeCode.Int32: return IntOps.BitwiseAnd((int)a, b);
      case TypeCode.Int64: return LongOps.BitwiseAnd((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.BitwiseAnd((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.BitwiseAnd((sbyte)a, b);
      case TypeCode.UInt16: return IntOps.BitwiseAnd((short)a, b);
      case TypeCode.UInt32:
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.BitwiseAnd((int)v, b) : LongOps.BitwiseAnd((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.BitwiseAnd((long)v, b) : IntegerOps.BitwiseAnd(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for &: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static object BitwiseOr(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:  return IntOps.BitwiseOr((byte)a, b);
      case TypeCode.Int16: return IntOps.BitwiseOr((short)a, b);
      case TypeCode.Int32: return IntOps.BitwiseOr((int)a, b);
      case TypeCode.Int64: return LongOps.BitwiseOr((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.BitwiseOr((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.BitwiseOr((sbyte)a, b);
      case TypeCode.UInt16: return IntOps.BitwiseOr((short)a, b);
      case TypeCode.UInt32:
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.BitwiseOr((int)v, b) : LongOps.BitwiseOr((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.BitwiseOr((long)v, b) : IntegerOps.BitwiseOr(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for |: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static object BitwiseXor(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:  return IntOps.BitwiseXor((byte)a, b);
      case TypeCode.Int16: return IntOps.BitwiseXor((short)a, b);
      case TypeCode.Int32: return IntOps.BitwiseXor((int)a, b);
      case TypeCode.Int64: return LongOps.BitwiseXor((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.BitwiseXor((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.BitwiseXor((sbyte)a, b);
      case TypeCode.UInt16: return IntOps.BitwiseXor((short)a, b);
      case TypeCode.UInt32:
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.BitwiseXor((int)v, b) : LongOps.BitwiseXor((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.BitwiseXor((long)v, b) : IntegerOps.BitwiseXor(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for ^: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static object BitwiseNegate(object a)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:  return ~(int)(byte)a;
      case TypeCode.Int16: return ~(int)(short)a;
      case TypeCode.Int32: return ~(int)a;
      case TypeCode.Int64: return ~(long)a;
      case TypeCode.Object:
        if(a is Integer) return ~(Integer)a;
        break;
      case TypeCode.SByte: return ~(int)(sbyte)a;
      case TypeCode.UInt16: return ~(int)(short)a;
      case TypeCode.UInt32:
      { uint v = (uint)a;
        return v<=int.MaxValue ? (object)~(int)v : (object)~(long)v;
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? (object)(~(long)v) : (object)(~new Integer(v));
      }
    }
    throw TypeError("unsupported operand type for ~: '{0}'", TypeName(a));
  }

  public static object Call(string name) { return Call(GetGlobal(name), EmptyArray); }
  public static object Call(string name, params object[] args) { return Call(GetGlobal(name), args); }
  public static object Call(object func, params object[] args) { return ExpectProcedure(func).Call(args); }

  public static void CheckArity(string name, int nargs, int min, int max)
  { if(max==-1)
    { if(nargs<min) throw new ArgumentException(name+": expects at least "+min.ToString()+
                                                " arguments, but received "+nargs.ToString());
    }
    else if(nargs<min || nargs>max)
      throw new ArgumentException(name+": expects "+(min==max ? min.ToString() : min.ToString()+"-"+max.ToString())+
                                  " arguments, but received "+nargs.ToString());
  }

  public static Binding CheckBinding(Binding bind)
  { if(bind.Value==Binding.Unbound) throw new NameException("use of unbound variable: "+bind.Name);
    return bind;
  }

  public static object CheckVariable(object value, string name)
  { if(value==Binding.Unbound) throw new NameException("use of unbound variable: "+name);
    return value;
  }

  public static object[] CheckValues(MultipleValues values, int length)
  { if(values.Values.Length<length) throw Ops.ValueError("expected at least "+length.ToString()+
                                                         " values, but received "+values.Values.Length.ToString());
    return values.Values;
  }

  public static Snippet CompileRaw(object obj) { return SnippetMaker.Generate(AST.Create(obj)); }

  public static int Compare(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean:
        if(b is bool) return (bool)a ? (bool)b ? 0 : 1 : (bool)b ? -1 : 0;
        break;
      case TypeCode.Byte: return IntOps.Compare((int)(byte)a, b);
      case TypeCode.Char:
        if(b is char) return (int)(char)a - (int)(char)b;
        break;
      case TypeCode.Decimal:
        if(b is Decimal) return ((Decimal)a).CompareTo(b);
        try { return ((Decimal)a).CompareTo(Convert.ToDecimal(b)); }
        catch(InvalidCastException) { break; }
      case TypeCode.Double: return FloatOps.Compare((double)a, b);
      case TypeCode.Empty: return b==null ? 0 : -1;
      case TypeCode.Int16: return IntOps.Compare((short)a, b);
      case TypeCode.Int32: return IntOps.Compare((int)a, b);
      case TypeCode.Int64: return LongOps.Compare((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Compare((Integer)a, b);
        if(a is Complex)
        { Complex c = (Complex)a;
          if(c.imag==0) return FloatOps.Compare(c.real, b);
        }
        break;
      case TypeCode.SByte: return IntOps.Compare((sbyte)a, b);
      case TypeCode.Single: return FloatOps.Compare((float)a, b);
      case TypeCode.String:
      { string sb = b as string;
        if(sb!=null) return string.Compare((string)a, sb);
        break;
      }
      case TypeCode.UInt16: return IntOps.Compare((short)a, b);
      case TypeCode.UInt32:
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Compare((int)v, b) : LongOps.Compare((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Compare((long)v, b) : IntegerOps.Compare(new Integer(v), b);
      }
    }
    throw Ops.TypeError("can't compare types: {0} and {1}", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object ConvertTo(object o, Type type)
  { switch(ConvertTo(o==null ? null : o.GetType(), type))
    { case Conversion.Identity: case Conversion.Reference: return o;
      case Conversion.None: throw TypeError("object cannot be converted to '{0}'", type);
      default:
        if(type==typeof(bool)) return FromBool(IsTrue(o));
        if(type.IsSubclassOf(typeof(Delegate))) return MakeDelegate(o, type);
        try { return Convert.ChangeType(o, type); }
        catch(OverflowException) { throw ValueError("large value caused overflow"); }
    }
  }

  public static Conversion ConvertTo(Type from, Type to)
  { if(from==null)
      return !to.IsValueType ? Conversion.Reference : to==typeof(bool) ? Conversion.Safe : Conversion.None;
    else if(to==from) return Conversion.Identity;
    else if(to.IsAssignableFrom(from)) return Conversion.Reference;

    // TODO: check whether it's faster to use IndexOf() or our own loop
    // TODO: add support for Integer, Complex, and Decimal
    if(to.IsPrimitive)
    { if(from.IsPrimitive)
      { if(to==typeof(bool)) return IsIn(typeConv[9], from) ? Conversion.None : Conversion.Safe;
        else
          switch(Type.GetTypeCode(from))
          { case TypeCode.Int32:  return IsIn(typeConv[4], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.Double: return Conversion.None;
            case TypeCode.Int64:  return IsIn(typeConv[6], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.Char:   return IsIn(typeConv[8], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.Byte:   return IsIn(typeConv[1], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.UInt32: return IsIn(typeConv[5], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.Single: return to==typeof(double) ? Conversion.Safe : Conversion.None;
            case TypeCode.Int16:  return IsIn(typeConv[2], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.UInt16: return IsIn(typeConv[3], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.SByte:  return IsIn(typeConv[0], to) ? Conversion.Safe : Conversion.Unsafe;
            case TypeCode.UInt64: return IsIn(typeConv[7], to) ? Conversion.Safe : Conversion.Unsafe;
          }
       }
       else if(from==typeof(object)) return Conversion.Unsafe;
    }
    if(from.IsArray && to.IsArray && to.GetElementType().IsAssignableFrom(from.GetElementType()))
      return Conversion.Reference;
    if(to.IsSubclassOf(typeof(Delegate)))
      return typeof(IProcedure).IsAssignableFrom(from) ? Conversion.Unsafe : Conversion.None;
    return Conversion.None;
  }

  public static object Divide(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte: return IntOps.Divide((byte)a, b, false);
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a / (Decimal)b;
        try { return (Decimal)a / Convert.ToDecimal(b); }
        catch(InvalidCastException) { break; }
      case TypeCode.Double:  return FloatOps.Divide((double)a, b, false);
      case TypeCode.Int16: return IntOps.Divide((short)a, b, false);
      case TypeCode.Int32: return IntOps.Divide((int)a, b, false);
      case TypeCode.Int64: return LongOps.Divide((long)a, b, false);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Divide((Integer)a, b);
        if(a is Complex) return ComplexOps.Divide((Complex)a, b);
        break;
      case TypeCode.SByte: return IntOps.Divide((sbyte)a, b, false);
      case TypeCode.Single: return FloatOps.Divide((float)a, b, false);
      case TypeCode.UInt16: return IntOps.Divide((short)a, b, false);
      case TypeCode.UInt32: return LongOps.Divide((uint)a, b, false);
      case TypeCode.UInt64: return IntegerOps.Divide(new Integer((ulong)a), b);
    }
    throw TypeError("unsupported operand types for /: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static Pair DottedList(object last, params object[] items)
  { Pair head=new Pair(items[0], null), tail=head;
    for(int i=1; i<items.Length; i++)
    { Pair next=new Pair(items[i], null);
      tail.Cdr = next;
      tail     = next;
    }
    tail.Cdr = last;
    return head;
  }

  public static bool EqualP(object a, object b)
  { if(a==b) return true;

    Pair pa=a as Pair;
    if(pa!=null)
    { Pair pb=b as Pair;
      if(pb!=null)
      { do
        { if(!EqualP(pa.Car, pb.Car)) return false;
          Pair next=pa.Cdr as Pair;
          if(next==null && pa.Cdr!=null) return EqualP(pa.Cdr, pb.Cdr);
          pa = next;
          pb = pb.Cdr as Pair;
        } while(pa!=null && pb!=null);
        return pa==pb;
      }
    }
    else if(!(b is Pair)) return EqvP(a, b);
    return false;
  }

  public static bool EqvP(object a, object b)
  { if(a==b) return true;
    switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return b is bool && (bool)a==(bool)b;
      case TypeCode.Byte: return IntOps.AreEqual((byte)a, b);
      case TypeCode.Char: return IntOps.AreEqual((int)(char)a, b);
      case TypeCode.Decimal: return b is Decimal ? (Decimal)a==(Decimal)b
                                                 : FloatOps.AreEqual(Decimal.ToDouble((Decimal)a), b);
      case TypeCode.Double: return FloatOps.AreEqual((double)a, b);
      case TypeCode.Int16: return IntOps.AreEqual((short)a, b);
      case TypeCode.Int32: return IntOps.AreEqual((int)a, b);
      case TypeCode.Int64: return LongOps.AreEqual((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.AreEqual((Integer)a, b);
        if(a is Complex) return ComplexOps.AreEqual((Complex)a, b);
        return a.Equals(b);
      case TypeCode.SByte: return IntOps.AreEqual((sbyte)a, b);
      case TypeCode.Single: return FloatOps.AreEqual((float)a, b);
      case TypeCode.String: return b is string ? (string)a==(string)b : false;
      case TypeCode.UInt16: return IntOps.AreEqual((ushort)a, b);
      case TypeCode.UInt32:
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.AreEqual((int)v, b) : LongOps.AreEqual((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.AreEqual((long)v, b) : IntegerOps.AreEqual(new Integer(v), b);
      }
    }
    return false;
  }

  public static char ExpectChar(object obj)
  { try { return (char)obj; }
    catch(InvalidCastException) { throw new ArgumentException("expected character but received "+TypeName(obj)); }
  }

  // TODO: coerce numbers into complexes?
  public static Complex ExpectComplex(object obj)
  { try { return (Complex)obj; }
    catch(InvalidCastException) { throw new ArgumentException("expected complex but received "+TypeName(obj)); }
  }

  public static IEnumerator ExpectEnumerator(object obj)
  { IEnumerable ea = obj as IEnumerable;
    IEnumerator  e = ea==null ? obj as IEnumerator : ea.GetEnumerator();
    if(e==null) throw Ops.TypeError("expected enumerable object but received "+Ops.TypeName(obj));
    return e;
  }

  public static int ExpectInt(object obj)
  { try { return (int)obj; }
    catch(InvalidCastException) { throw new ArgumentException("expected int but received "+TypeName(obj)); }
  }

  public static Pair ExpectList(object obj)
  { if(obj==null) return null;
    Pair ret = obj as Pair;
    if(ret==null) throw new ArgumentException("expected list but received "+TypeName(obj));
    return ret;
  }

  public static Pair ExpectPair(object obj)
  { Pair ret = obj as Pair;
    if(ret==null) throw new ArgumentException("expected pair but received "+TypeName(obj));
    return ret;
  }

  public static IProcedure ExpectProcedure(object obj)
  { IProcedure ret = obj as IProcedure;
    if(ret==null) throw new ArgumentException("expected function but received "+TypeName(obj));
    return ret;
  }

  public static Promise ExpectPromise(object obj)
  { Promise ret = obj as Promise;
    if(ret==null) throw new ArgumentException("expected promise but received "+TypeName(obj));
    return ret;
  }

  public static Reference ExpectRef(object obj)
  { Reference ret = obj as Reference;
    if(ret==null) throw new ArgumentException("expected ref but received "+TypeName(obj));
    return ret;
  }

  public static string ExpectString(object obj)
  { string ret = obj as string;
    if(ret==null) throw new ArgumentException("expected string but received "+TypeName(obj));
    return ret;
  }

  public static Symbol ExpectSymbol(object obj)
  { Symbol ret = obj as Symbol;
    if(ret==null) throw new ArgumentException("expected symbol but received "+TypeName(obj));
    return ret;
  }

  public static Type ExpectType(object obj)
  { Type ret = obj as Type;
    if(ret==null) throw new ArgumentException("expected type but received "+TypeName(obj));
    return ret;
  }

  public static MultipleValues ExpectValues(object obj)
  { MultipleValues ret = obj as MultipleValues;
    if(ret==null) throw new ArgumentException("expected multiplevalues but received "+TypeName(obj));
    return ret;
  }

  public static object[] ExpectVector(object obj)
  { object[] ret = obj as object[];
    if(ret==null) throw new ArgumentException("expected vector but received "+TypeName(obj));
    return ret;
  }

  public static object FastCadr(Pair pair) { return ((Pair)pair.Cdr).Car; }
  public static object FastCddr(Pair pair) { return ((Pair)pair.Cdr).Cdr; }

  public static object FloorDivide(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:   return IntOps.Divide((byte)a, b, true);
      case TypeCode.Double: return FloatOps.Divide((double)a, b, true);
      case TypeCode.Int16:  return IntOps.Divide((short)a, b, true);
      case TypeCode.Int32:  return IntOps.Divide((int)a, b, true);
      case TypeCode.Int64:  return LongOps.Divide((long)a, b, true);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.FloorDivide((Integer)a, b);
        if(a is Complex)
        { Complex c = (Complex)b;
          if(c.imag==0) return FloatOps.Divide(c.real, b, true);
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic!=null) return FloatOps.Divide(ic.ToDouble(NumberFormatInfo.InvariantInfo), b, true);
        }
        break;
      case TypeCode.SByte: return IntOps.Divide((sbyte)a, b, true);
      case TypeCode.Single: return FloatOps.Divide((float)a, b, true);
      case TypeCode.UInt16: return IntOps.Divide((short)a, b, true);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Divide((int)v, b, true) : LongOps.Divide((long)v, b, true);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Divide((long)v, b, true) : IntegerOps.Divide(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for //: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  // TODO: check whether we can eliminate this (ie, "(eq? #t #t)" still works reliably)
  public static object FromBool(bool value) { return value ? TRUE : FALSE; }

  public static object GetGlobal(string name) { return TopLevel.Current.Get(name); }
  public static bool GetGlobal(string name, out object value) { return TopLevel.Current.Get(name, out value); }

  public static object GetMember(object obj, string dottedName)
  { foreach(string bit in dottedName.Split('.'))
    { LastPtr = obj;
      obj = MemberContainer.FromObject(obj).GetMember(bit);
    }
    return obj;
  }

  public static bool IsTrue(object obj) { return obj!=null && (!(obj is bool) || (bool)obj); }

  public static object LeftShift(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:  return IntOps.LeftShift((int)(byte)a, b);
      case TypeCode.Int16: return IntOps.LeftShift((int)(short)a, b);
      case TypeCode.Int32: return IntOps.LeftShift((int)a, b);
      case TypeCode.Int64: return LongOps.LeftShift((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.LeftShift((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.LeftShift((int)(sbyte)a, b);
      case TypeCode.UInt16: return IntOps.LeftShift((int)(short)a, b);
      case TypeCode.UInt32: return LongOps.LeftShift((long)(uint)a, b);
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.LeftShift((long)v, b) : IntegerOps.LeftShift(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for <<: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static object Less(object a, object b) { return FromBool(Compare(a,b)<0); }
  public static object LessEqual(object a, object b) { return FromBool(Compare(a,b)<=0); }

  public static Pair List(params object[] items) { return List(items, 0, items.Length); }
  public static Pair List(object[] items, int start) { return List(items, start, items.Length-start); }
  public static Pair List(object[] items, int start, int length)
  { Pair pair=null;
    for(int i=start+length-1; i>=start; i--) pair = new Pair(items[i], pair);
    return pair;
  }

  public static Pair List2(object first, params object[] items) { return new Pair(first, List(items)); }

  public static object[] ListToArray(Pair pair)
  { if(pair==null) return EmptyArray;
    ArrayList items = new ArrayList();
    while(pair!=null) { items.Add(pair.Car); pair = pair.Cdr as Pair; }
    return (object[])items.ToArray(typeof(object));
  }

  public static Delegate MakeDelegate(object callable, Type delegateType)
  { IProcedure proc = callable as IProcedure;
    if(proc==null) throw new ArgumentException("delegate: expected a procedure");
    return Delegate.CreateDelegate(delegateType, Interop.MakeDelegateWrapper(proc, delegateType), "Handle");
  }

  public static Exception MakeException(Type type, object[] args)
  { ConstructorInfo ci = type.GetConstructor(new Type[] { typeof(string) });
    if(ci!=null)
    { System.Text.StringBuilder sb = new System.Text.StringBuilder();
      if(args!=null) foreach(object o in args) sb.Append(Str(o));
      return (Exception)ci.Invoke(new object[1] { sb.ToString() });
    }
    
    ci = type.GetConstructor(Type.EmptyTypes);
    if(ci!=null) return (Exception)ci.Invoke(EmptyArray);
    
    throw new NotSupportedException("unable to construct exception type: "+type.FullName);
  }

  public static object Modulus(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:    return IntOps.Modulus((int)(byte)a, b);
      case TypeCode.Decimal: return FloatOps.Modulus(Decimal.ToDouble((Decimal)a), b);
      case TypeCode.Double:  return FloatOps.Modulus((double)a, b);
      case TypeCode.Int16:   return IntOps.Modulus((int)(short)a, b);
      case TypeCode.Int32:   return IntOps.Modulus((int)a, b);
      case TypeCode.Int64:   return LongOps.Modulus((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Modulus((Integer)a, b);
        if(a is Complex)
        { Complex c = (Complex)a;
          if(c.imag==0) return FloatOps.Modulus(c.real, b);
        }
        break;
      case TypeCode.SByte: return IntOps.Modulus((int)(sbyte)a, b);
      case TypeCode.Single: return FloatOps.Modulus((float)a, b);
      case TypeCode.UInt16: return IntOps.Modulus((int)(short)a, b);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Modulus((int)v, b) : LongOps.Modulus((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Modulus((long)v, b) : IntegerOps.Modulus(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for %: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static object More(object a, object b) { return FromBool(Compare(a,b)>0); }
  public static object MoreEqual(object a, object b) { return FromBool(Compare(a,b)>=0); }

  public static object Multiply(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:    return IntOps.Multiply((int)(byte)a, b);
      case TypeCode.Char:    return new string((char)a, ToInt(b));
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a * (Decimal)b;
        try { return (Decimal)a * Convert.ToDecimal(b); }
        catch(InvalidCastException) { break; }
      case TypeCode.Double: return FloatOps.Multiply((double)a, b);
      case TypeCode.Int16: return IntOps.Multiply((int)(short)a, b);
      case TypeCode.Int32: return IntOps.Multiply((int)a, b);
      case TypeCode.Int64: return LongOps.Multiply((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Multiply((Integer)a, b);
        if(a is Complex) return ComplexOps.Multiply((Complex)a, b);
        break;
      case TypeCode.SByte: return IntOps.Multiply((int)(sbyte)a, b);
      case TypeCode.Single: return FloatOps.Multiply((float)a, b);
      case TypeCode.String: return StringOps.Multiply((string)a, b);
      case TypeCode.UInt16: return IntOps.Multiply((int)(short)a, b);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Multiply((int)v, b) : LongOps.Multiply((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Multiply((long)v, b) : IntegerOps.Multiply(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for *: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static object Negate(object a)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte: return -(int)(byte)a;
      case TypeCode.Decimal: return -(Decimal)a;
      case TypeCode.Double: return -(double)a;
      case TypeCode.Int16: return -(int)(short)a;
      case TypeCode.Int32: return -(int)a;
      case TypeCode.Int64: return -(long)a;
      case TypeCode.Object:
        if(a is Integer) return -(Integer)a;
        if(a is Complex) return -(Complex)a;
        break;
      case TypeCode.SByte: return -(int)(sbyte)a;
      case TypeCode.Single: return -(float)a;
      case TypeCode.UInt16: return -(int)(short)a;
      case TypeCode.UInt32:
      { uint v = (uint)a;
        return v<=int.MaxValue ? (object)-(int)v : (object)-(long)v;
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? (object)-(long)v : (object)-new Integer(v);
      }
    }
    throw TypeError("unsupported operand type for unary -: '{0}'", TypeName(a));
  }

  public static object NotEqual(object a, object b) { return FromBool(!EqvP(a, b)); }

  public static object Power(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:    return IntOps.Power((int)(byte)a, b);
      case TypeCode.Double:  return FloatOps.Power((double)a, b);
      case TypeCode.Int16: return IntOps.Power((int)(short)a, b);
      case TypeCode.Int32: return IntOps.Power((int)a, b);
      case TypeCode.Int64: return LongOps.Power((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Power((Integer)a, b);
        if(a is Complex) return ComplexOps.Power((Complex)a, b);
        break;
      case TypeCode.SByte: return IntOps.Power((int)(sbyte)a, b);
      case TypeCode.Single: return FloatOps.Power((float)a, b);
      case TypeCode.UInt16: return IntOps.Power((int)(short)a, b);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Power((int)v, b) : LongOps.Power((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Power((long)v, b) : IntegerOps.Power(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for **: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  // TODO: optimize this
  public static object PowerMod(object a, object b, object c)
  { if(a is Integer) return IntegerOps.PowerMod((Integer)a, b, c);
    if(a is Complex) return ComplexOps.PowerMod((Complex)a, b, c);
    return Modulus(Power(a, b), c);
  }
  
  public static string Repr(object obj)
  { switch(Convert.GetTypeCode(obj))
    { case TypeCode.Boolean: return (bool)obj ? "#t" : "#f";
      case TypeCode.Char: return Builtins.charToName.core((char)obj, true);
      case TypeCode.Double: return ((double)obj).ToString("R");
      case TypeCode.Empty: return "nil";
      case TypeCode.Object:
        if(obj is object[])
        { System.Text.StringBuilder sb = new System.Text.StringBuilder();
          sb.Append("#(");
          bool sep=false;
          foreach(object o in (object[])obj)
          { if(sep) sb.Append(' ');
            else sep=true;
            sb.Append(Repr(o));
          }
          sb.Append(')');
          return sb.ToString();
        }
        break;
      case TypeCode.Single: return ((float)obj).ToString("R");
      case TypeCode.String:
      { string str = (string)obj;
        System.Text.StringBuilder sb = new System.Text.StringBuilder(str.Length+16);
        sb.Append('"');
        for(int i=0; i<str.Length; i++)
        { char c = str[i];
          if(c>=32 && c!='"' && c!='\\' && c!=127) sb.Append(c);
          else
            switch(c)
            { case '\n': sb.Append("\\n"); break;
              case '\r': sb.Append("\\r"); break;
              case '\"': sb.Append("\\\""); break;
              case '\\': sb.Append("\\\\"); break;
              case '\t': sb.Append("\\t"); break;
              case '\b': sb.Append("\\b"); break;
              case (char)27: sb.Append("\\e"); break;
              default:
                sb.Append(c==0 ? "\\0" : c<27 ? "\\c"+((char)(c+64)).ToString()
                                              : (c<256 ? "\\x" : "\\u")+ToHex((uint)c, c<256 ? 2 : 4));
                break;
            }
        }
        sb.Append('"');
        return sb.ToString();
      }
    }
    return obj.ToString();
  }

  public static object RightShift(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:  return IntOps.RightShift((int)(byte)a, b);
      case TypeCode.Int16: return IntOps.RightShift((int)(short)a, b);
      case TypeCode.Int32: return IntOps.RightShift((int)a, b);
      case TypeCode.Int64: return LongOps.RightShift((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.RightShift((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.RightShift((int)(sbyte)a, b);
      case TypeCode.UInt16: return IntOps.RightShift((int)(short)a, b);
      case TypeCode.UInt32: return LongOps.RightShift((long)(uint)a, b);
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.RightShift((long)v, b) : IntegerOps.RightShift(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for >>: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static string Str(object o)
  { TypeCode tc = Convert.GetTypeCode(o);
    if(tc==TypeCode.Object) return Repr(o);
    else if(tc==TypeCode.Empty) return "[NULL]";
    else return o.ToString();
  }

  public static object Subtract(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Byte:    return IntOps.Subtract((int)(byte)a, b);
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a - (Decimal)b;
        try { return (Decimal)a - Convert.ToDecimal(b); }
        catch(InvalidCastException) { break; }
      case TypeCode.Double:  return FloatOps.Subtract((double)a, b);
      case TypeCode.Int16: return IntOps.Subtract((int)(short)a, b);
      case TypeCode.Int32: return IntOps.Subtract((int)a, b);
      case TypeCode.Int64: return LongOps.Subtract((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Subtract((Integer)a, b);
        if(a is Complex) return ComplexOps.Subtract((Complex)a, b);
        break;
      case TypeCode.SByte: return IntOps.Subtract((int)(sbyte)a, b);
      case TypeCode.Single: return FloatOps.Subtract((float)a, b);
      case TypeCode.UInt16: return IntOps.Subtract((int)(short)a, b);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Subtract((int)v, b) : LongOps.Subtract((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Subtract((long)v, b) : IntegerOps.Subtract(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for -: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  public static SyntaxErrorException SyntaxError(string message) { return new SyntaxErrorException(message); }
  public static SyntaxErrorException SyntaxError(string format, params object[] args)
  { return new SyntaxErrorException(string.Format(format, args));
  }
  public static SyntaxErrorException SyntaxError(Node node, string message)
  { return new SyntaxErrorException(message); // TODO: improve this with source information
  }
  public static SyntaxErrorException SyntaxError(Node node, string format, params object[] args)
  { return SyntaxError(node, string.Format(format, args));
  }

  public static double ToFloat(object o)
  { if(o is double) return (double)o;
    if(o is Complex)
    { Complex c = (Complex)o;
      if(c.imag==0) return c.real;
    }

    try { return Convert.ToDouble(o); }
    catch(FormatException) { throw ValueError("string does not contain a valid float"); }
    catch(OverflowException) { throw ValueError("too big for float"); }
    catch(InvalidCastException) { throw TypeError("expected float, but got {0}", TypeName(o)); }
  }

  public static string ToHex(uint number, int minlen)
  { const string cvt = "0123456789ABCDEF";

    unsafe
    { char* chars = stackalloc char[8];
      int len = 0;
      do
      { chars[8 - ++len] = cvt[(int)(number&0xF)];
        number >>= 4;
      } while(number!=0 && len<minlen);
      return new string(chars, 8-len, len);
    }
  }

  public static int ToInt(object o)
  { if(o is int) return (int)o;

    try
    { switch(Convert.GetTypeCode(o))
      { case TypeCode.Boolean: return (bool)o ? 1 : 0;
        case TypeCode.Byte: return (byte)o;
        case TypeCode.Char: return (char)o;
        case TypeCode.Decimal: return (int)(Decimal)o;
        case TypeCode.Double: return checked((int)(double)o);
        case TypeCode.Int16: return (short)o;
        case TypeCode.Int64: return checked((int)(long)o);
        case TypeCode.SByte: return (sbyte)o;
        case TypeCode.Single: return checked((int)(float)o);
        case TypeCode.String:
          try { return int.Parse((string)o); }
          catch(FormatException) { throw ValueError("string does not contain a valid int"); }
        case TypeCode.UInt16: return (int)(ushort)o;
        case TypeCode.UInt32: return checked((int)(uint)o);
        case TypeCode.UInt64: return checked((int)(ulong)o);
        default: return checked((int)Convert.ToSingle(o)); // we do it this way so it truncates
      }
    }
    catch(FormatException) { throw ValueError("string does not contain a valid int"); }
    catch(OverflowException) { goto toobig; }
    catch(InvalidCastException) { throw TypeError("expected int, but got {0}", TypeName(o)); }
    toobig: throw ValueError("too big for int");
  }

  public static long ToLong(object o)
  { try
    { switch(Convert.GetTypeCode(o))
      { case TypeCode.Boolean: return (bool)o ? 1 : 0;
        case TypeCode.Byte: return (byte)o;
        case TypeCode.Char: return (char)o;
        case TypeCode.Decimal: return (long)(Decimal)o;
        case TypeCode.Double: return checked((long)(double)o);
        case TypeCode.Int16: return (short)o;
        case TypeCode.Int32: return (int)o;
        case TypeCode.Int64: return (long)o;
        case TypeCode.SByte: return (sbyte)o;
        case TypeCode.Single: return checked((long)(float)o);
        case TypeCode.String:
          try { return long.Parse((string)o); }
          catch(FormatException) { throw ValueError("string does not contain a valid long"); }
        case TypeCode.UInt16: return (long)(ushort)o;
        case TypeCode.UInt32: return (long)(uint)o;
        case TypeCode.UInt64: return checked((long)(ulong)o);
        default: return checked((long)Convert.ToSingle(o));
      }
    }
    catch(FormatException) { throw ValueError("string does not contain a valid long"); }
    catch(OverflowException) { throw ValueError("too big for long"); } // TODO: allow conversion to long integer?
    catch(InvalidCastException) { throw TypeError("expected long, but got {0}", TypeName(o)); }
  }

  public static TypeErrorException TypeError(string message) { return new TypeErrorException(message); }
  public static TypeErrorException TypeError(string format, params object[] args)
  { return new TypeErrorException(string.Format(format, args));
  }

  public static string TypeName(object o) { return TypeName(o==null ? null : o.GetType()); }
  public static string TypeName(Type type)
  { if(type==null) return "nil";
    switch(Type.GetTypeCode(type))
    { case TypeCode.Boolean: return "bool";
      case TypeCode.Empty: return "nil";
      case TypeCode.Byte:  case TypeCode.SByte: return "fixnum8";
      case TypeCode.Int16: case TypeCode.UInt16: return "fixnum16";
      case TypeCode.Int32: case TypeCode.UInt32: return "fixnum32";
      case TypeCode.Int64: case TypeCode.UInt64: return "fixnum64";
      case TypeCode.Char: return "char";
      case TypeCode.Object:
        if(type==typeof(Symbol)) return "symbol";
        if(type==typeof(Pair)) return "pair";
        if(type==typeof(object[])) return "vector";
        if(type==typeof(IProcedure)) return "procedure";
        if(type==typeof(Integer)) return "bigint";
        if(type==typeof(Complex)) return "complex";
        if(type==typeof(MultipleValues)) return "multiplevalues";
        if(type==typeof(Reference)) return "ref";
        if(type==typeof(Promise)) return "promise";
        if(type==typeof(Type)) return "type";
        goto default;
      case TypeCode.Double: return "flonum64";
      case TypeCode.Single: return "flonum32";
      case TypeCode.String: return "string";
      default: return type.FullName;
    }
  }

  public static ValueErrorException ValueError(string message)
  { return new ValueErrorException(message);
  }
  public static ValueErrorException ValueError(string format, params object[] args)
  { return new ValueErrorException(string.Format(format, args));
  }

  public static readonly object Missing = new Singleton("<Missing>");
  public static readonly object FALSE=false, TRUE=true;
  public static readonly object[] EmptyArray = new object[0];
  [ThreadStatic] public static Stack ExceptionStack;
  [ThreadStatic] public static object LastPtr; // .last

  static bool IsIn(Type[] typeArr, Type type)
  { for(int i=0; i<typeArr.Length; i++) if(typeArr[i]==type) return true;
    return false;
  }

  static readonly Type[][] typeConv = 
  { // FROM
    new Type[] { typeof(int), typeof(double), typeof(short), typeof(long), typeof(float) }, // sbyte
    new Type[] // byte
    { typeof(int), typeof(double), typeof(uint), typeof(short), typeof(ushort), typeof(long), typeof(ulong),
      typeof(float)
    },
    new Type[] { typeof(int), typeof(double), typeof(long), typeof(float) }, // short
    new Type[] { typeof(int), typeof(double), typeof(uint), typeof(long), typeof(ulong), typeof(float) }, // ushort
    new Type[] { typeof(double), typeof(long), typeof(float) }, // int
    new Type[] { typeof(double), typeof(long), typeof(ulong), typeof(float) }, // uint
    new Type[] { typeof(double), typeof(float) }, // long
    new Type[] { typeof(double), typeof(float) }, // ulong
    new Type[] // char
    { typeof(int), typeof(double), typeof(ushort), typeof(uint), typeof(long), typeof(ulong), typeof(float)
    },

    // TO
    new Type[] // bool
    { typeof(int), typeof(byte), typeof(char), typeof(sbyte), typeof(short), typeof(ushort), typeof(uint),
      typeof(long), typeof(ulong)
    }
  };
}
#endregion

#region Pair
public sealed class Pair
{ public Pair(object car, object cdr) { Car=car; Cdr=cdr; }

  public override string ToString()
  { if(Mods.Srfi1.circularListP.core(this)) return "(!!)";

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.Append('(');
    bool sep=false;

    Pair pair=this, next;
    do
    { if(sep) sb.Append(' ');
      else sep=true;
      sb.Append(Ops.Repr(pair.Car));
      next = pair.Cdr as Pair;
      if(next==null)
      { if(pair.Cdr!=null) sb.Append(" . ").Append(Ops.Repr(pair.Cdr));
        break;
      }
      else pair=next;
    } while(pair!=null);
    sb.Append(')');
    return sb.ToString();
  }

  public object Car, Cdr;
}
#endregion

#region Promise
public sealed class Promise
{ public Promise(IProcedure form) { Form=form; }
  public override string ToString() { return "#<promise>"; }
  public IProcedure Form;
  public object Value;
}
#endregion

#region Reference
public sealed class Reference
{ public Reference(object value) { Value=value; }
  public override string ToString() { return "#<reference>"; }
  public object Value;
}
#endregion

#region Symbol
public sealed class Symbol
{ public Symbol(string name) { Name=name; }

  public readonly string Name;
  public override string ToString() { return Name; }

  public static Symbol Get(string name)
  { lock(table)
    { Symbol sym = (Symbol)table[name];
      if(sym==null) table[name] = sym = new Symbol(name);
      return sym;
    }
  }

  static readonly Hashtable table = new Hashtable();
}
#endregion

#region Template
public sealed class Template
{ public Template(IntPtr func, string name, int numParams, bool hasList, bool argsClosed)
  { TopLevel=TopLevel.Current; FuncPtr=func; Name=name; NumParams=numParams; HasList=hasList; ArgsClosed=argsClosed;
  }

  public object[] FixArgs(object[] args)
  { if(HasList)
    { int positional = NumParams-1;
      if(args.Length==NumParams) args[positional] = new Pair(args[positional], null);
      else if(args.Length>=positional)
      { object[] nargs = new object[NumParams];
        Array.Copy(args, nargs, positional);
        nargs[positional] = Ops.List(args, positional);
        args = nargs;
      }
      else throw new TargetParameterCountException(Name+": expected at least "+positional+" arguments, but received "+
                                                   args.Length);
    }
    else if(args.Length!=NumParams)
      throw new TargetParameterCountException(Name+": expected "+NumParams+" arguments, but received "+args.Length);
    return args;
  }

  public readonly TopLevel TopLevel;
  public readonly string Name;
  public readonly IntPtr FuncPtr;
  public readonly int  NumParams;
  public readonly bool HasList, ArgsClosed;
}
#endregion

#region Void
public sealed class Void
{ Void() { }
  public static readonly Void Value = new Void();
}
#endregion

} // namespace NetLisp.Backend