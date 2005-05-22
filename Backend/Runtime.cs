using System;
using System.Collections;
using System.Collections.Specialized;

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
public interface ICallable
{ object Call(params object[] args);
}

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

#region Enums
[Flags]
public enum Conversion
{ Unsafe=1, Safe=3, Reference=5, Identity=7, None=8, Overflow=10,
  Failure=8, Success=1
}
#endregion

#region Frame
public sealed class Frame
{ public Frame() { Locals=Globals=new HybridDictionary(); }
  public Frame(Frame parent) : this(parent, new HybridDictionary()) { }
  public Frame(Frame parent, IDictionary locals)
  { Locals=locals;
    if(parent!=null) { Parent=parent; Globals=parent.Globals; }
    else Globals=locals;
  }
  public Frame(IDictionary locals, IDictionary globals) { Locals=locals; Globals=globals; }

  public void Bind(string name, object value) { Locals[name]=value; }
  public void Unbind(string name) { Locals.Remove(name); }

  public bool Contains(string name) { return Locals.Contains(name) ? true : Parent==null ? Globals.Contains(name) : Parent.Contains(name); }

  public object Get(string name)
  { object obj = Locals[name];
    if(obj!=null || Locals.Contains(name)) return obj;
    return Parent==null ? GetGlobal(name) : Parent.Get(name);
  }

  public object GetGlobal(string name)
  { object obj = Globals[name];
    if(obj!=null || Globals.Contains(name)) return obj;
    throw new Exception("no such name"); // FIXME: use a different exception
  }

  public void Set(string name, object value)
  { if(Locals.Contains(name)) Locals[name]=value;
    else if(Parent!=null) Parent.Set(name, value);
    else Globals[name]=value;
  }

  public void SetGlobal(string name, object value) { Globals[name]=value; }

  public Frame Parent;
  public IDictionary Locals, Globals;
  
  [ThreadStatic]
  public static Frame Current;
}
#endregion

#region Ops
public sealed class Ops
{ Ops() { }

  public static AttributeErrorException AttributeError(string format, params object[] args)
  { return new AttributeErrorException(string.Format(format, args));
  }

  public static object Call(string name) { return Call(Frame.Current.GetGlobal(name), EmptyArray); }
  public static object Call(string name, params object[] args) { return Call(Frame.Current.GetGlobal(name), args); }

  public static object Call(object func, object[] args)
  { ICallable f = func as ICallable;
    if(f==null) throw new Exception("not a function"); // FIXME: ex
    return f.Call(args);
  }

  public static Snippet CompileRaw(object obj) { return SnippetMaker.Generate(AST.Create(obj)); }

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
    // TODO: check whether it's possible to speed up this big block of checks up somehow
    // TODO: add support for Integer, Complex, and Decimal
    if(from.IsPrimitive && to.IsPrimitive)
    { if(from==typeof(int))    return IsIn(typeConv[4], to)   ? Conversion.Safe : Conversion.Unsafe;
      if(to  ==typeof(bool))   return IsIn(typeConv[9], from) ? Conversion.None : Conversion.Safe;
      if(from==typeof(double)) return Conversion.None;
      if(from==typeof(long))   return IsIn(typeConv[6], to) ? Conversion.Safe : Conversion.Unsafe;
      if(from==typeof(char))   return IsIn(typeConv[8], to) ? Conversion.Safe : Conversion.Unsafe;
      if(from==typeof(byte))   return IsIn(typeConv[1], to) ? Conversion.Safe : Conversion.Unsafe;
      if(from==typeof(uint))   return IsIn(typeConv[5], to) ? Conversion.Safe : Conversion.Unsafe;
      if(from==typeof(float))  return to==typeof(double) ? Conversion.Safe : Conversion.None;
      if(from==typeof(short))  return IsIn(typeConv[2], to) ? Conversion.Safe : Conversion.Unsafe;
      if(from==typeof(ushort)) return IsIn(typeConv[3], to) ? Conversion.Safe : Conversion.Unsafe;
      if(from==typeof(sbyte))  return IsIn(typeConv[0], to) ? Conversion.Safe : Conversion.Unsafe;
      if(from==typeof(ulong))  return IsIn(typeConv[7], to) ? Conversion.Safe : Conversion.Unsafe;
    }
    if(from.IsArray && to.IsArray && to.GetElementType().IsAssignableFrom(from.GetElementType()))
      return Conversion.Reference;
    if(to.IsSubclassOf(typeof(Delegate))) // we use None if both are delegates because we already checked above that it's not the right type of delegate
      return from.IsSubclassOf(typeof(Delegate)) ? Conversion.None : Conversion.Unsafe;
    return Conversion.None;
  }

  public static Pair DottedList(object last, params object[] items)
  { Pair head=Modules.Builtins.cons(items[0], null), tail=head;
    for(int i=1; i<items.Length; i++)
    { Pair next=Modules.Builtins.cons(items[i], null);
      tail.Cdr = next;
      tail     = next;
    }
    tail.Cdr = last;
    return head;
  }

  public static object FastCadr(Pair pair) { return ((Pair)pair.Cdr).Car; }
  public static object FastCddr(Pair pair) { return ((Pair)pair.Cdr).Cdr; }

  // TODO: check whether we can eliminate this (ie, "true is true" still works)
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

  public static object GetGlobal(string name) { return Frame.Current.GetGlobal(name); }

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
    for(int i=items.Length-1; i>=0; i--) pair = Modules.Builtins.cons(items[i], pair);
    return pair;
  }
  
  public static Pair List2(object first, params object[] items) { return Modules.Builtins.cons(first, List(items)); }

  public static object[] ListToArray(Pair pair)
  { if(pair==null) return EmptyArray;
    ArrayList items = new ArrayList();
    while(pair!=null) { items.Add(pair.Car); pair = pair.Cdr as Pair; }
    return (object[])items.ToArray(typeof(object));
  }

  public static Delegate MakeDelegate(object callable, Type delegateType)
  { return Delegate.CreateDelegate(delegateType, DelegateProxy.Make(callable, delegateType), "Handle");
  }

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

} // namespace NetLisp.Backend