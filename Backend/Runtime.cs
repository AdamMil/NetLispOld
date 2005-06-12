using System;
using System.Collections;
using System.Collections.Specialized;
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

#region Interfaces
public interface IDescriptor
{ object Get(object instance);
}

public interface IDataDescriptor : IDescriptor
{ void Set(object instance, object value);
}

public interface IHasAttributes
{ bool GetAttr(string name, out object value);
  void SetAttr(string key, object value);
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

  public readonly static object Unbound = "<UNBOUND>";
}
#endregion

// FIXME: if closure accesses top-level environment (by calling EVAL), it will get the TL of the caller,
//        not of where it was defined
#region Closure
public abstract class Closure : IProcedure
{ public int MinArgs { get { return Template.ParamNames.Length; } }
  public int MaxArgs { get { return Template.HasList ? -1 : Template.ParamNames.Length; } }
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

#region Environment
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
  
  [ThreadStatic] public static TopLevel Current;
}

public sealed class LocalEnvironment
{ public LocalEnvironment(LocalEnvironment parent, object[] values) { Parent=parent; Values=values; }
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

  public static AttributeErrorException AttributeError(string format, params object[] args)
  { return new AttributeErrorException(string.Format(format, args));
  }

  public static object Call(string name) { return Call(GetGlobal(name), EmptyArray); }
  public static object Call(string name, params object[] args) { return Call(GetGlobal(name), args); }
  public static object Call(object func, params object[] args) { return ExpectProcedure(func).Call(args); }

  public static Snippet CompileRaw(object obj) { return SnippetMaker.Generate(AST.Create(obj)); }

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
    if(to.IsSubclassOf(typeof(Delegate))) // we use None if both are delegates because we already checked above that it's not the right type of delegate
      return from.IsSubclassOf(typeof(Delegate)) ? Conversion.None : Conversion.Unsafe;
    return Conversion.None;
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

  public static char ExpectChar(object obj) { return (char)obj; } // FIXME: bad
  public static int ExpectInt(object obj) { return (int)obj; } // FIXME: bad
  public static string ExpectString(object obj) { return (string)obj; } // FIXME: bad

  public static Pair ExpectPair(object obj)
  { Pair ret = obj as Pair;
    if(ret!=null) return ret;
    throw new ArgumentException("expected pair, but received "+Repr(obj));
  }

  public static IProcedure ExpectProcedure(object obj)
  { IProcedure ret = obj as IProcedure;
    if(ret!=null) return ret;
    throw new ArgumentException("expected function, but received"+Repr(obj));
  }

  public static bool Equal(object a, object b)
  { if(Eqv(a, b)) return true;

    Pair pa=a as Pair;
    if(pa!=null)
    { Pair pb=b as Pair;
      if(pb!=null)
      { do
        { if(!Equal(pa.Car, pb.Car)) return false;
          Pair next=pa.Cdr as Pair;
          if(next==null && pa.Cdr!=null) return Equal(pa.Cdr, pb.Cdr);
          pa = next;
          pb = pb.Cdr as Pair;
        } while(pa!=null && pb!=null);
        return pa==pb;
      }
    }
    return false;
  }

  public static bool Eqv(object a, object b)
  { if(a==b) return true;
    if(a is bool && b is bool) return (bool)a==(bool)b;
    return OpEquals(a, b);
  }

  public static object FastCadr(Pair pair) { return ((Pair)pair.Cdr).Car; }
  public static object FastCddr(Pair pair) { return ((Pair)pair.Cdr).Cdr; }

  // TODO: check whether we can eliminate this (ie, "(eq? #t #t)" still works)
  public static object FromBool(bool value) { return value ? TRUE : FALSE; }

  public static object GetAttr(object o, string name)
  { IHasAttributes iha = o as IHasAttributes;
    if(iha!=null)
    { object ret;
      if(iha.GetAttr(name, out ret)) return ret;
    }
    throw AttributeError("object has no attribute '{0}'", name);
  }

