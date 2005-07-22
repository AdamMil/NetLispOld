using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

// TODO: add exception handling

namespace NetLisp.Backend
{

#region Index, Name, Options, Singleton, IWalker
public sealed class Index
{ public long Next { get { lock(this) return index++; } }
  long index;
}

public sealed class Name
{ public Name(string name) { String=name; Depth=Local; Index=-1; }
  public Name(string name, int depth) { String=name; Depth=depth; Index=-1; }
  public Name(string name, int depth, int index) { String=name; Depth=depth; Index=index; }

  public const int Global=-2, Local=-1;

  public override bool Equals(object obj)
  { Name o = obj as Name;
    return o!=null && o.String==String && o.Depth==Depth && o.Index==Index;
  }

  public override int GetHashCode() { return String.GetHashCode() ^ Depth ^ Index; }

  public string String;
  public int Depth, Index;
}

public sealed class Options
{ private Options() { }
  public static bool Debug, Optimize;
}

public sealed class Singleton
{ public Singleton(string name) { Name=name; }
  public override string ToString() { return "Singleton: "+Name; }
  public string Name;
}

public interface IWalker
{ void PostWalk(Node node);
  bool Walk(Node node);
}
#endregion

#region AST
public sealed class AST
{ public static LambdaNode Create(object obj) { return (LambdaNode)Create(obj, false); }
  public static Node Create(object obj, bool interpreted)
  { Node body = Parse(obj);
    if(!interpreted)
    { // wrapping it in a lambda node is done so we can keep the preprocessing code simple, and so that we can support
      // top-level closures. it's unwrapped later on by SnippetMaker.Generate()
      body = new LambdaNode(new string[0], false, body);
      body.Preprocess();
    }
    return body;
  }

