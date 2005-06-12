using System;
using System.Collections;
using System.Reflection;

namespace NetLisp.Backend
{

public sealed class Builtins
{ public static Hashtable GetProcedureDict()
  { Hashtable dict = new Hashtable(Old.ReflectedType.FromType(typeof(Builtins)).Dict);

    foreach(Type type in typeof(Builtins).GetNestedTypes(BindingFlags.Public))
      if(type.IsSubclassOf(typeof(Primitive)))
      { Primitive prim = (Primitive)type.GetConstructor(Type.EmptyTypes).Invoke(null);
        dict[prim.Name] = prim;
      }

    return dict;
  }

public static void import(string name) { Interop.Import(name); }
public static void usens(string ns) { Interop.ImportNamespace(ns); }

  #region and
  public sealed class and : Primitive
  { public and() : base("and", 0, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length; i++) if(!Ops.IsTrue(args[i])) return args[i];
      return Ops.TRUE;
    }
  }
  #endregion

  #region apply
  public sealed class apply : Primitive
  { public apply() : base("apply", 2, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);

      int  alen = args.Length-2;
      Pair pair = Ops.ExpectPair(args[alen+1]);
      object[] nargs = new object[Ops.Length(pair) + alen];
      if(alen!=0) Array.Copy(args, 1, nargs, 0, alen);

      do
      { nargs[alen++] = pair.Car;
        pair = pair.Cdr as Pair;
      } while(pair!=null);

