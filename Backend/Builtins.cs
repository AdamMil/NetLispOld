using System;
using System.Collections;

namespace NetLisp.Backend.Modules
{

public sealed class Builtins
{ static Builtins()
  { IDictionary dict = ReflectedType.FromType(typeof(Builtins)).Dict;
    _append = (ICallable)dict["append"];
    _initialExpander = (ICallable)dict["initial-expander"];
  }

  public static object apply(object func, Pair args) { return Ops.Call(func, Ops.ListToArray(args)); }

  public static Pair append(params Pair[] pairs)
  { if(pairs.Length==0) return null;
    for(int i=0; i<pairs.Length-1; i++)
    { pairs[i] = listCopy(pairs[i]);
      last(pairs[i]).Cdr = pairs[i+1];
    }
    return pairs[0];
  }

  public static object car(Pair p) { return p.Car; }
  public static object cdr(Pair p) { return p.Cdr; }

  public static object cadr(Pair p)
  { Pair next = p.Cdr as Pair;
    if(next==null) throw new Exception(); // FIXME: ex
    return next.Car;
  }
  public static object cdar(Pair p)
  { Pair next = p.Car as Pair;
    if(next==null) throw new Exception(); // FIXME: ex
    return next.Cdr;
  }
  public static object cddr(Pair p)
  { Pair next = p.Cdr as Pair;
    if(next==null) throw new Exception(); // FIXME: ex
    return next.Cdr;
  }

  public static Snippet compile(object obj) { return Ops.CompileRaw(Ops.Call("expand", obj)); }
  public static Pair cons(object car, object cdr) { return new Pair(car, cdr); }

  [SymbolName("eq?")]
  public static bool eqP(object a, object b) { return a==b; }

  public static object eval(object obj)
  { Snippet snip = obj as Snippet;
    if(snip==null) snip = compile(obj);
    return snip.Run();
  }

  public static object expand(object form) { return initialExpander(form, _initialExpander); }

  [SymbolName("expander?")]
  public static bool expanderP(object obj)
  { Function func = obj as Function;
    return func!=null && func.Macro;
  }

  [SymbolName("inexact->exact")]
  public static object inexactToExact(object obj) { throw new NotImplementedException(); }

  [SymbolName("initial-expander")]
  public static object initialExpander(object form, ICallable expander)
  { if(expander==null) throw new ArgumentNullException("expander");
    Pair pair = form as Pair;
    if(pair==null) return form;
    Symbol sym = pair.Car as Symbol;
    if(sym!=null)
    { object obj;
      if(Frame.Current.Get(sym.Name, out obj))
      { Function func = obj as Function;
        if(func!=null && func.Macro) return func.Call(form, expander);
      }
    }
    return map(new ixLambda((ICallable)expander), pair);
  }

  [SymbolName("install-expander")]
  public static object installExpander(Symbol sym, Function func)
  { func.Macro = true;
    Frame.Current.Bind(sym.Name, func);
    return sym;
  }

  public static Pair last(Pair pair)
  { while(true)
    { Pair next = pair.Cdr as Pair;
      if(next==null) return pair;
      pair = next;
    }
  }

  public static int length(object obj)
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

  public static Pair list(params object[] items) { return Ops.List(items); }

  [SymbolName("list-copy")]
  public static Pair listCopy(Pair list)
  { if(list==null) return null;
    Pair head=cons(list.Car, list.Cdr), tail=head;
    while(true)
    { list = list.Cdr as Pair;
      if(list==null) return head;
      Pair next = cons(list.Car, list.Cdr);
      tail.Cdr = next;
      tail = next;
    }
  }

  public static Pair map(ICallable func, params Pair[] pairs)
  { object[] args = new object[pairs.Length];
    Pair head=null, tail=null;
    while(true)
    { for(int i=0; i<pairs.Length; i++)
      { if(pairs[i]==null) return head;
        args[i] = pairs[i].Car;
        pairs[i] = pairs[i].Cdr as Pair;
      }
      Pair next = cons(func.Call(args), null);
      if(head==null) head=tail=next;
      else { tail.Cdr=next; tail=next; }
    }
  }

  public static bool not(object obj) { return !Ops.IsTrue(obj); }

  [SymbolName("pair?")]
  public static bool pairP(object obj) { return obj is Pair; }
  
  sealed class ixLambda : ICallable
  { public ixLambda(ICallable expander) { this.expander=expander; }
    
    public object Call(params object[] args)
    { if(args.Length!=1) throw new Exception(); // FIXME: ex
      return expander.Call(args[0], expander);
    }

    ICallable expander;
  }

  static readonly ICallable _append, _initialExpander;
}

} // namespace NetLisp.Backend.Modules