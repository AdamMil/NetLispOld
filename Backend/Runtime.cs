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

#region Procedures
public interface IProcedure
{ int MinArgs { get; }
  int MaxArgs { get; }

  object Call(params object[] args);
}

public abstract class SimpleProcedure : IProcedure
{ public SimpleProcedure(string name, int min, int max) { this.name=name; this.min=min; this.max=max; }

  public int MinArgs { get { return min; } }
  public int MaxArgs { get { return max; } }
  public string Name { get { return name; } }

  public abstract object Call(object[] args);
  public override string ToString() { return name; }

  protected void CheckArity(object[] args) { Ops.CheckArity(name, args.Length, min, max); }

  protected string name;
  protected int min, max;
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
{ public Binding(string name) { Value=Unbound; Name=name; }

  public override bool Equals(object obj) { return this==obj; }
  public override int GetHashCode() { return Name.GetHashCode(); }

  public object Value;
  public string Name;

  public readonly static object Unbound = new Singleton("<UNBOUND>");
}
#endregion

// FIXME: if closure accesses top-level environment (by calling EVAL), it will get the TL of the caller,
//        not of where it was defined
#region Closure
public abstract class Closure : IProcedure
{ public int MinArgs { get { return Template.NumParams; } }
  public int MaxArgs { get { return Template.HasList ? -1 : Template.NumParams; } }

  public abstract object Call(params object[] args);
  public override string ToString() { return Template.Name==null ? "#<lambda>" : "#<lambda '"+Template.Name+"'>"; }

  public Template Template;
  public LocalEnvironment Environment;
}
#endregion

#region RG (stuff that can't be written in C#)
public sealed class RG
{ static RG()
  { TypeGenerator tg;
    CodeGenerator cg;

    #region Closure
    { tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed, "ClosureF", typeof(Closure));
      cg = tg.DefineConstructor(new Type[] { typeof(Template), typeof(LocalEnvironment) });
      cg.EmitThis();
      cg.EmitArgGet(0);
      cg.EmitFieldSet(typeof(Closure), "Template");
      cg.EmitThis();
      cg.EmitArgGet(1);
      cg.EmitFieldSet(typeof(Closure), "Environment");
      cg.EmitReturn();
      cg.Finish();

      cg = tg.DefineMethodOverride(typeof(Closure), "Call", true);
      cg.EmitThis();
      cg.EmitFieldGet(typeof(Closure), "Environment");

      cg.EmitThis();
      cg.EmitFieldGet(typeof(Closure), "Template");
      cg.EmitArgGet(0);
      cg.EmitCall(typeof(Template), "FixArgs");

      cg.EmitThis();
      cg.EmitFieldGet(typeof(Closure), "Template");
      cg.EmitFieldGet(typeof(Template), "FuncPtr");
      cg.ILG.Emit(OpCodes.Tailcall);
      cg.ILG.EmitCalli(OpCodes.Calli, CallingConventions.Standard, typeof(object),
                       new Type[] { typeof(LocalEnvironment), typeof(object[]) }, null);
      cg.EmitReturn();
      cg.Finish();

      ClosureType = tg.FinishType();
    }
    #endregion
  }

  public static readonly Type ClosureType;
}
#endregion

#region Environments
public sealed class TopLevel
{ public enum NS { Main, Macro }

  public void Bind(string name, object value)
  { Binding bind = (Binding)Globals[name];
    if(bind==null) Globals[name] = bind = new Binding(name);
    bind.Value = value;
  }

  public bool Contains(string name) { return Globals.Contains(name); }

  public object Get(string name)
  { Binding obj = (Binding)Globals[name];
    if(obj==null || obj.Value==Binding.Unbound) throw new Exception("no such name"); // FIXME: use a different exception
    return obj.Value;
  }

  public bool Get(string name, out object value)
  { Binding obj = (Binding)Globals[name];
    if(obj==null || obj.Value==Binding.Unbound) { value=null; return false; }
    value = obj.Value;
    return true;
  }

  public Binding GetBinding(string name)
  { Binding obj = (Binding)Globals[name];
    if(obj==null) Globals[name] = obj = new Binding(name);
    return obj;
  }

  public Module GetModule(string name)
  { return Modules==null ? null : (Module)Modules[name];
  }

  public void Set(string name, object value)
  { Binding obj = (Binding)Globals[name];
    if(obj==null) throw new Exception("no such name"); // FIXME: ex
    obj.Value = value;
  }

  public void SetModule(string name, Module module)
  { if(Modules==null) Modules = new ListDictionary();
    Modules[name] = module;
  }

  public Hashtable Globals=new Hashtable(), Macros=new Hashtable();
  public ListDictionary Modules;
  
  [ThreadStatic] public static TopLevel Current;
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

  public override string ToString()
  {
    return base.ToString ();
  }
}
#endregion

#region Module
public class Module
{ public Module(string name) { Name=name; TopLevel=new TopLevel(); }

  public IDictionary GetExportDict()
  { Hashtable hash = new Hashtable();
    foreach(Export e in Exports) hash[e.AsName] = TopLevel.Get(e.Name);
    return hash;
  }