      return Ops.Call(args[0], nargs);
    }
  }
  #endregion
  
  #region append
  public sealed class append : Primitive
  { public append() : base("append", 2, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
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

  #region call-with-values
  public sealed class callWithValues : Primitive
  { public callWithValues() : base("call-with-values", 2, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure thunk=Ops.ExpectProcedure(args[0]), func=Ops.ExpectProcedure(args[1]);
      MultipleValues mv = thunk.Call(Ops.EmptyArray) as MultipleValues;
      if(mv==null) throw new ArgumentException("call-with-values: thunk must return using (values)");
      return func.Call(mv.Values);
    }
  }
  #endregion

  // TODO: compositions?
  #region car
  public sealed class car : Primitive
  { public car() : base("car", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Car;
    }
  }
  #endregion

  #region cdr
  public sealed class cdr : Primitive
  { public cdr() : base("cdr", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Cdr;
    }
  }
  #endregion

  // TODO: implement character equivalence predicates: http://www.swiss.ai.mit.edu/projects/scheme/documentation/scheme_6.html#SEC62
  #region char?
  public sealed class charP : Primitive
  { public charP() : base("char?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is char ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region char-upcase
  public sealed class charUpcase : Primitive
  { public charUpcase() : base("char-upcase", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]));
    }
  }
  #endregion

  #region char-downcase
  public sealed class charDowncase : Primitive
  { public charDowncase() : base("char-downcase", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToLower(Ops.ExpectChar(args[0]));
    }
  }
  #endregion
  
  #region char->digit
  public sealed class charToDigit : Primitive
  { public charToDigit() : base("char->digit", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      int num = (int)Ops.ExpectChar(args[0]) - 48;
      int radix = args.Length==2 ? Ops.ExpectInt(args[1]) : 10;
      if(num>48) num -= 32 + 7;
      else if(num>16) num -= 7;
      else if(num>9) return Ops.FALSE;
      return num<0 || num>=radix ? Ops.FALSE : num;
    }
  }
  #endregion

  public static Snippet compile(object obj) { return Ops.CompileRaw(Ops.Call("expand", obj)); }

  [SymbolName("compiled-procedure?")]
  public static object compiledProcedureP(object obj) { return Ops.FromBool(obj is IProcedure); }

  [SymbolName("compound-procedure?")]
  public static object compoundProcedureP(object obj) { return Ops.FALSE; }

  #region cons
  public sealed class cons : Primitive
  { public cons() : base("cons", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return new Pair(args[0], args[1]);
    }
  }
  #endregion

  #region cons*
  public sealed class consAll : Primitive
  { public consAll() : base("cons*", 1, -1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ConsAll(args);
    }
  }
  #endregion

  #region digit->char
  public sealed class digitToChar : Primitive
  { public digitToChar() : base("digit->char", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      int num = Ops.ExpectInt(args[0]);
      int radix = args.Length==2 ? Ops.ExpectInt(args[1]) : 10;
      return num<0 || num>=radix ? Ops.FALSE : convert[num];
    }
    
    const string convert = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
  }
  #endregion

  #region eq?
  public sealed class eqP : Primitive
  { public eqP() : base("eq?", 2, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return args[0]==args[1] ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region eqv?
  public sealed class eqvP : Primitive
  { public eqvP() : base("eqv?", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.Eqv(args[0], args[1]) ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion
  
  #region equal?
  public sealed class equalP : Primitive
  { public equalP() : base("equal?", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.Equal(args[0], args[1]) ? Ops.TRUE : Ops.FALSE;
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
  public sealed class initialExpander : Primitive
  { public initialExpander() : base("initial-expander", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure expander = Ops.ExpectProcedure(args[1]);

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

  #region last
  public sealed class last : Primitive
  { public last() : base("last", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      if(args[0]==null) return null;
      Pair pair = Ops.ExpectPair(args[0]);
      while(true)
      { Pair next = pair.Cdr as Pair;
        if(next==null) return pair;
        pair = next;
      }
    }
  }
  #endregion

  #region length
  public sealed class length : Primitive
  { public length() : base("length", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0]==null ? 0 : Ops.Length(Ops.ExpectPair(args[0]));
    }
  }
  #endregion

  #region list-copy
  public sealed class listCopy : Primitive
  { public listCopy() : base("list-copy", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args[0]==null) return null;
      Pair list=Ops.ExpectPair(args[0]), head=new Pair(list.Car, list.Cdr), tail=head;
      while(true)
      { list = list.Cdr as Pair;
        if(list==null) return head;
        Pair next = new Pair(list.Car, list.Cdr);
        tail.Cdr = next;
        tail = next;
      }
    }
  }
  #endregion

  #region list->string
  public sealed class listToString : Primitive
  { public listToString() : base("list->string", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args[0]==null) return "";

      Pair pair = Ops.ExpectPair(args[0]);
      System.Text.StringBuilder sb = new System.Text.StringBuilder();

      try { while(pair!=null) { sb.Append((char)pair.Car); pair=pair.Cdr as Pair; } }
      catch(InvalidCastException) { throw new Exception(name+": expects a list of characters"); }
      return sb.ToString();
    }
  }
  #endregion

  #region make-string
  public sealed class makeString : Primitive
  { public makeString() : base("make-string", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return new string(args.Length==2 ? Ops.ExpectChar(args[1]) : '\0', Ops.ExpectInt(args[0]));
    }
  }
  #endregion

  public static Pair map(IProcedure func, params Pair[] pairs)
  { Pair head=null, tail=null;
    object[] args = new object[pairs.Length];

    while(true)
    { for(int i=0; i<pairs.Length; i++)
      { if(pairs[i]==null) return head;
        args[i] = pairs[i].Car;
        pairs[i] = pairs[i].Cdr as Pair;
      }
      Pair next = new Pair(func.Call(args), null);
      if(head==null) head=tail=next;
      else { tail.Cdr=next; tail=next; }
    }
  }

  #region not
  public sealed class not : Primitive
  { public not() : base("not", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return !Ops.IsTrue(args[0]) ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region null?
  public sealed class nullP : Primitive
  { public nullP() : base("null?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0]==null ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region or
  public sealed class or : Primitive
  { public or() : base("or", 0, -1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length; i++) if(Ops.IsTrue(args[i])) return args[i];
      return Ops.FALSE;
    }
  }
  #endregion

  #region pair?
  public sealed class pairP : Primitive
  { public pairP() : base("pair?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is Pair ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region primitive-procedure?
  public sealed class primitiveProcedureP : Primitive
  { public primitiveProcedureP() : base("primitive-procedure?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      object func = args[0];
      return Ops.FromBool(!(func is Closure) && func is IProcedure);
    }
  }
  #endregion

  #region procedure?
  public sealed class procedureP : Primitive
  { public procedureP() : base("procedure?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.FromBool(args[0] is IProcedure);
    }
  }
  #endregion

  #region procedure-arity-valid?
  public sealed class procedureArityValidP : Primitive
  { public procedureArityValidP() : base("procedure-arity-valid?", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure proc = Ops.ExpectProcedure(args[0]);
      int min=proc.MinArgs, max=proc.MaxArgs;
      return Ops.FromBool(max==-1 ? args.Length>=min : args.Length>=min && args.Length<=max);
    }
  }
  #endregion

  #region procedure-arity
  public sealed class procedureArity : Primitive
  { public procedureArity() : base("procedure-arity", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure proc = Ops.ExpectProcedure(args[0]);
      int max = proc.MaxArgs;
      return new Pair(proc.MinArgs, max==-1 ? Ops.FALSE : max);
    }
  }
  #endregion

  #region set-car!
  public sealed class setCarN : Primitive
  { public setCarN() : base("set-car!", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      Ops.ExpectPair(args[0]).Car = args[1];
      return null;
    }
  }
  #endregion

  #region set-cdr!
  public sealed class setCdrN : Primitive
  { public setCdrN() : base("set-cdr!", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      Ops.ExpectPair(args[0]).Cdr = args[1];
      return null;
    }
  }
  #endregion

  #region string
  public sealed class @string : Primitive
  { public @string() : base("string", 0, -1) { }

    public override object Call(object[] args)
    { char[] chars = new char[args.Length];
      try { for(int i=0; i<args.Length; i++) chars[i] = (char)args[i]; }
      catch(InvalidCastException) { throw new Exception(name+": expects character arguments"); }
      return new string(chars);
    }
  }
  #endregion

  #region string?
  public sealed class stringP : Primitive
  { public stringP() : base("string?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is string ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region string-length
  public sealed class stringLength : Primitive
  { public stringLength() : base("string-length", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).Length;
    }
  }
  #endregion

  #region string-null?
  public sealed class stringNullP : Primitive
  { public stringNullP() : base("string-null?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.FromBool(Ops.ExpectString(args[0]).Length==0);
    }
  }
  #endregion

  #region string-ref
  public sealed class stringRef : Primitive
  { public stringRef() : base("string-ref", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0])[Ops.ExpectInt(args[1])];
    }
  }
  #endregion
  
  // TODO: string-set!
  // TODO: more string functions: http://www.swiss.ai.mit.edu/projects/scheme/documentation/scheme_7.html#SEC73
  // TODO: string-builder methods in a module
  // TODO: regexp methods in a module

  #region tree-copy
  public sealed class treeCopy : Primitive
  { public treeCopy() : base("tree-copy", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args[0]==null) return null;
      return copy(Ops.ExpectPair(args[0]));
    }

    static object copy(object obj) // TODO: optimize this
    { Pair pair = obj as Pair;
      if(pair==null) return obj;
      return new Pair(copy(pair.Car), copy(pair.Cdr));
    }
  }
  #endregion

  #region values
  public sealed class values : Primitive
  { public values() : base("values", 1, -1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return new MultipleValues(args);
    }
  }
  #endregion

  #region _ixLambda
  sealed class _ixLambda : IProcedure
  { public _ixLambda(IProcedure expander) { this.expander=expander; }
    
    public int MinArgs { get { return 1; } }
    public int MaxArgs { get { return 1; } }

    public object Call(params object[] args)
    { if(args.Length!=1) throw new Exception(); // FIXME: ex
      return Ops.Call(expander, args[0], expander);
    }

    IProcedure expander;
  }
  #endregion
}

} // namespace NetLisp.Backend