  static Node Parse(object obj)
  { Symbol sym = obj as Symbol;
    if(sym!=null) return new VariableNode(sym.Name);

    Pair pair = obj as Pair;
    if(pair==null) return new LiteralNode(obj);

    sym = pair.Car as Symbol;
    if(sym!=null)
      switch(sym.Name)
      { case "if":
        { int len = Builtins.length.core(pair);
          if(len<3 || len>4) throw new Exception("if: expects 3 or 4 forms"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          Pair next = (Pair)pair.Cdr;
          return new IfNode(Parse(pair.Car), Parse(next.Car), next.Cdr==null ? null : Parse(Ops.FastCadr(next)));
        }
        case "begin":
        { if(Builtins.length.core(pair)<2) throw new Exception("begin: no forms given");
          return ParseBody((Pair)pair.Cdr);
        }
        case "let":
        { if(Builtins.length.core(pair)<3) throw new Exception("too few for let");
          pair = (Pair)pair.Cdr;

          Pair bindings = (Pair)pair.Car;
          string[] names = new string[Builtins.length.core(bindings)];
          Node[]   inits = new Node[names.Length];
          for(int i=0; i<names.Length; bindings=(Pair)bindings.Cdr,i++)
          { if(bindings.Car is Pair)
            { Pair binding = (Pair)bindings.Car;
              names[i] = ((Symbol)binding.Car).Name;
              inits[i] = Parse(Ops.FastCadr(binding));
            }
            else names[i] = ((Symbol)bindings.Car).Name;
          }

          return new LetNode(names, inits, ParseBody((Pair)pair.Cdr));
        }
        case "lambda":
        { if(Builtins.length.core(pair)<3) throw new Exception("too few for lambda"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          bool hasList;
          return new LambdaNode(ParseLambaList(pair.Car, out hasList), hasList, ParseBody((Pair)pair.Cdr));
        }
        case "quote":
        { if(Builtins.length.core(pair)!=2) throw new Exception("wrong number for quote"); // FIXME: ex
          return Quote(Ops.FastCadr(pair));
        }
        case "set!":
        { if(Builtins.length.core(pair)!=3) throw new Exception("wrong number for set!"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          sym = pair.Car as Symbol;
          if(sym==null) throw new Exception("set must set symbol"); // FIXME: SyntaxException
          return new SetNode(sym.Name, Parse(Ops.FastCadr(pair)));
        }
        case "define":
        { int length = Builtins.length.core(pair);
          if(length!=3) throw new Exception("wrong number for define"); // FIXME: ex
          pair = (Pair)pair.Cdr;
          sym = (Symbol)pair.Car;
          return new DefineNode(sym.Name, Parse(Ops.FastCadr(pair))); // (define name value)
        }
        case "vector":
        { ArrayList items = new ArrayList();
          while(true)
          { pair = pair.Cdr as Pair;
            if(pair==null) break;
            items.Add(Parse(pair.Car));
          }
          return new VectorNode((Node[])items.ToArray(typeof(Node)));
        }
        // (module name-symbol (provides ...) body...)
        case "#%module":
        { if(Builtins.length.core(pair)<4) goto moduleError;
          pair = (Pair)pair.Cdr;
          string name=((Symbol)pair.Car).Name;
          pair = (Pair)pair.Cdr;
          CallNode provide=(CallNode)Parse(pair.Car);
          pair = (Pair)pair.Cdr;
          return new ModuleNode(name, provide, ParseBody(pair));
          moduleError: throw new Exception("module definition should be of this form: "+
                                           "(module name-symbol (provide ...) body ...)");
        }
      }

    Node func = Parse(pair.Car);
    ArrayList args = new ArrayList();
    while(true)
    { pair = pair.Cdr as Pair;
      if(pair==null) break;
      args.Add(Parse(pair.Car));
    }
    if(Options.Optimize && func is LambdaNode) // optimization: transform ((lambda (a) ...) x) into (let ((a x)) ...)
    { LambdaNode fl = (LambdaNode)func;
      string[] names = new string[fl.Parameters.Length];
      for(int i=0; i<names.Length; i++) names[i] = fl.Parameters[i].String;
      int positional = fl.Parameters.Length-(fl.HasList ? 1 : 0);
      Ops.CheckArity("<unnamed lambda>", args.Count, positional, fl.HasList ? -1 : positional);

      Node[] inits = new Node[names.Length];
      for(int i=0; i<positional; i++) inits[i] = (Node)args[i];
      if(fl.HasList)
      { if(args.Count==positional) inits[positional] = new LiteralNode(null);
        else
        { Node[] elems = new Node[args.Count-positional];
          for(int i=positional; i<args.Count; i++) elems[i-positional] = (Node)args[i];
          inits[positional] = new ListNode(elems, null);
        }
      }
      return new LetNode(names, inits, fl.Body);
    }
    else return new CallNode(func, (Node[])args.ToArray(typeof(Node)));
  }

  static BodyNode ParseBody(Pair start)
  { if(start==null) return null;
    ArrayList items = new ArrayList();
    while(start!=null)
    { items.Add(Parse(start.Car));
      start = start.Cdr as Pair;
    }
    return new BodyNode((Node[])items.ToArray(typeof(Node)));
  }

  static string[] ParseLambaList(object obj, out bool hasList)
  { hasList = false;
    if(obj is Symbol) { hasList=true; return new string[] { ((Symbol)obj).Name }; }

    Pair list = (Pair)obj;
    ArrayList names = new ArrayList();
    while(list!=null)
    { Symbol sym = list.Car as Symbol;
      if(sym==null) throw new Exception("lambda list must contain symbols"); // FIXME: SyntaxException
      names.Add(sym.Name);
      object next = list.Cdr;
      list = next as Pair;
      if(list==null && next!=null)
      { sym = next as Symbol;
        if(sym==null) throw new Exception("lambda list must contain symbols"); // FIXME: SyntaxException
        names.Add(sym.Name);
        hasList=true;
        break;
      }
    }
    return (string[])names.ToArray(typeof(string));
  }

  static Node Quote(object obj)
  { Pair pair = obj as Pair;
    if(pair==null) return new LiteralNode(obj);
    
    ArrayList items = new ArrayList();
    Node dot=null;
    while(true)
    { items.Add(Quote(pair.Car));
      object next = pair.Cdr;
      pair = next as Pair;
      if(pair==null)
      { if(next!=null) dot = Quote(next);
        break;
      }
    }
    return new ListNode((Node[])items.ToArray(typeof(Node)), dot);
  }
}
#endregion

#region Node
public abstract class Node
{ [Flags] public enum Flag : byte { Tail=1, Const=2 };

  public bool IsConstant
  { get { return (Flags&Flag.Const) != 0; }
    set { if(value) Flags|=Flag.Const; else Flags&=~Flag.Const; }
  }

  public bool Tail
  { get { return (Flags&Flag.Tail) != 0; }
    set { if(value) Flags|=Flag.Tail; else Flags&=~Flag.Tail; }
  }

  public void Emit(CodeGenerator cg)
  { Type type = typeof(object);
    Emit(cg, ref type);
  }
  public abstract void Emit(CodeGenerator cg, ref Type etype);
  public virtual object Evaluate() { throw new NotSupportedException(); }

  public abstract Type GetNodeType();

  public virtual void MarkTail(bool tail) { Tail=tail; }
  public virtual void Optimize() { }
  public virtual void Preprocess()
  { if(Options.Optimize) Walk(new Optimizer());
    MarkTail(true);
  }

  public virtual void Walk(IWalker w)
  { w.Walk(this);
    w.PostWalk(this);
  }

  public LambdaNode InFunc;
  public Flag Flags;
  
  public static bool AreEquivalent(Type type, Type desired)
  { Conversion conv = Ops.ConvertTo(type, desired);
    return conv==Conversion.Identity || conv==Conversion.Reference;
  }

  public static bool Compatible(Type type, Type desired)
  { if((type!=null && type.IsValueType) != desired.IsValueType) return false;
    Conversion conv = Ops.ConvertTo(type, desired);
    return conv!=Conversion.None && conv!=Conversion.Unsafe;
  }

  public static void EmitConstant(CodeGenerator cg, object value, ref Type etype)
  { if(etype==null) cg.ILG.Emit(OpCodes.Ldnull);
    else if(etype==typeof(void)) return;
    else
    { value = TryConvert(value, ref etype);
      if(etype.IsValueType)
      { if(Type.GetTypeCode(etype)!=TypeCode.Object) cg.EmitConstant(value);
        else
        { cg.EmitConstantObject(value);
          cg.ILG.Emit(OpCodes.Unbox, etype);
          cg.EmitIndirectLoad(etype);
        }
      }
      else cg.EmitConstantObject(value);
    }
  }

  public static object MaybeEmitBranch(CodeGenerator cg, Node test, Label label, bool onTrue)
  { OpCode brtrue=onTrue ? OpCodes.Brtrue : OpCodes.Brfalse, brfalse=onTrue ? OpCodes.Brfalse : OpCodes.Brtrue;
    Type type = typeof(bool);
    test.Emit(cg, ref type);

    if(type==typeof(bool)) cg.ILG.Emit(brtrue, label);
    else if(type==typeof(negbool)) cg.ILG.Emit(brfalse, label);
    else if(type==typeof(object))
    { cg.EmitIsTrue();
      cg.ILG.Emit(brtrue, label);
    }
    else
    { cg.ILG.Emit(OpCodes.Pop);
      return type!=null;
    }
    return null;
  }

  public static object TryConvert(object value, ref Type etype)
  { if(etype==null) return null;
    if(etype==typeof(object)) return value;

    Type vtype = value==null ? null : value.GetType();
    if(Compatible(vtype, etype))
    { value = Ops.ConvertTo(value, etype);
      etype = value.GetType();
    }
    else etype = value.GetType();
    return value;
  }

  protected struct negbool { }

  sealed class Optimizer : IWalker
  { public bool Walk(Node node) { return true; }
    public void PostWalk(Node node) { node.Optimize(); }
  }
}
#endregion

#region BodyNode
public sealed class BodyNode : Node
{ public BodyNode(Node[] forms) { Forms=forms; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(Forms.Length==0)
    { if(etype!=typeof(void))
      { cg.ILG.Emit(OpCodes.Ldnull);
        etype = null;
      }
    }
    else
    { int i;
      for(i=0; i<Forms.Length-1; i++)
      { Type type = typeof(void);
        Forms[i].Emit(cg, ref type);
        if(type!=typeof(void)) cg.ILG.Emit(OpCodes.Pop);
      }
      Forms[i].Emit(cg, ref etype);
    }
  }

  public override object Evaluate()
  { object ret=null;
    foreach(Node n in Forms) ret = n.Evaluate();
    return ret;
  }

  public override Type GetNodeType() { return Forms.Length==0 ? null : Forms[Forms.Length-1].GetNodeType(); }

  public override void MarkTail(bool tail)
  { for(int i=0; i<Forms.Length; i++) Forms[i].MarkTail(tail && i==Forms.Length-1);
  }

  public override void Optimize()
  { bool isconst=true;
    for(int i=0; i<Forms.Length; i++) if(!Forms[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) foreach(Node n in Forms) n.Walk(w);
    w.PostWalk(this);
  }

  public readonly Node[] Forms;
}
#endregion

#region CallNode
public sealed class CallNode : Node
{ public CallNode(Node func, params Node[] args) { Function=func; Args=args; }
  public CallNode(string name, params Node[] args) { Function=new VariableNode(name); Args=args; }
  static CallNode()
  { ArrayList cfunc = new ArrayList();

    ops = new Hashtable();
    string[] arr = new string[]
    { "-", "Subtract", "bitnot", "BitwiseNegate", "+", "Add", "*", "Multiply", "/", "Divide", "//", "FloorDivide",
      "%", "Modulus",  "bitand", "BitwiseAnd", "bitor", "BitwiseOr", "bitxor", "BitwiseXor", "=", "AreEqual",
      "!=", "NotEqual", "<", "Less", ">", "More", "<=", "LessEqual", ">=", "MoreEqual", "expt", "Power",
      "lshift", "LeftShift", "rshift", "RightShift", "exptmod", "PowerMod"
    };
    for(int i=0; i<arr.Length; i+=2)
    { ops[arr[i]] = arr[i+1];
      cfunc.Add(arr[i]);
    }
    cfunc.AddRange(new string[] {
      "eq?", "eqv?", "equal?", "null?", "pair?", "char?", "symbol?", "string?", "procedure?", "vector?", "values",
      "not", "string-null?", "string-length", "string-ref", "vector-length", "vector-ref", "car", "cdr",
      "char-upcase", "char-downcase"});

    cfunc.Sort();
    constant = (string[])cfunc.ToArray(typeof(string));
  }

  static Hashtable ops;
  static string[] constant;

  #region Emit
  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(IsConstant)
    { EmitConstant(cg, Evaluate(), ref etype);
      if(Tail) cg.EmitReturn();
      return;
    }

    if(Options.Optimize && Function is VariableNode)
    { VariableNode vn = (VariableNode)Function;
      if(Tail && FuncNameMatch(vn.Name, InFunc)) // see if we can tailcall ourselves with a branch
      { int positional = InFunc.Parameters.Length-(InFunc.HasList ? 1 : 0);
        if(Args.Length<positional)
          throw new Exception(string.Format("{0} expects {1}{2} args, but is being passed {3}",
                                            vn.Name, InFunc.HasList ? "at least " : "", positional, Args.Length));
        for(int i=0; i<positional; i++) Args[i].Emit(cg);
        if(InFunc.HasList) cg.EmitList(Args, positional);
        for(int i=InFunc.Parameters.Length-1; i>=0; i--) cg.EmitSet(InFunc.Parameters[i]);
        cg.ILG.Emit(OpCodes.Br, InFunc.StartLabel);
        etype = typeof(object);
        return;
      }
      else // inline common functions
      { string name=vn.Name.String, opname=(string)ops[name];
        switch(name) // functions with side effects
        { case "set-car!": case "set-cdr!":
            CheckArity(2);
            if(etype==typeof(void))
            { cg.EmitPair(Args[0]);
              Args[1].Emit(cg);
              cg.EmitFieldSet(typeof(Pair), name=="set-car!" ? "Car" : "Cdr");
            }
            else
            { Slot tmp = cg.AllocLocalTemp(typeof(object));
              Args[1].Emit(cg);
              tmp.EmitSet(cg);
              cg.EmitPair(Args[0]);
              tmp.EmitGet(cg);
              cg.EmitFieldSet(typeof(Pair), name=="set-car!" ? "Car" : "Cdr");
              tmp.EmitGet(cg);
              cg.FreeLocalTemp(tmp);
              goto objret;
            }
            goto ret;
        }

        switch(name)
        { case "eq?": case "eqv?": case "equal?":
          { CheckArity(2);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            if(name=="eq?") cg.ILG.Emit(OpCodes.Ceq);
            else cg.EmitCall(typeof(Ops), name=="eqv?" ? "EqvP" : "EqualP");
            if(etype!=typeof(bool))
            { EmitFromBool(cg, true);
              goto objret;
            }
            goto ret;
          }
          case "not":
          { CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Type type = typeof(bool);
            Args[0].Emit(cg, ref type);
            if(etype==typeof(bool))
            { if(type==typeof(bool)) etype=typeof(negbool);
              else
              { if(type!=typeof(negbool)) cg.EmitIsFalse(type);
                etype=typeof(bool);
              }
            }
            else
            { if(type!=typeof(bool) && type!=typeof(negbool)) cg.EmitIsTrue(type);
              EmitFromBool(cg, type==typeof(negbool));
              goto objret;
            }
            goto ret;
          }
          case "null?": case "pair?": case "char?": case "symbol?": case "string?": case "procedure?": case "vector?":
          case "string-null?":
          { CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Type type=null;
            if(name=="string-null?") cg.EmitString(Args[0]);
            else Args[0].Emit(cg);
            switch(name)
            { case "pair?": type=typeof(Pair); break;
              case "char?": type=typeof(char); break;
              case "symbol?": type=typeof(Symbol); break;
              case "string?": type=typeof(string); break;
              case "procedure?": type=typeof(IProcedure); break;
              case "vector?": type=typeof(object[]); break;
              case "not": cg.EmitCall(typeof(Ops), "IsTrue"); break;
              case "string-null?": cg.EmitPropGet(typeof(string), "Length"); break;
            }
            if(etype==typeof(bool))
            { if(type!=null) cg.ILG.Emit(OpCodes.Isinst, type);
              else etype=typeof(Node.negbool);
            }
            else
            { if(type!=null) cg.ILG.Emit(OpCodes.Isinst, type);
              EmitFromBool(cg, type!=null);
              goto objret;
            }
            goto ret;
          }
          case "string-length": case "vector-length":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            if(name=="string-length")
            { cg.EmitString(Args[0]);
              cg.EmitPropGet(typeof(string), "Length");
            }
            else
            { cg.EmitTypedNode(Args[0], typeof(object[]));
              cg.EmitPropGet(typeof(object[]), "Length");
            }
            if(etype!=typeof(int))
            { cg.ILG.Emit(OpCodes.Box, typeof(int));
              goto objret;
            }
            goto ret;
          case "string-ref":
            CheckArity(2);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            cg.EmitString(Args[0]);
            { Type type=typeof(int);
              Args[1].Emit(cg, ref type);
              if(type!=typeof(int)) cg.EmitCall(typeof(Ops), "ToInt");
            }
            cg.EmitPropGet(typeof(string), "Chars");
            if(etype!=typeof(char))
            { cg.ILG.Emit(OpCodes.Box, typeof(char));
              goto objret;
            }
            goto ret;
          case "vector-ref": case "vector-set!":
            if(name=="vector-ref")
            { CheckArity(2);
              if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            }
            else CheckArity(3);
            cg.EmitTypedNode(Args[0], typeof(object[]));
            { Type type=typeof(int);
              Args[1].Emit(cg, ref type);
              if(type!=typeof(int)) cg.EmitCall(typeof(Ops), "ToInt");
            }
            if(name=="vector-ref") cg.ILG.Emit(OpCodes.Ldelem_Ref);
            else
            { Args[2].Emit(cg);
              if(etype==typeof(void)) { cg.ILG.Emit(OpCodes.Stelem_Ref); goto ret; }
              else
              { Slot tmp = cg.AllocLocalTemp(typeof(object));
                cg.ILG.Emit(OpCodes.Dup);
                tmp.EmitSet(cg);
                cg.ILG.Emit(OpCodes.Stelem_Ref);
                tmp.EmitGet(cg);
                cg.FreeLocalTemp(tmp);
              }
            }
            goto objret;
          case "car": case "cdr":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            cg.EmitPair(Args[0]);
            cg.EmitFieldGet(typeof(Pair), name=="car" ? "Car" : "Cdr");
            goto objret;
          case "char-upcase": case "char-downcase":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            cg.EmitTypedNode(Args[0], typeof(char));
            cg.EmitCall(typeof(char), name=="char-upcase" ? "ToUpper" : "ToLower", new Type[] { typeof(char) });
            if(etype!=typeof(char))
            { cg.ILG.Emit(OpCodes.Box, typeof(char));
              goto objret;
            }
            goto ret;
          case "bitnot":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            break;
          case "-":
            if(Args.Length==1)
            { if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
              opname = "Negate";
              Args[0].Emit(cg);
              break;
            }
            else goto plusetc;
          case "+": case "*": case "/": case "//": case "%": case "bitand": case "bitor": case "bitxor": plusetc:
            CheckArity(2, -1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            for(int i=1; i<Args.Length-1; i++)
            { Args[i].Emit(cg);
              cg.EmitCall(typeof(Ops), opname);
            }
            Args[Args.Length-1].Emit(cg);
            break;
          case "=": case "!=": case "<": case ">": case "<=": case ">=":
            CheckArity(2, -1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            if(Args.Length!=2) goto normal; // TODO: do the code generation for more than 2 arguments
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            if(etype==typeof(bool))
            { switch(name)
              { case "=": case "!=":
                  cg.EmitCall(typeof(Ops), "EqvP");
                  if(name=="!=") etype = typeof(negbool);
                  break;
                default:
                  cg.EmitCall(typeof(Ops), "Compare");
                  cg.EmitInt(0);
                  switch(name)
                  { case "<":  cg.ILG.Emit(OpCodes.Clt); break;
                    case "<=": cg.ILG.Emit(OpCodes.Cgt); etype=typeof(negbool); break;
                    case ">":  cg.ILG.Emit(OpCodes.Cgt); break;
                    case ">=": cg.ILG.Emit(OpCodes.Clt); etype=typeof(negbool); break;
                  }
                  break;
              }
              goto ret;
            }
            break;
          case "expt": case "lshift": case "rshift":
            CheckArity(2);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            break;
          case "exptmod":
            CheckArity(3);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            Args[2].Emit(cg);
            break;
          case "values":
            CheckArity(1, -1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            if(Args.Length==1) cg.EmitExpression(Args[0]);
            else
            { cg.EmitObjectArray(Args);
              cg.EmitNew(typeof(MultipleValues), new Type[] { typeof(object[]) });
            }
            goto ret;
          default: goto normal;
        }

        if(Tail) cg.ILG.Emit(OpCodes.Tailcall);
        cg.EmitCall(typeof(Ops), opname);
        objret:
        etype = typeof(object);
        ret:
        if(Tail) cg.EmitReturn();
        return;
      }
    }
    
    normal:
    cg.EmitTypedNode(Function, typeof(IProcedure));
    cg.EmitObjectArray(Args);
    if(Tail) cg.ILG.Emit(OpCodes.Tailcall);
    cg.EmitCall(typeof(IProcedure), "Call");
    etype = typeof(object);
    if(Tail) cg.EmitReturn();
  }
  #endregion
  
  #region Evaluate
  public override object Evaluate()
  { object[] a = new object[Args.Length];
    for(int i=0; i<Args.Length; i++) a[i] = Args[i].Evaluate();

    if(IsConstant)
    { string name = ((VariableNode)Function).Name.String;
      try
      { switch(name)
        { case "+":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Add(ret, a[i]);
            return ret;
          }
          case "-":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Subtract(ret, a[i]);
            return ret;
          }
          case "*":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Multiply(ret, a[i]);
            return ret;
          }
          case "/":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Divide(ret, a[i]);
            return ret;
          }
          case "//":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.FloorDivide(ret, a[i]);
            return ret;
          }
          case "%":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Modulus(ret, a[i]);
            return ret;
          }
          case "expt": CheckArity(2); return Ops.Power(a[0], a[1]);
          case "exptmod": CheckArity(3); return Ops.PowerMod(a[0], a[1], a[2]);
          case "bitnot": CheckArity(1); return Ops.BitwiseNegate(a[0]);
          case "bitand":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.BitwiseAnd(ret, a[i]);
            return ret;
          }
          case "bitor":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.BitwiseOr(ret, a[i]);
            return ret;
          }
          case "bitxor":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.BitwiseXor(ret, a[i]);
            return ret;
          }
          case "lshift": CheckArity(2); return Ops.LeftShift(a[0], a[1]);
          case "rshift": CheckArity(2); return Ops.RightShift(a[0], a[1]);
          case "=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(!Ops.EqvP(a[i], a[i+1])) return Ops.FALSE;
            return Ops.TRUE;
          }
          case "!=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.EqvP(a[i], a[i+1])) return Ops.FALSE;
            return Ops.TRUE;
          }
          case "<":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])>=0) return Ops.FALSE;
            return Ops.TRUE;
          }
          case "<=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])>0) return Ops.FALSE;
            return Ops.TRUE;
          }
          case ">":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])<=0) return Ops.FALSE;
            return Ops.TRUE;
          }
          case ">=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])<0) return Ops.FALSE;
            return Ops.TRUE;
          }
        }
      }
      catch(Exception e) { throw new ArgumentException(name+": "+e.Message); }
      
      switch(name)
      { case "car": CheckArity(1); CheckType(a, 0, typeof(Pair)); return ((Pair)a[0]).Car;
        case "cdr": CheckArity(1); CheckType(a, 0, typeof(Pair)); return ((Pair)a[0]).Cdr;
        case "char?": CheckArity(1); return a[0] is char;
        case "char-downcase": CheckArity(1); CheckType(a, 0, typeof(char)); return char.ToLower((char)a[0]);
        case "char-upcase": CheckArity(1); CheckType(a, 0, typeof(char)); return char.ToUpper((char)a[0]);
        case "eq?": CheckArity(2); return a[0]==a[1];
        case "eqv?": CheckArity(2); return Ops.EqvP(a[0], a[1]);
        case "equal?": CheckArity(2); return Ops.EqualP(a[0], a[1]);
        case "not": CheckArity(1); return !Ops.IsTrue(a[0]);
        case "null?": CheckArity(1); return a[0]==null;
        case "pair?": CheckArity(1); return a[0] is Pair;
        case "procedure?": CheckArity(1); return a[0] is IProcedure;
        case "string?": CheckArity(1); return a[0] is string;
        case "string-length":
          CheckArity(1); CheckType(a, 0, typeof(string));
          return ((string)a[0]).Length;
        case "string-null?": CheckArity(1); return a[0] is string && (string)a[0]=="";
        case "string-ref":
          CheckArity(2); CheckType(a, 0, typeof(string)); CheckType(a, 1, typeof(int));
          return ((string)a[0])[Ops.ToInt(a[1])];
        case "symbol?": CheckArity(1); return a[0] is Symbol;
        case "values": CheckArity(1, -1); return a.Length==1 ? a[0] : new MultipleValues(a);
        case "vector?": CheckArity(1); return a[0] is object[];
        case "vector-ref":
          CheckArity(2); CheckType(a, 0, typeof(object[])); CheckType(a, 1, typeof(int));
          return ((object[])a[0])[Ops.ToInt(a[1])];
        case "vector-length":
          CheckArity(1); CheckType(a, 0, typeof(object[]));
          return ((object[])a[0]).Length;
        default: throw new NotImplementedException("unhandled inline: "+name);
      }
    }
    
    IProcedure proc = Ops.ExpectProcedure(Function.Evaluate());
    return proc.Call(a);
  }
  #endregion

  #region GetNodeType
  public override Type GetNodeType()
  { if(Options.Optimize && Function is VariableNode)
      switch(((VariableNode)Function).Name.String)
      { case "eq?": case "eqv?": case "equal?":
        case "null?": case "pair?": case "char?": case "symbol?": case "string?": case "procedure?":
        case "not": case "string-null?":
          return typeof(bool);
        case "string-ref": case "char-upcase": case "char-downcase":
          return typeof(char);
        case "string-length":
          return typeof(int);
      }
    return typeof(object);
  }
  #endregion

  public override void MarkTail(bool tail)
  { Tail=tail;
    Function.MarkTail(false);
    foreach(Node n in Args) n.MarkTail(false);
  }

  public override void Optimize()
  { bool isconst=true;
    for(int i=0; i<Args.Length; i++) if(!Args[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst && Function is VariableNode && IsConstFunc(((VariableNode)Function).Name.String);
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { Function.Walk(w);
      foreach(Node n in Args) n.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node Function;
  public readonly Node[] Args;
  
  void CheckArity(int min) { CheckArity(min, min); }
  void CheckArity(int min, int max) { Ops.CheckArity(((VariableNode)Function).Name.String, Args.Length, min, max); }

  void CheckType(object[] args, int num, Type type)
  { if(AreEquivalent(args[num]==null ? null : args[num].GetType(), type)) return;
    try
    { if(type==typeof(int)) { Ops.ExpectInt(args[num]); return; }
    }
    catch { }
    throw new ArgumentException(string.Format("{0}: for argument {1}, expects type {2} but received {3}",
                                              ((VariableNode)Function).Name.String, num, Ops.TypeName(type),
                                              Ops.TypeName(args[num])));
  }

  void EmitVoids(CodeGenerator cg)
  { for(int i=0; i<Args.Length; i++)
    { Type type = typeof(void);
      Args[i].Emit(cg, ref type);
      if(type!=typeof(void)) cg.ILG.Emit(OpCodes.Pop);
    }
  }

  static void EmitFromBool(CodeGenerator cg, bool brtrue)
  { Label yes=cg.ILG.DefineLabel(), end=cg.ILG.DefineLabel();
    cg.ILG.Emit(brtrue ? OpCodes.Brtrue_S : OpCodes.Brfalse_S, yes);
    cg.EmitFieldGet(typeof(Ops), "FALSE");
    cg.ILG.Emit(OpCodes.Br_S, end);
    cg.ILG.MarkLabel(yes);
    cg.EmitFieldGet(typeof(Ops), "TRUE");
    cg.ILG.MarkLabel(end);
  }

  static bool FuncNameMatch(Name var, LambdaNode func)
  { Name binding = func.Binding;
    return binding!=null && var.Index==binding.Index && var.String==binding.String &&
           var.Depth==binding.Depth+(func.MaxNames!=0 ? 1 : 0);
  }
  
  static bool IsConstFunc(string name) { return Array.BinarySearch(constant, name)>=0; }
}
#endregion

#region DefineNode
public sealed class DefineNode : Node
{ public DefineNode(string name, Node value)
  { Name=new Name(name, Name.Global); Value=value;
    if(value is LambdaNode) ((LambdaNode)value).Name = name;
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(InFunc!=null) throw new SyntaxErrorException("define: only allowed at toplevel scope");
    cg.EmitFieldGet(typeof(TopLevel), "Current");
    cg.EmitString(Name.String);
    Value.Emit(cg);
    cg.EmitCall(typeof(TopLevel), "Bind");
    cg.Namespace.GetSlot(Name); // side effect of creating the slot
    if(etype!=typeof(void))
    { cg.EmitConstantObject(Symbol.Get(Name.String));
      etype = typeof(Symbol);
    }
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { if(InFunc!=null) throw new SyntaxErrorException("define: only allowed at toplevel scope");
    TopLevel.Current.Bind(Name.String, Value.Evaluate());
    return Symbol.Get(Name.String);
  }

  public override Type GetNodeType() { return typeof(Symbol); }

  public override void MarkTail(bool tail) { Tail=tail; Value.MarkTail(false); }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Value.Walk(w);
    w.PostWalk(this);
  }

  public readonly Name Name;
  public readonly Node Value;
}
#endregion

#region IfNode
public sealed class IfNode : Node
{ public IfNode(Node test, Node iftrue, Node iffalse) { Test=test; IfTrue=iftrue; IfFalse=iffalse; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(IsConstant)
    { EmitConstant(cg, Evaluate(), ref etype);
      if(Tail) cg.EmitReturn();
    }
    else
    { Label endlbl=Tail ? new Label() : cg.ILG.DefineLabel(), falselbl=cg.ILG.DefineLabel();
      Type truetype=IfTrue.GetNodeType(), falsetype=IfFalse==null ? null : IfFalse.GetNodeType();
      if(truetype!=falsetype || !Compatible(truetype, etype)) truetype=falsetype=etype=typeof(object);
      else etype=truetype;

      object ttype = MaybeEmitBranch(cg, Test, falselbl, false);
      if(ttype==null)
      { IfTrue.Emit(cg, ref truetype);
        Debug.Assert(Compatible(truetype, etype));
        if(!Tail) cg.ILG.Emit(OpCodes.Br, endlbl);
        cg.ILG.MarkLabel(falselbl);
        cg.EmitExpression(IfFalse, ref falsetype);
        Debug.Assert(Compatible(falsetype, etype));
      }
      else
      { if((bool)ttype) IfTrue.Emit(cg, ref truetype);
        else cg.EmitExpression(IfFalse, ref falsetype);
        cg.ILG.MarkLabel(falselbl);
      }

      if(!Tail) cg.ILG.MarkLabel(endlbl);
      else if(IfFalse==null) cg.EmitReturn();
    }
  }

  public override object Evaluate()
  { if(Ops.IsTrue(Test.Evaluate())) return IfTrue.Evaluate();
    else if(IfFalse!=null) return IfFalse.Evaluate();
    else return null;
  }

  public override Type GetNodeType()
  { if(IsConstant)
      return Ops.IsTrue(Test.Evaluate()) ? IfTrue.GetNodeType() : IfFalse!=null ? IfFalse.GetNodeType() : null;
    Type truetype=IfTrue.GetNodeType(), falsetype=IfFalse==null ? null : IfFalse.GetNodeType();
    return truetype==falsetype ? truetype : typeof(object);
  }

  public override void MarkTail(bool tail)
  { Tail=tail;
    Test.MarkTail(false);
    IfTrue.MarkTail(tail);
    if(IfFalse!=null) IfFalse.MarkTail(tail);
  }

  public override void Optimize()
  { if(Test.IsConstant)
    { bool test = Ops.IsTrue(Test.Evaluate());
      IsConstant = test && IfTrue.IsConstant || !test && (IfFalse==null || IfFalse.IsConstant);
    }
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { Test.Walk(w);
      IfTrue.Walk(w);
      if(IfFalse!=null) IfFalse.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node Test, IfTrue, IfFalse;
}
#endregion

#region LambdaNode
public class LambdaNode : Node
{ public LambdaNode(Node body) { Parameters=new Name[0]; Body=body; }
  public LambdaNode(string[] names, bool hasList, Node body)
  { Body=body; HasList=hasList; MaxNames=names.Length;

    Parameters=new Name[names.Length];
    for(int i=0; i<names.Length; i++) Parameters[i] = new Name(names[i], 0, i);
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(etype!=typeof(void))
    { index = lindex.Next;
      CodeGenerator impl = MakeImplMethod(cg);

      Slot tmpl;
      if(!cg.TypeGenerator.GetNamedConstant("template"+index, typeof(Template), out tmpl))
      { CodeGenerator icg = cg.TypeGenerator.GetInitializer();
        icg.ILG.Emit(OpCodes.Ldftn, (MethodInfo)impl.MethodBase);
        icg.EmitString(Name!=null ? Name : Binding!=null ? Binding.String : null);
        icg.EmitInt(Parameters.Length);
        icg.EmitBool(HasList);
        icg.EmitNew(typeof(Template), new Type[] { typeof(IntPtr), typeof(string), typeof(int), typeof(bool) });
        tmpl.EmitSet(icg);
      }

      tmpl.EmitGet(cg);
      cg.EmitArgGet(0);
      cg.EmitNew(RG.ClosureType, new Type[] { typeof(Template), typeof(LocalEnvironment) });
      etype = RG.ClosureType;
    }
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { string[] names = new string[Parameters.Length];
    for(int i=0; i<names.Length; i++) names[i] = Parameters[i].String;
    return new InterpretedProcedure(Name!=null ? Name : Binding!=null ? Binding.String : null, names, HasList, Body);
  }

  public override Type GetNodeType() { return RG.ClosureType; }

  public override void MarkTail(bool tail)
  { Tail=tail;
    Body.MarkTail(true);
  }

  public override void Preprocess()
  { base.Preprocess();
    Walk(new NodeDecorator((LambdaNode)this));
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Body.Walk(w);
    w.PostWalk(this);
  }

  public readonly Name[] Parameters;
  public readonly Node Body;
  public Name Binding;
  public string Name;
  public Label StartLabel;
  public int MaxNames;
  public readonly bool HasList;

  protected CodeGenerator MakeImplMethod(CodeGenerator cg)
  { CodeGenerator icg;
    icg = cg.TypeGenerator.DefineStaticMethod("lambda$" + index, typeof(object),
                                              new Type[] { typeof(LocalEnvironment), typeof(object[]) });

    icg.Namespace = new LocalNamespace(cg.Namespace, icg);
    if(MaxNames!=0)
    { icg.EmitArgGet(0);
      if(Parameters.Length!=0) icg.EmitArgGet(1);
      if(MaxNames==Parameters.Length)
        icg.EmitNew(typeof(LocalEnvironment), new Type[] { typeof(LocalEnvironment), typeof(object[]) });
      else
      { icg.EmitInt(MaxNames);
        icg.EmitNew(typeof(LocalEnvironment),
                    Parameters.Length==0 ? new Type[] { typeof(LocalEnvironment), typeof(int) }
                                         : new Type[] { typeof(LocalEnvironment), typeof(object[]), typeof(int) });
      }
      icg.EmitArgSet(0);
    }

    StartLabel = icg.ILG.DefineLabel();
    icg.ILG.MarkLabel(StartLabel);
    Body.Emit(icg);
    icg.Finish();
    return icg;
  }

  sealed class NodeDecorator : IWalker
  { public NodeDecorator(LambdaNode top) { func=this.top=top; }

    public bool Walk(Node node)
    { node.InFunc = func;

      if(node is LambdaNode)
      { LambdaNode oldFunc=func;
        int oldFree=freeStart, oldBound=boundStart;

        func  = (LambdaNode)node;
        freeStart = free.Count;
        boundStart = bound.Count;

        foreach(Name name in func.Parameters)
        { bound.Add(name);
          values.Add(null);
        }

        func.Body.Walk(this);

        for(int i=freeStart; i<free.Count; i++)
        { Name name = (Name)free[i];
          int index = IndexOf(name.String, bound, oldBound, boundStart);
          if(index==-1)
          { if(oldFunc==top || oldFunc is ModuleNode) name.Depth=Backend.Name.Global;
            else
            { if(func.MaxNames!=0) name.Depth++;
              free[freeStart++] = name;
            }
          }
          else
          { Name bname = (Name)bound[index];
            if(bname.Depth==Backend.Name.Local && IndexOf(name.String, oldFunc.Parameters)==-1)
            { bname.Depth = 0;
              bname.Index = name.Index = oldFunc.MaxNames++;
            }
            else name.Index=bname.Index;
            if(func.MaxNames!=0) name.Depth++;

            LambdaNode lambda = values[index] as LambdaNode;
            if(lambda!=null) lambda.Binding = name;
          }
        }

        values.RemoveRange(boundStart, bound.Count-boundStart);
        bound.RemoveRange(boundStart, bound.Count-boundStart);
        free.RemoveRange(freeStart, free.Count-freeStart);
        func=oldFunc; boundStart=oldBound; freeStart=oldFree;
        return false;
      }
      else if(node is LetNode)
      { LetNode let = (LetNode)node;
        foreach(Node n in let.Inits) if(n!=null) n.Walk(this);
        for(int i=0; i<let.Names.Length; i++)
        { bound.Add(let.Names[i]);
          values.Add(let.Inits[i]==null ? Backend.Binding.Unbound : let.Inits[i]);
        }
        let.Body.Walk(this);
        return false;
      }
      else if(node is VariableNode || node is SetNode)
      { Name name = node is SetNode ? ((SetNode)node).Name : ((VariableNode)node).Name;
        int index = IndexOf(name.String, bound);
        if(index==-1)
        { if(func==top) name.Depth=Backend.Name.Global;
          else
          { index = IndexOf(name.String, free);
            if(index==-1) { free.Add(name); name.Depth=0; }
            else
            { Name bname = name = (Name)free[index];
              if(node is SetNode) ((SetNode)node).Name=bname;
              else ((VariableNode)node).Name=bname;
            }
          }
        }
        else
        { Name bname = name = (Name)bound[index];
          if(node is SetNode)
          { SetNode set = (SetNode)node;
            set.Name=bname;

            if(values[index]==Backend.Binding.Unbound) values[index]=set.Value;
            else values[index]=null;
          }
          else ((VariableNode)node).Name=bname;
        }
      }
      return true;
    }

    public void PostWalk(Node node)
    { if(node is LetNode)
      { LetNode let = (LetNode)node;
        int start=bound.Count-let.Names.Length, len=let.Names.Length;

        for(int i=start; i<values.Count; i++)
        { LambdaNode lambda = values[i] as LambdaNode;
          if(lambda!=null) lambda.Binding = (Name)bound[i];
        }

        bound.RemoveRange(start, len);
        values.RemoveRange(start, len);
      }
      else if(node is DefineNode)
      { DefineNode def = (DefineNode)node;
        if(def.InFunc==top || def.InFunc is ModuleNode) def.InFunc=null;
      }
    }

    int IndexOf(string name, IList list) // these are kind of DWIMish
    { if(list==bound) return IndexOf(name, list, boundStart, list.Count);
      if(list==free)  return IndexOf(name, list, freeStart, list.Count);
      return IndexOf(name, list, 0, list.Count);
    }

    LambdaNode func, top;
    ArrayList bound=new ArrayList(), free=new ArrayList(), values=new ArrayList();
    int boundStart, freeStart;

    static int IndexOf(string name, IList list, int start, int end)
    { for(end--; end>=start; end--) if(((Name)list[end]).String==name) return end;
      return -1;
    }
  }

  long index;

  static Index lindex = new Index();
}
#endregion

#region ListNode
public sealed class ListNode : Node
{ public ListNode(Node[] items, Node dot) { Items=items; Dot=dot; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(!IsConstant || etype!=typeof(void)) // TODO: maybe use cg.EmitConstantObject()
    { if(IsConstant) cg.EmitConstantObject(Evaluate());
      else cg.EmitList(Items, Dot);
      etype = Items.Length==0 && Dot==null ? null : typeof(Pair);
    }
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { object obj = Dot==null ? null : Dot.Evaluate();
    for(int i=Items.Length-1; i>=0; i--) obj = new Pair(Items[i].Evaluate(), obj);
    return obj;
  }

  public override Type GetNodeType() { return Items.Length==0 ? null : typeof(Pair); }

  public override void Optimize()
  { bool isconst=(Dot==null || Dot.IsConstant);
    if(isconst) for(int i=0; i<Items.Length; i++) if(!Items[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { foreach(Node n in Items) n.Walk(w);
      if(Dot!=null) Dot.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node[] Items;
  public readonly Node Dot;
}
#endregion

#region LiteralNode
public sealed class LiteralNode : Node
{ public LiteralNode(object value) { Value=value; }
  public override void Emit(CodeGenerator cg, ref Type etype)
  { EmitConstant(cg, Value, ref etype);
    if(Tail) cg.EmitReturn();
  }
  public override object Evaluate() { return Value; }
  public override Type GetNodeType() { return Value==null ? null : Value.GetType(); }
  public override void Optimize() { IsConstant = true; }

  public readonly object Value;
}
#endregion

#region LetNode
public sealed class LetNode : Node
{ public LetNode(string[] names, Node[] inits, Node body)
  { Inits=inits; Body=body;

    Names = new Name[names.Length];
    for(int i=0; i<names.Length; i++) Names[i] = new Name(names[i]);
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { for(int i=0; i<Inits.Length; i++)
      if(Inits[i]!=null)
      { Inits[i].Emit(cg);
        cg.EmitSet(Names[i]);
      }
      else if(Options.Debug)
      { cg.EmitFieldGet(typeof(Binding), "Unbound");
        cg.EmitSet(Names[i]);
      }
    Body.Emit(cg, ref etype);
    for(int i=0; i<Names.Length; i++) cg.Namespace.RemoveSlot(Names[i]);
  }

  public override object Evaluate()
  { if(IsConstant || Inits.Length==0) return Body.Evaluate();

    InterpreterEnvironment ne, old=InterpreterEnvironment.Current;
    try
    { InterpreterEnvironment.Current = ne = new InterpreterEnvironment(old);
      for(int i=0; i<Inits.Length; i++)
        ne.Bind(Names[i].String, Inits[i]==null ? null : Inits[i].Evaluate());
      return Body.Evaluate();
    }
    finally { InterpreterEnvironment.Current=old; }
  }

  public override Type GetNodeType() { return Body.GetNodeType(); }

  public override void MarkTail(bool tail)
  { foreach(Node n in Inits) if(n!=null) n.MarkTail(false);
    Body.MarkTail(tail);
  }

  public override void Optimize()
  { bool isconst = Body.IsConstant;
    if(isconst) for(int i=0; i<Inits.Length; i++) if(Inits[i]!=null && !Inits[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { foreach(Node n in Inits) if(n!=null) n.Walk(w);
      Body.Walk(w);
    }
    w.PostWalk(this);
  }

  public Name[] Names;
  public Node[] Inits;
  public Node Body;
}
#endregion

#region ModuleNode
public sealed class ModuleNode : LambdaNode
{ public ModuleNode(string moduleName, CallNode provide, Node body) : base(body)
  { if(!(provide.Function is VariableNode) || ((VariableNode)provide.Function).Name.String != "provides")
      goto badProvides;

    Name=moduleName;
    
    ModuleWalker mw = new ModuleWalker(); // FIXME: the macro walking will be wrong because (expand) removes macros
    Walk(mw);

    ArrayList exports = new ArrayList();

    try
    { foreach(Node n in provide.Args)
        if(n is VariableNode) exports.Add(new Module.Export(((VariableNode)n).Name.String));
        else if(n is CallNode)
        { CallNode cn = (CallNode)n;
          switch(((VariableNode)cn.Function).Name.String)
          { case "rename":
            { if(cn.Args.Length!=2) goto badProvides;
              string name = ((VariableNode)cn.Args[0]).Name.String;
              exports.Add(new Module.Export(name, ((VariableNode)cn.Args[1]).Name.String,
                                            mw.Macros.Contains(name) ? TopLevel.NS.Macro : TopLevel.NS.Main));
              break;
            }
            case "all-defined":
              foreach(string name in mw.Defines) exports.Add(new Module.Export(name));
              foreach(string name in mw.Macros) exports.Add(new Module.Export(name, TopLevel.NS.Macro));
              break;
            case "all-defined-except":
              foreach(string name in mw.Defines) if(!Except(name, cn.Args)) exports.Add(new Module.Export(name));
              foreach(string name in mw.Macros)
                if(!Except(name, cn.Args)) exports.Add(new Module.Export(name, TopLevel.NS.Macro));
              break;
          }
        }
        else goto badProvides;
     }
     catch { goto badProvides; }
     
    Exports = (Module.Export[])exports.ToArray(typeof(Module.Export));

    return;
    badProvides: throw new SyntaxErrorException("Invalid 'provides' declaration");
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { TopLevel.Current.SetModule(Name, ModuleGenerator.Generate(this));
    if(etype!=typeof(void))
    { cg.EmitConstantObject(Symbol.Get(Name));
      etype = typeof(Symbol);
    }
    if(Tail) cg.EmitReturn();
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Body.Walk(w);
    w.PostWalk(this);
  }

  public readonly Module.Export[] Exports;
  
  sealed class ModuleWalker : IWalker
  { public bool Walk(Node node)
    { if(node is DefineNode) Defines.Add(((DefineNode)node).Name.String);
      else if(node is CallNode)
      { CallNode cn = (CallNode)node;
        VariableNode vn = cn.Function as VariableNode;
        if(vn!=null && vn.Name.String=="install-expander")
        { LiteralNode lit = cn.Args.Length==0 ? null : cn.Args[0] as LiteralNode;
          Symbol sym = lit==null ? null : lit.Value as Symbol;
          if(sym!=null) Macros.Add(sym.Name);
        }
      }
      return false;
    }

    public void PostWalk(Node node) { }

    public ArrayList Defines=new ArrayList(), Macros=new ArrayList();
  }
  
  static bool Except(string name, Node[] nodes)
  { foreach(VariableNode n in nodes) if(n.Name.String==name) return true;
    return false;
  }
}
#endregion

#region SetNode
public sealed class SetNode : Node
{ public SetNode(string name, Node value) { Name=new Name(name); Value=value; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { Value.Emit(cg);
    if(etype!=typeof(void))
    { cg.ILG.Emit(OpCodes.Dup);
      etype = typeof(object);
    }
    cg.EmitSet(Name);
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { object value = Value.Evaluate();
    InterpreterEnvironment cur = InterpreterEnvironment.Current;
    if(cur==null) TopLevel.Current.Set(Name.String, value);
    else cur.Set(Name.String, value);
    return value;
  }

  public override Type GetNodeType() { return Value.GetNodeType(); }

  public override void MarkTail(bool tail)
  { Tail=tail;
    Value.MarkTail(false);
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Value.Walk(w);
    w.PostWalk(this);
  }

  public Name Name;
  public readonly Node Value;
}
#endregion

#region VariableNode
public sealed class VariableNode : Node
{ public VariableNode(string name) { Name=new Name(name); }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(Options.Debug)
    { cg.EmitGet(Name);
      cg.EmitString(Name.String);
      cg.EmitCall(typeof(Ops), "CheckVariable");
      etype = typeof(object);
    }
    else if(etype!=typeof(void))
    { cg.EmitGet(Name);
      etype = typeof(object);
    }
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { InterpreterEnvironment cur = InterpreterEnvironment.Current;
    return cur==null ? TopLevel.Current.Get(Name.String) : cur.Get(Name.String);
  }

  public override Type GetNodeType() { return typeof(object); }

  public Name Name;
}
#endregion

#region VectorNode
public sealed class VectorNode : Node
{ public VectorNode(Node[] items) { Items=items; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(etype==typeof(void))
    { foreach(Node node in Items)
        if(!node.IsConstant)
        { node.Emit(cg, ref etype);
          if(etype!=typeof(void))
          { cg.ILG.Emit(OpCodes.Pop);
            etype = typeof(void);
          }
        }
    }
    else
    { if(IsConstant) cg.EmitConstantObject(Evaluate());
      else cg.EmitObjectArray(Items);
      etype = typeof(object[]);
    }
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { object[] ret = new object[Items.Length];
    for(int i=0; i<ret.Length; i++) ret[i] = Items[i].Evaluate();
    return ret;
  }

  public override Type GetNodeType() { return typeof(object[]); }

  public override void MarkTail(bool tail)
  { Tail=tail;
    foreach(Node node in Items) node.MarkTail(false);
  }

  public override void Optimize()
  { bool isconst=true;
    foreach(Node node in Items) if(!node.IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) foreach(Node n in Items) n.Walk(w);
    w.PostWalk(this);
  }

  public readonly Node[] Items;
}
#endregion

} // namespace NetLisp.Backend
