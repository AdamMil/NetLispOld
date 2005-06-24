using System;
using System.Collections;
using System.Reflection;

namespace NetLisp.Backend
{

public sealed class Builtins
{ public static void Bind(TopLevel top)
  { foreach(MethodInfo mi in typeof(Builtins).GetMethods())
    { object[] attrs = mi.GetCustomAttributes(typeof(SymbolNameAttribute), false);
      string name = attrs.Length==0 ? mi.Name : ((SymbolNameAttribute)attrs[0]).Name;
      top.Bind(name, Interop.MakeFunctionWrapper(mi, true));
    }

    foreach(Type type in typeof(Builtins).GetNestedTypes(BindingFlags.Public))
      if(type.IsSubclassOf(typeof(Primitive)))
      { Primitive prim = (Primitive)type.GetConstructor(Type.EmptyTypes).Invoke(null);
        top.Bind(prim.Name, prim);
      }
  }

public static void import(params object[] args)
{ foreach(object o in args)
  { if(o is string) Interop.Import((string)o);
    else if(o is Pair)
    { string ns = (string)Ops.FastCadr((Pair)o);
      foreach(string name in Ops.ListToArray((Pair)Ops.FastCddr((Pair)o)))
        Interop.Import(ns+"."+name);
    }
  }
}

[SymbolName("load-assembly-by-name")]
public static void loadByName(string name) { Interop.LoadAssemblyByName(name); }
[SymbolName("load-assembly-from-file")]
public static void loadFromFile(string name) { Interop.LoadAssemblyFromFile(name); }
public static void print(object obj) { Console.Write(Ops.Repr(obj)); }
public static void println(object obj) { Console.WriteLine(Ops.Repr(obj)); }

