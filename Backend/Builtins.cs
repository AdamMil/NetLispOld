using System;
using System.Collections;
using System.Reflection;

namespace NetLisp.Backend
{

public sealed class Builtins
{ public static Hashtable GetProcedureDict()
  { Hashtable dict = new Hashtable(ReflectedType.FromType(typeof(Builtins)).Dict);

    foreach(Type type in typeof(Builtins).GetNestedTypes(BindingFlags.Public))
      if(typeof(ICallable).IsAssignableFrom(type))
      { string name;
        object[] attrs = type.GetCustomAttributes(typeof(SymbolNameAttribute), false);
        name = attrs.Length==0 ? type.Name : ((SymbolNameAttribute)attrs[0]).Name;
        dict[name] = type.GetConstructor(Type.EmptyTypes).Invoke(null);
      }

    return dict;
  }

  #region and
  public sealed class and : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectAtLeast(1, args);
      for(int i=0; i<args.Length; i++) if(!Ops.IsTrue(args[i])) return null;
      return args[args.Length-1];
    }
  }
  #endregion

  #region apply
  public sealed class apply : ICallable
  { public object Call(LocalEnvironment func, object[] args)
    { Ops.ExpectExactly(2, args);
      return Ops.Call(args[0], Ops.ListToArray(Ops.ExpectPair(args[1])));
    }
  }
  #endregion
  
  #region append
  public sealed class append : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectAtLeast(2, args);
      Pair head=null, prev=null;
      int i;
      for(i=0; i<args.Length-1; i++)
      { Pair pair=Ops.ExpectPair(args[i]), tail=new Pair(pair.Car, pair.Cdr);
        if(prev==null) head = tail;
        else prev.Cdr = tail;
        while(true)
        { pair = pair.Cdr as Pair;
          if(pair==null) break;
          Pair next = new Pair(pair.Car, pair.Cdr);
          tail.Cdr = next;
          tail = next;
        }
        prev = tail;
      }
      prev.Cdr = args[i];
      return head;
    }
  }
  #endregion

  #region car
  public sealed class car : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(1, args);
      return Ops.ExpectPair(args[0]).Car;
    }
  }
  #endregion

  #region cdr
  public sealed class cdr : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(1, args);
      return Ops.ExpectPair(args[0]).Cdr;
    }
  }
  #endregion

  public static Snippet compile(object obj) { return Ops.CompileRaw(Ops.Call("expand", obj)); }

  #region cons
  public sealed class cons : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(2, args);
      return new Pair(args[0], args[1]);
    }
  }
  #endregion

  #region eq?
  [SymbolName("eq?")]
  public sealed class _eqP : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(2, args);
      return args[0]==args[1];
    }
  }
  #endregion

  public static object eval(object obj)
  { Snippet snip = obj as Snippet;
    if(snip==null) snip = compile(obj);
    return snip.Run(null);
  }

  public static object expand(object form)
  { return Ops.Call(initialExpander.Instance, form, initialExpander.Instance);
  }

  [SymbolName("expander?")]
  public static bool expanderP(object obj)
  { Closure clos = obj as Closure;
    return clos!=null && clos.Template.Macro;
  }

  [SymbolName("inexact->exact")]
  public static object inexactToExact(object obj) { throw new NotImplementedException(); }

  #region initial-expander
  [SymbolName("initial-expander")]
  public sealed class initialExpander : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(2, args);
      ICallable expander = Ops.ExpectFunction(args[1]);

      Pair pair = args[0] as Pair;
      if(pair==null || pair.Cdr!=null && !(pair.Cdr is Pair)) return args[0];
      Symbol sym = pair.Car as Symbol;
      if(sym!=null)
      { object obj;
        if(Ops.GetGlobal(sym.Name, out obj))
        { Closure clos = obj as Closure;
          if(clos!=null && clos.Template.Macro) return clos.Call(clos.Environment, args[0], expander);
        }
      }
      return map(new _ixLambda(expander), pair);
    }
    
    public static readonly initialExpander Instance = new initialExpander();
  }
  #endregion

  [SymbolName("install-expander")]
  public static object installExpander(Symbol sym, Closure func)
  { func.Template.Macro = true;
    TopLevel.Current.Bind(sym.Name, func);
    return sym;
  }

  public static Pair last(Pair pair)
  { while(true)
    { Pair next = pair.Cdr as Pair;
      if(next==null) return pair;
      pair = next;
    }
  }

  #region length
  public sealed class length : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(1, args);
      Pair pair = args[0] as Pair;
      if(pair!=null) return Ops.Length(pair);
      else throw new ArgumentException("length: expects pair");
    }
  }
  #endregion

  [SymbolName("list-copy")]
  public static Pair listCopy(Pair list)
  { if(list==null) return null;
    Pair head=new Pair(list.Car, list.Cdr), tail=head;
    while(true)
    { list = list.Cdr as Pair;
      if(list==null) return head;
      Pair next = new Pair(list.Car, list.Cdr);
      tail.Cdr = next;
      tail = next;
    }
  }

  public static Pair map(ICallable func, params Pair[] pairs)
  { Closure clos = func as Closure;
    Pair head=null, tail=null;
    object[] args = new object[pairs.Length];

    while(true)
    { for(int i=0; i<pairs.Length; i++)
      { if(pairs[i]==null) return head;
        args[i] = pairs[i].Car;
        pairs[i] = pairs[i].Cdr as Pair;
      }
      Pair next = new Pair(clos==null ? func.Call(null, args) : clos.Call(clos.Environment, args), null);
      if(head==null) head=tail=next;
      else { tail.Cdr=next; tail=next; }
    }
  }

  #region not
  public sealed class not : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(1, args);
      return Ops.FromBool(!Ops.IsTrue(args[0]));
    }
  }
  #endregion

  #region null?
  [SymbolName("null?")]
  public sealed class nullP : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(1, args);
      return args[0]==null;
    }
  }
  #endregion

  #region or
  public sealed class or : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectAtLeast(1, args);
      for(int i=0; i<args.Length; i++) if(Ops.IsTrue(args[i])) return args[i];
      return null;
    }
  }
  #endregion

  #region pair?
  [SymbolName("pair?")]
  public sealed class pairP : ICallable
  { public object Call(LocalEnvironment unused, object[] args)
    { Ops.ExpectExactly(1, args);
      return args[0] is Pair;
    }
  }
  #endregion
  
  sealed class _ixLambda : ICallable
  { public _ixLambda(ICallable expander) { this.expander=expander; }
    
    public object Call(LocalEnvironment unused, params object[] args)
    { if(args.Length!=1) throw new Exception(); // FIXME: ex
      return Ops.Call(expander, args[0], expander);
    }

    ICallable expander;
  }
}

} // namespace NetLisp.Backend