  public struct Export
  { public Export(string name) { Name=name; AsName=name; NS=TopLevel.NS.Main; }
    public Export(string name, TopLevel.NS ns) { Name=name; AsName=name; NS=ns; }
    public Export(string name, string asName) { Name=name; AsName=asName; NS=TopLevel.NS.Main; }
    public Export(string name, string asName, TopLevel.NS ns) { Name=name; AsName=asName; NS=ns; }
    public readonly string Name, AsName;
    public readonly TopLevel.NS NS;
  }

  public void ImportAll(TopLevel top)
  { foreach(Export e in Exports)
      if(e.NS==TopLevel.NS.Main) top.Bind(e.AsName, TopLevel.Get(e.Name));
      else top.Macros[e.AsName] = TopLevel.Macros[e.Name];
  }

  public readonly TopLevel TopLevel;
  public readonly string Name;
  public Export[] Exports;

  internal void AddBuiltins(Type type)
  { foreach(MethodInfo mi in type.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly))
    { object[] attrs = mi.GetCustomAttributes(typeof(SymbolNameAttribute), false);
      string name = attrs.Length==0 ? mi.Name : ((SymbolNameAttribute)attrs[0]).Name;
      TopLevel.Bind(name, Interop.MakeFunctionWrapper(mi, true));
    }

    foreach(Type ptype in type.GetNestedTypes(BindingFlags.Public))
      if(ptype.IsSubclassOf(typeof(Primitive)))
      { Primitive prim = (Primitive)ptype.GetConstructor(Type.EmptyTypes).Invoke(null);
        TopLevel.Bind(prim.Name, prim);
      }
  }

  internal void CreateExports() // FIXME: this exports objects imported from the base (parent) module
  { Hashtable hash = new Hashtable();
    foreach(string name in TopLevel.Globals.Keys)
{ if(((Binding)TopLevel.Globals[name]).Value==Binding.Unbound) continue; // FIXME: don't allow free variables in modules
      if(!name.StartsWith("#_")) hash[name] = new Module.Export(name);
}
    foreach(string name in TopLevel.Macros.Keys)
      if(!name.StartsWith("#_")) hash[name] = new Module.Export(name, TopLevel.NS.Macro);

    Exports = new Export[hash.Count];
    hash.Values.CopyTo(Exports, 0);
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
  { if(bind.Value==Binding.Unbound) throw new Exception("use of unbound variable: "+bind.Name);
    return bind;
  }

  public static object CheckVariable(object value, string name)
  { if(value==Binding.Unbound) throw new Exception("use of unbound variable: "+name);
    return value;
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

  public static Pair List(params object[] items) { return ListSlice(0, items); }
  public static Pair ListSlice(int start, params object[] items)
  { Pair pair=null;
    for(int i=items.Length-1; i>=start; i--) pair = new Pair(items[i], pair);
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
      case TypeCode.Empty: return "nil";
      case TypeCode.Double: return ((double)obj).ToString("R");
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
      default: return obj.ToString();
    }
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

  static string Source(Node node) { throw new NotImplementedException(); }


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
  public static TypeErrorException TypeError(Node node, string format, params object[] args)
  { return new TypeErrorException(Source(node)+string.Format(format, args));
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
        if(type==typeof(IProcedure)) return "procedure";
        if(type==typeof(Integer)) return "bigint";
        if(type==typeof(Complex)) return "complex";
        if(type==typeof(MultipleValues)) return "multiplevalues";
        goto default;
      case TypeCode.Double: return "flonum64";
      case TypeCode.Single: return "flonum32";
      case TypeCode.String: return "string";
      default: return type.FullName;
    }
  }

  public static ValueErrorException ValueError(string format, params object[] args)
  { return new ValueErrorException(string.Format(format, args));
  }
  public static ValueErrorException ValueError(Node node, string format, params object[] args)
  { return new ValueErrorException(Source(node)+string.Format(format, args));
  }

  public static readonly object Missing = new Singleton("<Missing>");
  public static readonly object FALSE=false, TRUE=true;
  public static readonly object[] EmptyArray = new object[0];

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
  { Symbol sym = (Symbol)table[name];
    if(sym==null) table[name] = sym = new Symbol(name);
    return sym;
  }
  
  static readonly Hashtable table = new Hashtable();
}
#endregion

#region Template
public sealed class Template
{ public Template(IntPtr func, string name, int numParams, bool hasList)
  { FuncPtr=func; Name=name; NumParams=numParams; HasList=hasList;
  }

  public object[] FixArgs(object[] args)
  { if(HasList)
    { int positional = NumParams-1;
      if(args.Length==NumParams) args[positional] = new Pair(args[positional], null);
      else if(args.Length>=positional)
      { object[] nargs = new object[NumParams];
        Array.Copy(args, nargs, positional);
        nargs[positional] = Ops.ListSlice(positional, args);
        args = nargs;
      }
      else throw new Exception("expected at least "+positional+" arguments, but received "+args.Length); // FIXME: use other exception
    }
    else if(args.Length!=NumParams) throw new Exception("expected "+NumParams+" arguments, but received "+args.Length); // FIXME: use other exception  }
    return args;
  }

  public readonly string Name;
  public readonly IntPtr FuncPtr;
  public readonly int  NumParams;
  public readonly bool HasList;
}
#endregion

#region Void
public sealed class Void
{ Void() { }
  public static readonly Void Value = new Void();
}
#endregion

} // namespace NetLisp.Backend