  public static object GetDescriptor(object desc, object instance)
  { if(Convert.GetTypeCode(desc)!=TypeCode.Object) return desc; // TODO: i'm not sure how much this optimization helps (if at all)

    IDescriptor d = desc as IDescriptor;
    return d==null ? desc : d.Get(instance);
  }

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
        // TODO: uncomment these
        //if(a is Integer) return (Integer)a!=0;
        //if(a is Complex) return ComplexOps.NonZero((Complex)a);
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
  { int total=1;
    while(true)
    { pair = pair.Cdr as Pair;
      if(pair==null) break;
      total++;
    }
    return total;
  }

  public static Delegate MakeDelegate(object callable, Type delegateType)
  { return Delegate.CreateDelegate(delegateType, Old.DelegateProxy.Make(callable, delegateType), "Handle");
  }

  public static bool OpEquals(object a, object b) { throw new NotImplementedException(); }

  public static string Repr(object obj)
  { if(obj==null) return "nil";
    if(obj is bool) return (bool)obj ? "#t" : "#f";
    return obj.ToString();
  }

  public static void SetAttr(object value, object o, string name)
  { IHasAttributes iha = o as IHasAttributes;
    if(iha!=null) iha.SetAttr(name, value);
    else throw AttributeError("object has no attribute '{0}'", name);
  }

  public static bool SetDescriptor(object desc, object instance, object value)
  { if(Convert.GetTypeCode(desc)!=TypeCode.Object) return false; // TODO: i'm not sure how much this optimization helps (if at all)
    IDataDescriptor dd = desc as IDataDescriptor;
    if(dd!=null) { dd.Set(instance, value); return true; }
    else return false;
  }

  static string Source(Node node) { throw new NotImplementedException(); }

  public static TypeErrorException TypeError(string format, params object[] args)
  { return new TypeErrorException(string.Format(format, args));
  }
  public static TypeErrorException TypeError(Node node, string format, params object[] args)
  { return new TypeErrorException(Source(node)+string.Format(format, args));
  }

  public static string TypeName(object o) { return "TYPENAME - FIXME"; } // FIXME: return GetDynamicType(o).__name__.ToString(); }

  public static ValueErrorException ValueError(string format, params object[] args)
  { return new ValueErrorException(string.Format(format, args));
  }
  public static ValueErrorException ValueError(Node node, string format, params object[] args)
  { return new ValueErrorException(Source(node)+string.Format(format, args));
  }

  public static readonly object Missing = "<Missing>";
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

#region Symbol
public sealed class Symbol
{ Symbol(string name) { Name=name; }
  static readonly Hashtable table = new Hashtable();

  public readonly string Name;

  public static Symbol Get(string name)
  { Symbol sym = (Symbol)table[name];
    if(sym==null) table[name] = sym = new Symbol(name);
    return sym;
  }
  
  public override string ToString() { return Name; }
}
#endregion

#region Template
public sealed class Template
{ public Template(IntPtr func, string[] paramNames, bool hasList)
  { FuncPtr=func; ParamNames=paramNames; HasList=hasList;
  }

  public object[] FixArgs(object[] args)
  { if(HasList)
    { int positional = ParamNames.Length-1;
      if(args.Length<positional) throw new Exception("too few arguments"); // FIXME: use other exception
      else if(args.Length!=positional)
      { object[] nargs = new object[ParamNames.Length];
        Array.Copy(args, nargs, positional);
        nargs[positional] = Ops.ListSlice(positional, args);
        args = nargs;
      }
      else args[positional] = new Pair(args[positional], null);
    }
    else if(args.Length!=ParamNames.Length) throw new Exception("wrong number of arguments"); // FIXME: use other exception  }
    return args;
  }

  public readonly string[] ParamNames;
  public readonly IntPtr FuncPtr;
  public readonly bool HasList;
  public bool Macro;
}
#endregion

#region Void
public sealed class Void
{ Void() { }
  public static readonly Void Value = new Void();
}
#endregion

} // namespace NetLisp.Backend