  #region Numeric operators
  #region +
  public sealed class opadd : Primitive
  { public opadd() : base("+", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Add(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Add(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region -
  public sealed class opsub : Primitive
  { public opsub() : base("-", 1, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==1) return Ops.Negate(args[0]);
      object ret = Ops.Subtract(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Subtract(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region *
  public sealed class opmul : Primitive
  { public opmul() : base("*", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Multiply(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Multiply(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region /
  public sealed class opdiv : Primitive
  { public opdiv() : base("/", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Divide(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Divide(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region //
  public sealed class opfdiv : Primitive
  { public opfdiv() : base("//", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.FloorDivide(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.FloorDivide(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region %
  public sealed class opmod : Primitive
  { public opmod() : base("%", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Modulus(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Modulus(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region expt
  public sealed class expt : Primitive
  { public expt() : base("expt", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.Power(args[0], args[1]);
    }
  }
  #endregion
  #region exptmod
  public sealed class exptmod : Primitive
  { public exptmod() : base("exptmod", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.PowerMod(args[0], args[1], args[2]);
    }
  }
  #endregion

  #region =
  public sealed class opeq : Primitive
  { public opeq() : base("=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])!=0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region !=
  public sealed class opne : Primitive
  { public opne() : base("!=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])==0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region <
  public sealed class oplt : Primitive
  { public oplt() : base("<", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])>=0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region <=
  public sealed class ople : Primitive
  { public ople() : base("<=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])>0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region >
  public sealed class opgt : Primitive
  { public opgt() : base(">", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])<=0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region >=
  public sealed class opge : Primitive
  { public opge() : base(">=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])<0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion

  #region bitand
  public sealed class bitand : Primitive
  { public bitand() : base("bitand", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.BitwiseAnd(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.BitwiseAnd(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region bitor
  public sealed class bitor : Primitive
  { public bitor() : base("bitor", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.BitwiseOr(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.BitwiseOr(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region bitxor
  public sealed class bitxor : Primitive
  { public bitxor() : base("bitxor", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.BitwiseXor(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.BitwiseXor(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region bitnot
  public sealed class bitnot : Primitive
  { public bitnot() : base("bitnot", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.BitwiseNegate(args[0]);
    }
  }
  #endregion
  #region lshift
  public sealed class lshift : Primitive
  { public lshift() : base("lshift", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.LeftShift(args[0], args[1]);
    }
  }
  #endregion
  #region rshift
  public sealed class rshift : Primitive
  { public rshift() : base("rshift", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.RightShift(args[0], args[1]);
    }
  }
  #endregion
  #endregion

  #region abs
  public sealed class abs : Primitive
  { public abs() : base("abs", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: return obj;
        case TypeCode.Decimal: return Math.Abs((Decimal)obj);
        case TypeCode.Double: return Math.Abs((double)obj);
        case TypeCode.Int16: return Math.Abs((short)obj);
        case TypeCode.Int32: return Math.Abs((int)obj);
        case TypeCode.Int64: return Math.Abs((long)obj);
        case TypeCode.SByte: return Math.Abs((sbyte)obj);
        case TypeCode.Single: return Math.Abs((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).Abs;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Abs(c.real);
          }
          goto default;
        default: throw Ops.TypeError(Name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region acos
  public sealed class acos : Primitive
  { public acos() : base("acos", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Acos((Complex)obj) : (object)Math.Acos(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region angle
  public sealed class angle : Primitive
  { public angle() : base("angle", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).Angle;
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
  { public append() : base("append", 0, -1) { }

    public override object Call(object[] args)
    { if(args.Length==0) return null;
      if(args.Length==1) return args[0];

      Pair head=null, prev=null;
      int i;
      for(i=0; i<args.Length-1; i++)
      { if(args[i]==null) continue;
        Pair pair=Ops.ExpectPair(args[i]), tail=new Pair(pair.Car, pair.Cdr);
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
      if(prev==null) return args[i];
      prev.Cdr = args[i];
      return head;
    }
  }
  #endregion

  #region asin
  public sealed class asin : Primitive
  { public asin() : base("asin", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Asin((Complex)obj) : (object)Math.Asin(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region atan
  public sealed class atan : Primitive
  { public atan() : base("atan", 1, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==2) return Math.Atan2(Ops.ToFloat(args[0]), Ops.ToFloat(args[1]));

      object obj = args[0];
      return obj is Complex ? Complex.Atan((Complex)obj) : (object)Math.Atan(Ops.ToFloat(obj));
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

  #region ceiling
  public sealed class ceiling : Primitive
  { public ceiling() : base("ceiling", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal:
        { Decimal d=(Decimal)obj, t=Decimal.Truncate(d);
          return d==t ? obj : t+Decimal.One;
        }
        case TypeCode.Double: return Math.Ceiling((double)obj);
        case TypeCode.Single: return Math.Ceiling((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Ceiling(c.real);
          }
          goto default;
        default: throw Ops.TypeError(Name+": expected a real number, but received "+Ops.TypeName(obj));
      }
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

  #region char->name
  public sealed class charToName : Primitive
  { public charToName() : base("char->name", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return core(Ops.ExpectChar(args[0]), args.Length==2 && Ops.IsTrue(args[1]));
    }
    
    internal static string core(char c, bool slashify)
    { string name;
      switch((int)c)
      { case 0:  name="nul"; break;
        case 7:  name="bel"; break;
        case 8:  name="bs"; break;
        case 9:  name="tab"; break;
        case 10: name="lf"; break;
        case 11: name="vt"; break;
        case 12: name="ff"; break;
        case 13: name="cr"; break;
        case 27: name="esc"; break;
        case 28: name="fs"; break;
        case 29: name="gs"; break;
        case 30: name="rs"; break;
        case 31: name="us"; break;
        case 32: name="space"; break;
        default: name = c>32 ? c==127 ? "del" : c.ToString() : "C-"+((char)(c+96)).ToString(); break;
      }
      return slashify ? "#\\"+name : name;
    }
  }
  #endregion

  public static Snippet compile(object obj) { return Ops.CompileRaw(Ops.Call("expand", obj)); }

  [SymbolName("compiled-procedure?")]
  public static object compiledProcedureP(object obj) { return Ops.FromBool(obj is IProcedure); }

  #region complex?
  public sealed class complexP : Primitive
  { public complexP() : base("complex?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      switch(Convert.GetTypeCode(args[0]))
      { case TypeCode.Byte:   case TypeCode.Decimal: case TypeCode.Double:
        case TypeCode.Int16:  case TypeCode.Int32:   case TypeCode.Int64:
        case TypeCode.SByte:  case TypeCode.Single:
        case TypeCode.UInt16: case TypeCode.UInt32:  case TypeCode.UInt64:
          return Ops.TRUE;
        case TypeCode.Object:
          return args[0] is Integer || args[0] is Complex ? Ops.TRUE : Ops.FALSE;
        default: return Ops.FALSE;
      }
    }
  }
  #endregion

  [SymbolName("compound-procedure?")]
  public static object compoundProcedureP(object obj) { return Ops.FALSE; }

  #region conjugate
  public sealed class conjugate : Primitive
  { public conjugate() : base("conjugate", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).Conjugate;
    }
  }
  #endregion

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

  #region cos
  public sealed class cos : Primitive
  { public cos() : base("cos", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Cos(Ops.ToFloat(args[0]));
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
      return Ops.EqvP(args[0], args[1]) ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion
  
  #region equal?
  public sealed class equalP : Primitive
  { public equalP() : base("equal?", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.EqualP(args[0], args[1]) ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  public static object eval(object obj)
  { Snippet snip = obj as Snippet;
    if(snip==null) snip = compile(obj);
    return snip.Run(null);
  }

  #region even?
  public sealed class evenP : Primitive
  { public evenP() : base("even?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);

      object obj = args[0];
      int iv;

      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:    iv = (byte)obj; goto isint;
        case TypeCode.SByte:   iv = (sbyte)obj; goto isint;
        case TypeCode.Int16:   iv = (short)obj; goto isint;
        case TypeCode.Int32:   iv = (int)obj; goto isint;
        case TypeCode.Int64:   iv = (int)(long)obj; goto isint;
        case TypeCode.UInt16:  iv = (ushort)obj; goto isint;
        case TypeCode.UInt32:  iv = (int)(uint)obj; goto isint;
        case TypeCode.UInt64:  iv = (int)(ulong)obj; goto isint;
        case TypeCode.Decimal: iv = (int)Decimal.ToDouble((Decimal)obj); goto isint;
        case TypeCode.Double:  iv = (int)(double)obj; goto isint;
        case TypeCode.Single:  iv = (int)(float)obj; goto isint;
        case TypeCode.Object:
          if(obj is Integer)
          { Integer i = (Integer)obj;
            return i.length!=0 && (i.data[0]&1)==0;
          }
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) { iv=(int)c.real; goto isint; }
          }
          goto default;
        default: throw Ops.TypeError(Name+": expected a real number, but received "+Ops.TypeName(obj));
      }
      
      isint: return (iv&1)==0 && iv!=0 ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region exact?
  public sealed class exactP : Primitive
  { public exactP() : base("exact?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return Ops.TRUE;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return Ops.FALSE;
        case TypeCode.Object:
          if(obj is Integer) return Ops.TRUE;
          if(obj is Complex) return Ops.FALSE;
          goto default;
        default: throw Ops.TypeError(Name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region exact-integer?
  public sealed class exactIntegerP : Primitive
  { public exactIntegerP() : base("exact-integer?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return Ops.TRUE;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return Ops.FALSE;
        case TypeCode.Object:
          if(obj is Integer) return Ops.TRUE;
          if(obj is Complex) return Ops.FALSE;
          goto default;
        default: throw Ops.TypeError(Name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region exact->inexact
  public sealed class exactToInexact : Primitive
  { public exactToInexact() : base("exact->inexact", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: return (double)(byte)obj;
        case TypeCode.SByte: return (double)(sbyte)obj;
        case TypeCode.Int16: return (double)(short)obj;
        case TypeCode.Int32: return (double)(int)obj;
        case TypeCode.Int64: return (double)(long)obj;
        case TypeCode.UInt16: return (double)(ushort)obj;
        case TypeCode.UInt32:  return (double)(uint)obj;
        case TypeCode.UInt64: return (double)(ulong)obj;

        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return obj;

        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).ToDouble();
          goto default;
        default: throw Ops.TypeError(Name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region exp
  public sealed class exp : Primitive
  { public exp() : base("exp", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Exp(Ops.ToFloat(args[0]));
    }
  }
  #endregion

  public static object expand(object form) { return form; }

  [SymbolName("expander?")]
  public static bool expanderP(object obj)
  { Symbol sym = obj as Symbol;
    return sym!=null && TopLevel.Current.Macros.Contains(sym.Name);
  }

  [SymbolName("expander-function")]
  public static IProcedure expanderFunction(Symbol sym)
  { return (IProcedure)TopLevel.Current.Macros[sym.Name];
  }

  #region floor
  public sealed class floor : Primitive
  { public floor() : base("floor", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal: return Decimal.Floor((Decimal)obj);
        case TypeCode.Double: return Math.Floor((double)obj);
        case TypeCode.Single: return Math.Floor((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Floor(c.real);
          }
          goto default;
        default: throw Ops.TypeError(Name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region gensym
  public sealed class gensym : Primitive
  { public gensym() : base("gensym", 0, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return new Symbol((args.Length==0 ? "#<g" : "#<"+Ops.ExpectString(args[0])) + gensyms.Next + ">");
    }
  }
  #endregion

  #region imag-part
  public sealed class imagPart : Primitive
  { public imagPart() : base("imag-part", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).imag;
    }
  }
  #endregion

  #region inexact?
  public sealed class inexactP : Primitive
  { public inexactP() : base("inexact?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return Ops.FALSE;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return Ops.TRUE;
        case TypeCode.Object:
          if(obj is Integer) return Ops.FALSE;
          if(obj is Complex) return Ops.TRUE;
          goto default;
        default: throw Ops.TypeError(Name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region inexact->exact
  public sealed class inexactToExact : Primitive
  { public inexactToExact() : base("inexact->exact", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return core(args[0]);
    }
    
    internal static object core(object obj)
    { switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return obj;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          throw new NotImplementedException("rationals");
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex) throw new NotImplementedException("rationals");
          goto default;
        default: throw Ops.TypeError("inexact->exact"+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  [SymbolName("install-expander")]
  public static object installExpander(Symbol sym, Closure func)
  { TopLevel.Current.Macros[sym.Name] = func;
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

  #region log
  public sealed class log : Primitive
  { public log() : base("log", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Log((Complex)obj) : (object)Math.Log(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region log10
  public sealed class log10 : Primitive
  { public log10() : base("log10", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Log10((Complex)obj) : (object)Math.Log10(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region magnitude
  public sealed class magnitude : Primitive
  { public magnitude() : base("magnitude", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: return obj;
        case TypeCode.Decimal: return Math.Abs((Decimal)obj);
        case TypeCode.Double: return Math.Abs((double)obj);
        case TypeCode.Int16: return Math.Abs((short)obj);
        case TypeCode.Int32: return Math.Abs((int)obj);
        case TypeCode.Int64: return Math.Abs((long)obj);
        case TypeCode.SByte: return Math.Abs((sbyte)obj);
        case TypeCode.Single: return Math.Abs((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).Abs;
          if(obj is Complex) return ((Complex)obj).Magnitude;
          goto default;
        default: throw Ops.TypeError(Name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region make-polar
  public sealed class makePolar : Primitive
  { public makePolar() : base("make-polar", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      double phase = Ops.ToFloat(args[1]);
      return new Complex(Math.Cos(phase), Math.Sin(phase)) * Ops.ToFloat(args[0]);
    }
  }
  #endregion

  #region make-rectangular
  public sealed class makeRectangular : Primitive
  { public makeRectangular() : base("make-rectangular", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return new Complex(Ops.ToFloat(args[0]), Ops.ToFloat(args[1]));
    }
  }
  #endregion

  #region make-ref
  public sealed class makeRef : Primitive
  { public makeRef() : base("make-ref", 0, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return new Reference(args.Length==0 ? null : args[0]);
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

  #region name->char
  public sealed class nameToChar : Primitive
  { public nameToChar() : base("name->char", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return core(Ops.ExpectString(args[0]));
    }

    internal static char core(string name)
    { char c;
      if(name.Length==0) throw Ops.ValueError("name->char: expected non-empty string");
      else if(name.Length==1) c=name[0];
      else if(name.StartsWith("c-") && name.Length==3)
      { int i = char.ToUpper(name[2])-64;
        if(i<1 || i>26) throw Ops.ValueError("name->char: invalid control code "+name);
        c=(char)i;
      }
      else
        switch(name.ToLower())
        { case "space": c=(char)32; break;
          case "lf": case "linefeed": c=(char)10; break;
          case "cr": case "return": c=(char)13; break;
          case "tab": case "ht": c=(char)9; break;
          case "bs": case "backspace": c=(char)8; break;
          case "esc": case "altmode": c=(char)27; break;
          case "del": case "rubout": c=(char)127; break;
          case "nul": c=(char)0; break;
          case "soh": c=(char)1; break;
          case "stx": c=(char)2; break;
          case "etx": c=(char)3; break;
          case "eot": c=(char)4; break;
          case "enq": c=(char)5; break;
          case "ack": c=(char)6; break;
          case "bel": c=(char)7; break;
          case "vt":  c=(char)11; break;
          case "ff": case "page": c=(char)12; break;
          case "so":  c=(char)14; break;
          case "si":  c=(char)15; break;
          case "dle": c=(char)16; break;
          case "dc1": c=(char)17; break;
          case "dc2": c=(char)18; break;
          case "dc3": c=(char)19; break;
          case "dc4": c=(char)20; break;
          case "nak": c=(char)21; break;
          case "syn": c=(char)22; break;
          case "etb": c=(char)23; break;
          case "can": c=(char)24; break;
          case "em":  c=(char)25; break;
          case "sub": case "call": c=(char)26; break;
          case "fs":  c=(char)28; break;
          case "gs":  c=(char)29; break;
          case "rs":  c=(char)30; break;
          case "us": case "backnext": c=(char)31; break;
          default: throw Ops.ValueError("name->char: unknown character name '"+name+"'");
        }

      return c;
    }
  }
  #endregion

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

  // TODO: number->string

  #region odd?
  public sealed class oddP : Primitive
  { public oddP() : base("odd?", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);

      object obj = args[0];
      int iv;

      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:    iv = (byte)obj; goto isint;
        case TypeCode.SByte:   iv = (sbyte)obj; goto isint;
        case TypeCode.Int16:   iv = (short)obj; goto isint;
        case TypeCode.Int32:   iv = (int)obj; goto isint;
        case TypeCode.Int64:   iv = (int)(long)obj; goto isint;
        case TypeCode.UInt16:  iv = (ushort)obj; goto isint;
        case TypeCode.UInt32:  iv = (int)(uint)obj; goto isint;
        case TypeCode.UInt64:  iv = (int)(ulong)obj; goto isint;
        case TypeCode.Decimal: iv = (int)Decimal.ToDouble((Decimal)obj); goto isint;
        case TypeCode.Double:  iv = (int)(double)obj; goto isint;
        case TypeCode.Single:  iv = (int)(float)obj; goto isint;
        case TypeCode.Object:
          if(obj is Integer)
          { Integer i = (Integer)obj;
            return i.length!=0 && (i.data[0]&1)==0;
          }
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) { iv=(int)c.real; goto isint; }
          }
          goto default;
        default: throw Ops.TypeError(Name+": expected a real number, but received "+Ops.TypeName(obj));
      }
      
      isint: return (iv&1)!=0 ? Ops.TRUE : Ops.FALSE;
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

  #region real-part
  public sealed class realPart : Primitive
  { public realPart() : base("real-part", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).real;
    }
  }
  #endregion

  #region ref-get
  public sealed class refGet : Primitive
  { public refGet() : base("ref-get", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectRef(args[0]).Value;
    }
  }
  #endregion

  #region ref-set!
  public sealed class refSetN : Primitive
  { public refSetN() : base("ref-set!", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectRef(args[0]).Value=args[1];
    }
  }
  #endregion

  #region round
  public sealed class round : Primitive
  { public round() : base("round", 1, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);

      object obj = args[0];
      int places = args.Length==2 ? Ops.ToInt(args[1]) : 0;
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal: return Decimal.Round((Decimal)obj, places);
        case TypeCode.Double:  return Math.Round((double)obj, places);
        case TypeCode.Single:  return Math.Round((float)obj, places);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Round(c.real, places);
          }
          goto default;
        default: throw Ops.TypeError(Name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }

    static object doubleCore(double d)
    { try { return checked((int)d); }
      catch(OverflowException)
      { try { return checked((long)d); }
        catch(OverflowException) { return new Integer(d); }
      }
    }
  }
  #endregion

  #region set-car!
  public sealed class setCarN : Primitive
  { public setCarN() : base("set-car!", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Car = args[1];
    }
  }
  #endregion

  #region set-cdr!
  public sealed class setCdrN : Primitive
  { public setCdrN() : base("set-cdr!", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Cdr = args[1];
    }
  }
  #endregion

  #region sin
  public sealed class sin : Primitive
  { public sin() : base("sin", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Sin(Ops.ToFloat(args[0]));
    }
  }
  #endregion

  #region sqrt
  public sealed class sqrt : Primitive
  { public sqrt() : base("sqrt", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Sqrt((Complex)obj) : (object)Math.Sqrt(Ops.ToFloat(obj));
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

  // TODO: string->number

  #region symbol?
  public sealed class symbolP : Primitive
  { public symbolP() : base("symbol?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is Symbol ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  // TODO: string-set!
  // TODO: more string functions: http://www.swiss.ai.mit.edu/projects/scheme/documentation/scheme_7.html#SEC73
  // TODO: string-builder methods in a module
  // TODO: regexp methods in a module

  #region tan
  public sealed class tan : Primitive
  { public tan() : base("tan", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Tan(Ops.ToFloat(args[0]));
    }
  }
  #endregion

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

  #region truncate
  public sealed class truncate : Primitive
  { public truncate() : base("truncate", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal: return Decimal.Truncate((Decimal)obj);
        case TypeCode.Double:  return doubleCore((double)obj);
        case TypeCode.Single:  return doubleCore((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return doubleCore(c.real);
          }
          goto default;
        default: throw Ops.TypeError(Name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
    
    static object doubleCore(double d)
    { try { return checked((int)d); }
      catch(OverflowException)
      { try { return checked((long)d); }
        catch(OverflowException) { return new Integer(d); }
      }
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

  static Index gensyms = new Index();
}

} // namespace NetLisp.Backend