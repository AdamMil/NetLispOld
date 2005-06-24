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

public class SymbolNameAttribute : Attribute
{ public SymbolNameAttribute(string name) { Name=name; }
  public string Name;
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

  protected void CheckArity(object[] args)
  { int num = args.Length;
    if(max==-1)
    { if(num<min) throw new ArgumentException(name+": expects at least "+min.ToString()+
                                              " arguments, but received "+args.Length.ToString());
    }
    else if(num<min || num>max)
      throw new ArgumentException(name+": expects "+(min==max ? min.ToString() : min.ToString()+"-"+max.ToString())+
                                  " arguments, but received "+num.ToString());
  }

  internal  bool IsPrimitive;
  protected string name;
  protected int min, max;
}

public abstract class Primitive : SimpleProcedure
{ public Primitive(string name, int min, int max) : base(name, min, max) { IsPrimitive=true; }
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
  public string Name { get { return "#<lambda>"; } }

  public abstract object Call(params object[] args);

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
{ public TopLevel() { Globals=new Hashtable(); }

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

  public void Set(string name, object value)
  { Binding obj = (Binding)Globals[name];
    if(obj==null) throw new Exception("no such name"); // FIXME: ex
    obj.Value = value;
  }

  public Hashtable Globals;
  public Hashtable Macros = new Hashtable();
  
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

#region MultipleValues
public sealed class MultipleValues
{ public MultipleValues(object[] values) { Values=values; }
  public object[] Values;
}
#endregion

#region Ops
public sealed class Ops
{ Ops() { }

  public static object Add(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return IntOps.Add((bool)a ? 1 : 0, b);
      case TypeCode.Byte:    return IntOps.Add((int)(byte)a, b);
      case TypeCode.Char:
        if(b is string) return (char)a+(string)b;
        break;
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a + (Decimal)b;
        try { return (Decimal)a + Convert.ToDecimal(b); }
        catch { break; }
      case TypeCode.Double:  return FloatOps.Add((double)a, b);
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
    { case TypeCode.Boolean:
        if(b is bool) return FromBool((bool)a && (bool)b);
        return IntOps.BitwiseAnd((bool)a ? 1 : 0, b);
      case TypeCode.Byte:  return IntOps.BitwiseAnd((int)(byte)a, b);
      case TypeCode.Int16: return IntOps.BitwiseAnd((int)(short)a, b);
      case TypeCode.Int32: return IntOps.BitwiseAnd((int)a, b);
      case TypeCode.Int64: return LongOps.BitwiseAnd((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.BitwiseAnd((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.BitwiseAnd((int)(sbyte)a, b);
      case TypeCode.UInt16: return IntOps.BitwiseAnd((int)(short)a, b);
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
    { case TypeCode.Boolean:
        if(b is bool) return FromBool((bool)a || (bool)b);
        return IntOps.BitwiseOr((bool)a ? 1 : 0, b);
      case TypeCode.Byte:  return IntOps.BitwiseOr((int)(byte)a, b);
      case TypeCode.Int16: return IntOps.BitwiseOr((int)(short)a, b);
      case TypeCode.Int32: return IntOps.BitwiseOr((int)a, b);
      case TypeCode.Int64: return LongOps.BitwiseOr((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.BitwiseOr((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.BitwiseOr((int)(sbyte)a, b);
      case TypeCode.UInt16: return IntOps.BitwiseOr((int)(short)a, b);
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
    { case TypeCode.Boolean:
        if(b is bool) return FromBool((bool)a != (bool)b);
        return IntOps.BitwiseXor((bool)a ? 1 : 0, b);
      case TypeCode.Byte:  return IntOps.BitwiseXor((int)(byte)a, b);
      case TypeCode.Int16: return IntOps.BitwiseXor((int)(short)a, b);
      case TypeCode.Int32: return IntOps.BitwiseXor((int)a, b);
      case TypeCode.Int64: return LongOps.BitwiseXor((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.BitwiseXor((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.BitwiseXor((int)(sbyte)a, b);
      case TypeCode.UInt16: return IntOps.BitwiseXor((int)(short)a, b);
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
    { case TypeCode.Boolean: return (bool)a ? -2 : -1;
      case TypeCode.Byte:  return ~(int)(byte)a;
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
  { if(a==b) return 0;
    switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean:
        if(b is bool) return (bool)a ? (bool)b ? 0 : 1 : (bool)b ? -1 : 0;
        return IntOps.Compare((bool)a ? 1 : 0, b);
      case TypeCode.Byte: return IntOps.Compare((int)(byte)a, b);
      case TypeCode.Char:
      { string sb = b as string;
        if(sb!=null)
        { if(sb.Length==0) return 1;
          int diff = (int)(char)a - (int)sb[0];
          return diff!=0 ? diff : sb.Length==1 ? 0 : -1;
        }
        else if(b is char) return (int)(char)a - (int)(char)b;
        break;
      }
      case TypeCode.Decimal:
        if(b is Decimal) return ((Decimal)a).CompareTo(b);
        try { return ((Decimal)a).CompareTo(Convert.ToDecimal(b)); }
        catch { break; }
      case TypeCode.Double: return FloatOps.Compare((double)a, b);
      case TypeCode.Empty: return b==null ? 0 : -1;
      case TypeCode.Int16: return IntOps.Compare((int)(short)a, b);
      case TypeCode.Int32: return IntOps.Compare((int)a, b);
      case TypeCode.Int64: return LongOps.Compare((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Compare((Integer)a, b);
        if(a is Complex) return ComplexOps.Compare((Complex)a, b);
        break;
      case TypeCode.SByte: return IntOps.Compare((int)(sbyte)a, b);
      case TypeCode.Single: return FloatOps.Compare((float)a, b);
      case TypeCode.String:
      { string sb = b as string;
        if(sb!=null) return ((string)a).CompareTo(sb);
        else if(b is char)
        { string sa = (string)a;
          if(sa.Length==0) return -1;
          int diff = (int)sa[0] - (int)(char)b;
          return diff!=0 ? diff : sa.Length==1 ? 0 : 1;
        }
        break;
      }
      case TypeCode.UInt16: return IntOps.Compare((int)(short)a, b);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.Compare((int)v, b) : LongOps.Compare((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.Compare((long)v, b) : IntegerOps.Compare(new Integer(v), b);
      }
    }
    return string.Compare(TypeName(a), TypeName(b));
  }

  public static object ConsAll(object[] args)
  { int i = args.Length-1;
    object obj = args[i];
    while(i-- != 0) obj = new Pair(args[i], obj);
    return obj;
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
    { case TypeCode.Boolean: return (bool)a ? IntOps.Divide(1, b) : 0;
      case TypeCode.Byte:    return IntOps.Divide((byte)a, b);
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a / (Decimal)b;
        try { return (Decimal)a / Convert.ToDecimal(b); }
        catch { break; }
      case TypeCode.Double:  return FloatOps.Divide((double)a, b);
      case TypeCode.Int16: return IntOps.Divide((short)a, b);
      case TypeCode.Int32: return IntOps.Divide((int)a, b);
      case TypeCode.Int64: return LongOps.Divide((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Divide((Integer)a, b);
        if(a is Complex) return ComplexOps.Divide((Complex)a, b);
        break;
      case TypeCode.SByte: return IntOps.Divide((sbyte)a, b);
      case TypeCode.Single: return FloatOps.Divide((float)a, b);
      case TypeCode.UInt16: return IntOps.Divide((short)a, b);
      case TypeCode.UInt32: return LongOps.Divide((uint)a, b);
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

  public static char ExpectChar(object obj)
  { try { return (char)obj; }
    catch(InvalidCastException) { throw new ArgumentException("expected character but received "+TypeName(obj)); }
  }

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

  public static object Equal(object a, object b)
  { return FromBool(a is Complex ? ((Complex)a).Equals(b) : Compare(a, b)==0);
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
  { return a==b || a is Complex ? ((Complex)a).Equals(b) : Compare(a, b)==0;
  }

  public static object FastCadr(Pair pair) { return ((Pair)pair.Cdr).Car; }
  public static object FastCddr(Pair pair) { return ((Pair)pair.Cdr).Cdr; }

  public static object FloorDivide(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return (bool)a ? IntOps.FloorDivide(1, b) : 0;
      case TypeCode.Byte:    return IntOps.FloorDivide((byte)a, b);
      case TypeCode.Double:  return FloatOps.FloorDivide((double)a, b);
      case TypeCode.Int16: return IntOps.FloorDivide((short)a, b);
      case TypeCode.Int32: return IntOps.FloorDivide((int)a, b);
      case TypeCode.Int64: return LongOps.FloorDivide((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.FloorDivide((Integer)a, b);
        break;
      case TypeCode.SByte: return IntOps.FloorDivide((sbyte)a, b);
      case TypeCode.Single: return FloatOps.FloorDivide((float)a, b);
      case TypeCode.UInt16: return IntOps.FloorDivide((short)a, b);
      case TypeCode.UInt32: 
      { uint v = (uint)a;
        return v<=int.MaxValue ? IntOps.FloorDivide((int)v, b) : LongOps.FloorDivide((long)v, b);
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)a;
        return v<=long.MaxValue ? LongOps.FloorDivide((long)v, b) : IntegerOps.FloorDivide(new Integer(v), b);
      }
    }
    throw TypeError("unsupported operand types for /: '{0}' and '{1}'", TypeName(a), TypeName(b));
  }

  // TODO: check whether we can eliminate this (ie, "(eq? #t #t)" still works)
  public static object FromBool(bool value) { return value ? TRUE : FALSE; }

  public static object GetGlobal(string name) { return TopLevel.Current.Get(name); }
  public static bool GetGlobal(string name, out object value) { return TopLevel.Current.Get(name, out value); }

  public static bool IsTrue(object a)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return (bool)a;
      case TypeCode.Byte:    return (byte)a!=0;
      case TypeCode.Char:    return (char)a!=0;
      case TypeCode.Decimal: return (Decimal)a!=0;
      case TypeCode.Double:  return (double)a!=0;
      case TypeCode.Empty:   return false;
      case TypeCode.Int16:   return (short)a!=0;
      case TypeCode.Int32:   return (int)a!=0;
      case TypeCode.Int64:   return (long)a!=0;
      case TypeCode.Object:
        if(a is Integer) return (Integer)a!=0;
        if(a is Complex) return ComplexOps.NonZero((Complex)a);
        if(a is ICollection) return ((ICollection)a).Count!=0;
        return true;
      case TypeCode.SByte:  return (sbyte)a!=0;
      case TypeCode.Single: return (float)a!=0;
      case TypeCode.String: return ((string)a).Length>0;
      case TypeCode.UInt16: return (short)a!=0;
      case TypeCode.UInt32: return (uint)a!=0;
      case TypeCode.UInt64: return (ulong)a!=0;
    }
    return true;
  }

  public static object LeftShift(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return IntOps.LeftShift((bool)a ? 1 : 0, b);
      case TypeCode.Byte:  return IntOps.LeftShift((int)(byte)a, b);
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
    throw TypeError("unsupported operand types for <<: '{0}' and '{1}'",
                    TypeName(a), TypeName(b));
  }

  public static object Less(object a, object b) { return FromBool(Compare(a,b)<0); }
  public static object LessEqual(object a, object b) { return FromBool(Compare(a,b)<=0); }

  public static Pair List(params object[] items) { return ListSlice(0, items); }
  public static Pair ListSlice(int start, params object[] items)
  { if(items.Length<=start) return null;
    Pair pair = null;
    for(int i=items.Length-1; i>=0; i--) pair = new Pair(items[i], pair);
    return pair;
  }
  
  public static Pair List2(object first, params object[] items) { return new Pair(first, List(items)); }

  public static object[] ListToArray(Pair pair)
  { if(pair==null) return EmptyArray;
    ArrayList items = new ArrayList();
    while(pair!=null) { items.Add(pair.Car); pair = pair.Cdr as Pair; }
    return (object[])items.ToArray(typeof(object));
  }

  public static int Length(Pair pair)
  { if(pair==null) return 0;
    int total=1;
    while(true)
    { pair = pair.Cdr as Pair;
      if(pair==null) break;
      total++;
    }
    return total;
  }

  public static Delegate MakeDelegate(object callable, Type delegateType)
  { IProcedure proc = callable as IProcedure;
    if(proc==null) throw new ArgumentException("delegate: expected a procedure");
    return Delegate.CreateDelegate(delegateType, Interop.MakeDelegateWrapper(proc, delegateType), "Handle");
  }

  public static object Modulus(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return IntOps.Modulus((bool)a ? 1 : 0, b);
      case TypeCode.Byte:    return IntOps.Modulus((int)(byte)a, b);
      case TypeCode.Decimal: return FloatOps.Modulus(((IConvertible)a).ToDouble(NumberFormatInfo.InvariantInfo), b);
      case TypeCode.Double:  return FloatOps.Modulus((double)a, b);
      case TypeCode.Int16:   return IntOps.Modulus((int)(short)a, b);
      case TypeCode.Int32:   return IntOps.Modulus((int)a, b);
      case TypeCode.Int64:   return LongOps.Modulus((long)a, b);
      case TypeCode.Object:
        if(a is Integer) return IntegerOps.Modulus((Integer)a, b);
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

  public static object Multiply(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return IntOps.Multiply((bool)a ? 1 : 0, b);
      case TypeCode.Byte:    return IntOps.Multiply((int)(byte)a, b);
      case TypeCode.Char:    return new string((char)a, ToInt(b));
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a * (Decimal)b;
        try { return (Decimal)a * Convert.ToDecimal(b); }
        catch { break; }
      case TypeCode.Double:  return FloatOps.Multiply((double)a, b);
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

  public static object More(object a, object b) { return FromBool(Compare(a,b)>0); }
  public static object MoreEqual(object a, object b) { return FromBool(Compare(a,b)>=0); }


  public static object Negate(object a)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return (bool)a ? -1 : 0;
      case TypeCode.Byte:  return -(int)(byte)a;
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

  public static object NotEqual(object a, object b)
  { return FromBool(a is Complex ? !((Complex)a).Equals(b) : Compare(a, b)!=0);
  }

  public static object Power(object a, object b)
  { switch(Convert.GetTypeCode(a))
    { case TypeCode.Boolean: return IntOps.Power((bool)a ? 1 : 0, b);
      case TypeCode.Byte:    return IntOps.Power((int)(byte)a, b);
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
    { case TypeCode.Boolean: return IntOps.RightShift((bool)a ? 1 : 0, b);
      case TypeCode.Byte:  return IntOps.RightShift((int)(byte)a, b);
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
    { case TypeCode.Boolean: return IntOps.Subtract((bool)a ? 1 : 0, b);
      case TypeCode.Byte:    return IntOps.Subtract((int)(byte)a, b);
      case TypeCode.Decimal:
        if(b is Decimal) return (Decimal)a - (Decimal)b;
        try { return (Decimal)a - Convert.ToDecimal(b); }
        catch { break; }
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

  public static string TypeName(object o) { return o==null ? "nil" : o.GetType().FullName; }

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
  { System.Text.StringBuilder sb = new System.Text.StringBuilder();
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
{ public Template(IntPtr func, int numParams, bool hasList)
  { FuncPtr=func; NumParams=numParams; HasList=hasList;
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