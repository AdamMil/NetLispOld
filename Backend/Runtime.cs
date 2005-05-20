using System;
using System.Collections;
using System.Collections.Specialized;

namespace NetLisp.Backend
{

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
}
#endregion

#region Ops
public sealed class Ops
{ Ops() { }

  public static object Car(Pair p) { return p.Car; }
  public static object Cdr(Pair p) { return p.Cdr; }
  public static Pair Cons(object car, object cdr) { return new Pair(car, cdr); }

  public static object Eval(object obj)
  { return obj is Pair || obj is Symbol ? AST.Create(MacroExpand(obj)).Evaluate(new Frame()) : obj;
  }

  public static object InexactToExact(object obj) { throw new NotImplementedException(); }

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

  public static int Length(object obj)
  { Pair pair = obj as Pair;
    if(pair!=null)
    { int total=1;
      while(true)
      { pair = pair.Cdr as Pair;
        if(pair==null) break;
        total++;
      }
      return total;
    }
    else throw new Exception("unhandled type"); // FIXME: use another exception
  }

  public static object MacroExpand(object obj) { return obj; }

  public static string Repr(object obj)
  { if(obj==null) return "nil";
    if(obj is bool) return (bool)obj ? "#t" : "#f";
    return obj.ToString();
  }

  internal static Pair List(params object[] items) { return List2(0, items); }
  internal static Pair List2(int start, params object[] items)
  { if(items.Length<=start) return null;
    Pair head=Cons(items[start], null), tail=head;
    for(; start<items.Length; start++)
    { Pair next=Cons(items[start], null);
      tail.Cdr = next;
      tail     = next;
    }
    return head;
  }
  
  internal static Pair DottedList(object last, params object[] items)
  { Pair head=Cons(items[0], null), tail=head;
    for(int i=1; i<items.Length; i++)
    { Pair next=Cons(items[i], null);
      tail.Cdr = next;
      tail     = next;
    }
    tail.Cdr = last;
    return head;
  }

  internal static object FastCadr(Pair pair) { return ((Pair)pair.Cdr).Car; }
  internal static object FastCddr(Pair pair) { return ((Pair)pair.Cdr).Cdr; }

  internal static Pair List2(object first, params object[] items)
  { Pair head=Cons(first, null), tail=head;
    for(int i=0; i<items.Length; i++)
    { Pair next=Cons(items[i], null);
      tail.Cdr = next;
      tail     = next;
    }
    return head;
  }
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

  public static readonly Symbol If=Get("if"), Lambda=Get("lambda"), Let=Get("let"), Set=Get("set!");
}
#endregion

} // namespace NetLisp.Backend