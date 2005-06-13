using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

// TODO: add exception handling

namespace NetLisp.Backend
{

public interface IWalker
{ void PostWalk(Node node);
  bool Walk(Node node);
}

public sealed class AST
{ public static Node Create(object obj)
  { Node node = Parse(obj);
    node.Preprocess();
    return node;
  }

  public static long NextIndex { get { lock(indexLock) return index++; } }

  static Node Parse(object obj)
  { Symbol sym = obj as Symbol;
    if(sym!=null) return new VariableNode(sym.Name);

    Pair pair = obj as Pair;
    if(pair==null) return new LiteralNode(obj);

    sym = pair.Car as Symbol;
    if(sym!=null)
      switch(sym.Name)
      { case "if":
        { int len = Ops.Length(pair);
          if(len<3 || len>4) throw new Exception("if: expects 3 or 4 forms"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          Pair next = (Pair)pair.Cdr;
          return new IfNode(Parse(pair.Car), Parse(next.Car), next.Cdr==null ? null : Parse(Ops.FastCadr(next)));
        }
        case "begin":
        { if(Ops.Length(pair)<2) throw new Exception("begin: no forms given");
          return ParseBody((Pair)pair.Cdr);
        }
        case "lambda":
        { if(Ops.Length(pair)<3) throw new Exception("too few for lambda"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          bool hasList;
          return new LambdaNode(ParseLambaList((Pair)pair.Car, out hasList), hasList, ParseBody((Pair)pair.Cdr));
        }
        case "quote":
        { if(Ops.Length(pair)!=2) throw new Exception("wrong number for quote"); // FIXME: ex
          return Quote(Ops.FastCadr(pair));
        }
        case "set!":
        { if(Ops.Length(pair)!=3) throw new Exception("wrong number for set!"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          sym = pair.Car as Symbol;
          if(sym==null) throw new Exception("set must set symbol"); // FIXME: SyntaxException
          return new SetNode(sym.Name, ParseBody((Pair)pair.Cdr));
        }
        case "define":
        { int length = Ops.Length(pair);
          if(length<3) throw new Exception("wrong number for define"); // FIXME: ex
          pair = (Pair)pair.Cdr;
          sym = pair.Car as Symbol;
          if(sym!=null)
          { if(length!=3) throw new Exception("wrong number for define"); // FIXME: ex
            return new DefineNode(sym.Name, ParseBody((Pair)pair.Cdr)); // (define name value)
          }
          Pair names = (Pair)pair.Car;
          if(names.Cdr is Symbol) // (define (name . list) body ...)
            return new DefineNode(((Symbol)names.Car).Name,
                                  new LambdaNode(new string[] { ((Symbol)names.Cdr).Name },
                                                 true, ParseBody((Pair)pair.Cdr)));
          else // define (name a0 a1 ...) body ...)
          { bool hasList;
            return new DefineNode(((Symbol)names.Car).Name,
                                  new LambdaNode(ParseLambaList((Pair)names.Cdr, out hasList),
                                                 hasList, ParseBody((Pair)pair.Cdr)));
          }
        }
      }

    Node func = Parse(pair.Car);
    ArrayList args = new ArrayList();
    while(true)
    { pair = pair.Cdr as Pair;
      if(pair==null) break;
      args.Add(Parse(pair.Car));
    }
    return new CallNode(func, (Node[])args.ToArray(typeof(Node)));
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

  static string[] ParseLambaList(Pair list, out bool hasList)
  { hasList = false;

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
        names.Add(sym);
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

  static long index;
  static readonly object indexLock = "<AST_INDEX_LOCK>";
}

public abstract class Node
{ public abstract void Emit(CodeGenerator cg);
  public virtual object Evaluate() { throw new NotSupportedException(); }

  public virtual void Preprocess()
  { MarkTail(true);
    Walk(new NodeDecorator());
  }
  
  public virtual void Walk(IWalker w)
  { w.Walk(this);
    w.PostWalk(this);
  }
  
  public bool Tail;

  internal virtual void MarkTail(bool tail) { Tail=tail; }

  protected LambdaNode InFunc;
  
  sealed class NodeDecorator : IWalker
  { public bool Walk(Node node)
    { node.InFunc = func;

      if(node is LambdaNode)
      { LambdaNode old = func;
        func = (LambdaNode)node;
        func.Body.Walk(this);
        func = old;
      }
      return true;
    }
    
    public void PostWalk(Node node)
    {
    }
    
    LambdaNode func;
  }
}

public sealed class BodyNode : Node
{ public BodyNode(Node[] forms) { Forms=forms; }

  public override void Emit(CodeGenerator cg)
  { for(int i=0; i<Forms.Length; i++)
    { Forms[i].Emit(cg);
      if(i!=Forms.Length-1) cg.ILG.Emit(OpCodes.Pop);
    }
  }

  public override object Evaluate()
  { object ret=null;
    foreach(Node n in Forms) ret = n.Evaluate();
    return ret;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) foreach(Node n in Forms) n.Walk(w);
    w.PostWalk(this);
  }

  public readonly Node[] Forms;
  
  internal override void MarkTail(bool tail)
  { for(int i=0; i<Forms.Length; i++) Forms[i].MarkTail(tail && i==Forms.Length-1);
  }
}

public sealed class CallNode : Node
{ public CallNode(Node func, Node[] args) { Function=func; Args=args; }
  static CallNode()
  { ops = new Hashtable();
    string[] arr = new string[]
    { "-", "Subtract", "bitnot", "BitwiseNegate", "+", "Add", "*", "Multiply", "/", "Divide", "//", "FloorDivide",
      "%", "Modulus",  "bitand", "BitwiseAnd", "bitor", "BitwiseOr", "bitxor", "BitwiseXor", "=", "Equal",
      "!=", "NotEqual", "<", "Less", ">", "More", "<=", "LessEqual", ">=", "MoreEqual", "pow", "Power",
      "lshift", "LeftShift", "rshift", "RightShift", "powmod", "PowerMod"
    };
    for(int i=0; i<arr.Length; i+=2) ops[arr[i]] = arr[i+1];
  }

  static Hashtable ops;

  public override void Emit(CodeGenerator cg)
  { if(Function is VariableNode)
    { string name=((VariableNode)Function).Name, opname=(string)ops[name];
      switch(name)
      { case "eq?": case "eqv?": case "equal?":
        { if(Args.Length!=2) goto normal;
          Label yes=cg.ILG.DefineLabel(), end=cg.ILG.DefineLabel();
          Args[0].Emit(cg);
          Args[1].Emit(cg);
          if(name=="eq?") cg.ILG.Emit(OpCodes.Ceq);
          else cg.EmitCall(typeof(Ops), name=="eqv?" ? "EqvP" : "EqualP");
          cg.ILG.Emit(OpCodes.Brtrue_S, yes);
          cg.EmitFieldGet(typeof(Ops), "FALSE");
          cg.ILG.Emit(OpCodes.Br_S, end);
          cg.ILG.MarkLabel(yes);
          cg.EmitFieldGet(typeof(Ops), "TRUE");
          cg.ILG.MarkLabel(end);
          goto ret;
        }
        case "null?": case "pair?": case "char?": case "string?": case "procedure?": case "complex?":
        case "not": case "string-null?":
        { if(Args.Length!=1) goto normal;
          Label yes=cg.ILG.DefineLabel(), end=cg.ILG.DefineLabel();
          Args[0].Emit(cg);
          Type type=null;
          switch(name)
          { case "pair?": type=typeof(Pair); break;
            case "char?": type=typeof(char); break;
            case "string?": type=typeof(string); break;
            case "complex?": type=typeof(Complex); break;
            case "procedure?": type=typeof(IProcedure); break;
            case "not": cg.EmitCall(typeof(Ops), "IsTrue"); break;
            case "string-null?":
              cg.ILG.Emit(OpCodes.Castclass, typeof(string));
              cg.EmitPropGet(typeof(string), "Length");
              break;
          }
          if(type!=null)
          { cg.ILG.Emit(OpCodes.Isinst, type);
            cg.ILG.Emit(OpCodes.Brtrue_S, yes);
          }
          else cg.ILG.Emit(OpCodes.Brfalse_S, yes);
          cg.EmitFieldGet(typeof(Ops), "FALSE");
          cg.ILG.Emit(OpCodes.Br_S, end);
          cg.ILG.MarkLabel(yes);
          cg.EmitFieldGet(typeof(Ops), "TRUE");
          cg.ILG.MarkLabel(end);
          goto ret;
        }
        case "string-length":
          if(Args.Length!=1) goto normal;
          Args[0].Emit(cg);
          cg.ILG.Emit(OpCodes.Castclass, typeof(string));
          cg.EmitPropGet(typeof(string), "Length");
          cg.ILG.Emit(OpCodes.Box, typeof(int));
          goto ret;
        case "string-ref":
          if(Args.Length!=2) goto normal;
          Args[0].Emit(cg);
          cg.ILG.Emit(OpCodes.Castclass, typeof(string));
          Args[1].Emit(cg);
          cg.EmitCall(typeof(Ops), "ToInt");
          cg.EmitPropGet(typeof(string), "Chars");
          cg.ILG.Emit(OpCodes.Box, typeof(char));
          goto ret;
        case "car": case "cdr":
          if(Args.Length!=1) goto normal;
          Args[0].Emit(cg);
          cg.ILG.Emit(OpCodes.Castclass, typeof(Pair));
          cg.EmitFieldGet(typeof(Pair), name=="car" ? "Car" : "Cdr");
          goto ret;
        case "char-upcase": case "char-downcase":
          if(Args.Length!=1) goto normal;
          cg.ILG.Emit(OpCodes.Unbox, typeof(char));
          cg.ILG.Emit(OpCodes.Ldind_I2);
          cg.EmitCall(typeof(char), name=="char-upcase" ? "ToUpper" : "ToLower", new Type[] { typeof(char) });
          cg.ILG.Emit(OpCodes.Box, typeof(char));
          goto ret;
        case "set-car!": case "set-cdr!":
          if(Args.Length!=2) goto normal;
          Slot tmp = cg.AllocLocalTemp(typeof(object));
          Args[1].Emit(cg);
          tmp.EmitSet(cg);
          Args[0].Emit(cg);
          cg.ILG.Emit(OpCodes.Castclass, typeof(Pair));
          tmp.EmitGet(cg);
          cg.EmitFieldSet(typeof(Pair), name=="set-car!" ? "Car" : "Cdr");
          tmp.EmitGet(cg);
          cg.FreeLocalTemp(tmp);
          goto ret;
        case "-": case "bitnot":
          if(Args.Length==1)
          { opname = "Negate";
            Args[0].Emit(cg);
            break;
          }
          else goto plusetc;
        case "+": case "*": case "/": case "//": case "%": case "bitand": case "bitor": case "bitxor": plusetc:
          if(Args.Length<2) goto normal;
          Args[0].Emit(cg);
          for(int i=1; i<Args.Length-1; i++)
          { Args[i].Emit(cg);
            cg.EmitCall(typeof(Ops), opname);
          }
          Args[Args.Length-1].Emit(cg);
          break;
        case "=": case "!=": case "<": case ">": case "<=": case ">=":
          if(Args.Length!=2) goto normal; // TODO: do the code generation for more than 2 arguments
          Args[0].Emit(cg);
          Args[1].Emit(cg);
          break;
        case "pow": case "lshift": case "rshift":
          if(Args.Length!=2) goto normal;
          Args[0].Emit(cg);
          Args[1].Emit(cg);
          break;
        case "powmod":
          if(Args.Length!=3) goto normal;
          Args[0].Emit(cg);
          Args[1].Emit(cg);
          Args[2].Emit(cg);
          break;
        case "values":
          cg.EmitObjectArray(Args);
          cg.EmitNew(typeof(MultipleValues), new Type[] { typeof(object[]) });
          goto ret;
        default: goto normal;
      }
      if(Tail) cg.ILG.Emit(OpCodes.Tailcall);
      cg.EmitCall(typeof(Ops), opname);
      ret:
      if(Tail) cg.EmitReturn();
      return;
    }
    
    normal:
    Function.Emit(cg);
    cg.ILG.Emit(OpCodes.Castclass, typeof(IProcedure));
    cg.EmitObjectArray(Args);
    if(Tail) cg.ILG.Emit(OpCodes.Tailcall);
    cg.EmitCall(typeof(IProcedure), "Call");
    if(Tail) cg.EmitReturn();
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
  
  internal override void MarkTail(bool tail)
  { Tail=tail;
    Function.MarkTail(false);
    foreach(Node n in Args) n.MarkTail(false);
  }
}

// FIXME: support internal definitions: http://www.swiss.ai.mit.edu/projects/scheme/documentation/scheme_3.html#SEC35
public sealed class DefineNode : Node
{ public DefineNode(string name, Node value) { Name=name; Value=value; }

  public override void Emit(CodeGenerator cg)
  { cg.EmitFieldGet(typeof(TopLevel), "Current");
    cg.EmitString(Name);
    Value.Emit(cg);
    cg.EmitCall(typeof(TopLevel), "Bind");
    cg.Namespace.GetGlobalSlot(Name); // side effect of creating the slot
    cg.EmitConstantObject(Symbol.Get(Name));
    if(Tail) cg.EmitReturn();
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Value.Walk(w);
    w.PostWalk(this);
  }

  public readonly string Name;
  public readonly Node Value;
  
  internal override void MarkTail(bool tail) { Tail=tail; Value.MarkTail(false); }
}

public sealed class IfNode : Node
{ public IfNode(Node test, Node iftrue, Node iffalse) { Test=test; IfTrue=iftrue; IfFalse=iffalse; }

  public override void Emit(CodeGenerator cg)
  { Label end = IfFalse==null ? new Label() : cg.ILG.DefineLabel();
    Label falselbl = cg.ILG.DefineLabel();

    cg.EmitIsTrue(Test);
    cg.ILG.Emit(OpCodes.Brfalse, falselbl);
    IfTrue.Emit(cg);
    if(IfFalse==null)
    { cg.ILG.MarkLabel(falselbl);
      if(Tail) cg.ILG.Emit(OpCodes.Ldnull);
    }
    else
    { cg.ILG.Emit(OpCodes.Br, end);
      cg.ILG.MarkLabel(falselbl);
      cg.EmitExpression(IfFalse);
      cg.ILG.MarkLabel(end);
    }
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { if(Ops.IsTrue(Test.Evaluate())) return IfTrue.Evaluate();
    else if(IfFalse!=null) return IfFalse.Evaluate();
    else return null;
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
  
  internal override void MarkTail(bool tail)
  { Tail=tail;
    Test.MarkTail(false);
    IfTrue.MarkTail(tail);
    if(IfFalse!=null) IfFalse.MarkTail(tail);
  }
}

public sealed class LambdaNode : Node
{ public LambdaNode(string[] names, bool hasList, BodyNode body)
  { Parameters=names; Body=body; HasList=hasList;

    // convert all the nested defines into a let
    int num=0;
    for(; num<body.Forms.Length; num++) if(!(body.Forms[num] is DefineNode)) break;
    if(num==0) Body=body;
    else
    { Node[] newbody = new Node[body.Forms.Length-num];
      Array.Copy(body.Forms, num, newbody, 0, newbody.Length);
      Body = new BodyNode(newbody);
      while(num-- != 0) Body = new LetNode(new string[] { ((DefineNode)body.Forms[num]).Name }, Body);
    }
  }

  public override void Emit(CodeGenerator cg)
  { index = AST.NextIndex;
    CodeGenerator impl = MakeImplMethod(cg);

    Slot tmpl;
    if(!cg.TypeGenerator.GetNamedConstant("template"+index, typeof(Template), out tmpl))
    { CodeGenerator icg = cg.TypeGenerator.GetInitializer();
      icg.ILG.Emit(OpCodes.Ldftn, (MethodInfo)impl.MethodBase);
      icg.EmitConstantObject(Parameters);
      icg.EmitBool(HasList);
      icg.EmitNew(typeof(Template), new Type[] { typeof(IntPtr), typeof(string[]), typeof(bool) });
      tmpl.EmitSet(icg);
    }

    tmpl.EmitGet(cg);
    cg.EmitArgGet(0);
    cg.EmitNew(RG.ClosureType, new Type[] { typeof(Template), typeof(LocalEnvironment) });
    if(Tail) cg.EmitReturn();
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Body.Walk(w);
    w.PostWalk(this);
  }

  public readonly string[] Parameters;
  public readonly Node Body;
  public readonly bool HasList;

  internal override void MarkTail(bool tail)
  { Tail=tail;
    Body.MarkTail(true);
  }

  CodeGenerator MakeImplMethod(CodeGenerator cg)
  { CodeGenerator icg;
    icg = cg.TypeGenerator.DefineStaticMethod("lambda$" + index, typeof(object),
                                              new Type[] { typeof(LocalEnvironment), typeof(object[]) });

    if(Parameters.Length==0) icg.Namespace = cg.Namespace;
    else
    { icg.Namespace = new EnvironmentNamespace(cg.Namespace, icg, Parameters);
      icg.EmitArgGet(0);
      icg.EmitArgGet(1);
      icg.EmitNew(typeof(LocalEnvironment), new Type[] { typeof(LocalEnvironment), typeof(object[]) });
      icg.EmitArgSet(0);
    }

    Body.Emit(icg);
    icg.Finish();
    return icg;
  }

  long index;
}

public sealed class ListNode : Node
{ public ListNode(Node[] items, Node dot) { Items=items; Dot=dot; }

  public override void Emit(CodeGenerator cg)
  { Slot pair = cg.AllocLocalTemp(typeof(Pair));
    ConstructorInfo cons = typeof(Pair).GetConstructor(new Type[] { typeof(object), typeof(object) });

    cg.EmitExpression(Items[Items.Length-1]);
    cg.EmitExpression(Dot);
    cg.EmitNew(cons);
    pair.EmitSet(cg);
    for(int i=Items.Length-2; i>=0; i--)
    { Items[i].Emit(cg);
      pair.EmitGet(cg);
      cg.EmitNew(cons);
      pair.EmitSet(cg);
    }
    pair.EmitGet(cg);
    cg.FreeLocalTemp(pair);

    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { object obj = Dot==null ? null : Dot.Evaluate();
    for(int i=Items.Length-1; i>=0; i--) obj = new Pair(Items[i].Evaluate(), obj);
    return obj;
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

public sealed class LiteralNode : Node
{ public LiteralNode(object value) { Value=value; }
  public override void Emit(CodeGenerator cg)
  { cg.EmitConstantObject(Value);
    if(Tail) cg.EmitReturn();
  }
  public override object Evaluate() { return Value; }
  public readonly object Value;
}

public sealed class LetNode : Node
{ public LetNode(string[] names, Node[] inits) { Names=names; Inits=inits; }

  public override void Emit(CodeGenerator cg)
  { 
  }

  public string[] Names;
  public Node[] Inits;
}

public sealed class Options
{ private Options() { }
  public static bool Debug, Optimize;
}

public sealed class SetNode : Node
{ public SetNode(string name, Node value) { Name=name; Value=value; }

  public override void Emit(CodeGenerator cg)
  { Value.Emit(cg);
    cg.ILG.Emit(OpCodes.Dup);
    cg.EmitSet(Name);
    if(Tail) cg.EmitReturn();
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Value.Walk(w);
    w.PostWalk(this);
  }

  public readonly string Name;
  public readonly Node Value;

  internal override void MarkTail(bool tail)
  { Tail=tail;
    Value.MarkTail(false);
  }
}

public sealed class VariableNode : Node
{ public VariableNode(string name) { Name=name; }

  public override void Emit(CodeGenerator cg)
  { cg.EmitGet(Name);
    if(Tail) cg.EmitReturn();
  }

  public readonly string Name;
}

} // namespace NetLisp.Backend
