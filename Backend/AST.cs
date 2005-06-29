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
{ public static LambdaNode Create(object obj)
  { // wrapping it in a lambda node is done so we can keep the preprocessing code simple, and so that we can support
    // top-level closures. it's unwrapped later on by SnippetMaker.Generate()
    LambdaNode node = new LambdaNode(new string[0], false, Parse(obj));
    node.Preprocess();
    return node;
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
      return new LetNode(names, (Node[])args.ToArray(typeof(Node)), fl.Body);
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
{ public abstract void Emit(CodeGenerator cg);
  public virtual object Evaluate() { throw new NotSupportedException(); }

  public virtual void MarkTail(bool tail) { Tail=tail; }
  public virtual void Preprocess() { MarkTail(true); }

  public virtual void Walk(IWalker w)
  { w.Walk(this);
    w.PostWalk(this);
  }
  
  public LambdaNode InFunc;
  public bool Tail;
}
#endregion

#region BodyNode
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
  
  public override void MarkTail(bool tail)
  { for(int i=0; i<Forms.Length; i++) Forms[i].MarkTail(tail && i==Forms.Length-1);
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
{ public CallNode(Node func, Node[] args) { Function=func; Args=args; }
  static CallNode()
  { ops = new Hashtable();
    string[] arr = new string[]
    { "-", "Subtract", "bitnot", "BitwiseNegate", "+", "Add", "*", "Multiply", "/", "Divide", "//", "FloorDivide",
      "%", "Modulus",  "bitand", "BitwiseAnd", "bitor", "BitwiseOr", "bitxor", "BitwiseXor", "=", "Equal",
      "!=", "NotEqual", "<", "Less", ">", "More", "<=", "LessEqual", ">=", "MoreEqual", "expt", "Power",
      "lshift", "LeftShift", "rshift", "RightShift", "exptmod", "PowerMod"
    };
    for(int i=0; i<arr.Length; i+=2) ops[arr[i]] = arr[i+1];
  }

  static Hashtable ops;

  public override void Emit(CodeGenerator cg)
  { if(Options.Optimize && Function is VariableNode)
    { VariableNode vn = (VariableNode)Function;
      if(Tail && FuncNameMatch(vn.Name, InFunc))
      { int positional = InFunc.Parameters.Length-(InFunc.HasList ? 1 : 0);
        if(Args.Length<positional)
          throw new Exception(string.Format("{0} expects {1}{2} args, but is being passed {3}",
                                            vn.Name, InFunc.HasList ? "at least " : "", positional, Args.Length));
        for(int i=0; i<positional; i++)
        { Args[i].Emit(cg);
          cg.EmitSet(InFunc.Parameters[i]);
        }
        if(InFunc.HasList) cg.EmitList(Args, positional);
        cg.ILG.Emit(OpCodes.Br, InFunc.StartLabel);
        return;
      }
      else
      { string name=vn.Name.String, opname=(string)ops[name];
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
          case "null?": case "pair?": case "char?": case "symbol?": case "string?": case "procedure?":
          case "not": case "string-null?":
          { if(Args.Length!=1) goto normal;
            Label yes=cg.ILG.DefineLabel(), end=cg.ILG.DefineLabel();
            Args[0].Emit(cg);
            Type type=null;
            switch(name)
            { case "pair?": type=typeof(Pair); break;
              case "char?": type=typeof(char); break;
              case "symbol?": type=typeof(Symbol); break;
              case "string?": type=typeof(string); break;
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
          case "expt": case "lshift": case "rshift":
            if(Args.Length!=2) goto normal;
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            break;
          case "exptmod":
            if(Args.Length!=3) goto normal;
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            Args[2].Emit(cg);
            break;
          case "values":
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
        ret:
        if(Tail) cg.EmitReturn();
        return;
      }
    }
    
    normal:
    Function.Emit(cg);
    cg.ILG.Emit(OpCodes.Castclass, typeof(IProcedure));
    cg.EmitObjectArray(Args);
    if(Tail) cg.ILG.Emit(OpCodes.Tailcall);
    cg.EmitCall(typeof(IProcedure), "Call");
    if(Tail) cg.EmitReturn();
  }
  
  public override void MarkTail(bool tail)
  { Tail=tail;
    Function.MarkTail(false);
    foreach(Node n in Args) n.MarkTail(false);
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
  
  static bool FuncNameMatch(Name var, LambdaNode func)
  { Name binding = func.Binding;
    return binding!=null && var.Index==binding.Index && var.String==binding.String &&
           var.Depth==binding.Depth+(func.MaxNames!=0 ? 1 : 0);
  }
}
#endregion

#region DefineNode
public sealed class DefineNode : Node
{ public DefineNode(string name, Node value)
  { Name=new Name(name, Name.Global); Value=value;
    if(value is LambdaNode) ((LambdaNode)value).Name = name;
  }

  public override void Emit(CodeGenerator cg)
  { if(InFunc!=null) throw new SyntaxErrorException("define: only allowed at toplevel scope");
    cg.EmitFieldGet(typeof(TopLevel), "Current");
    cg.EmitString(Name.String);
    Value.Emit(cg);
    cg.EmitCall(typeof(TopLevel), "Bind");
    cg.Namespace.GetSlot(Name); // side effect of creating the slot
    cg.EmitConstantObject(Symbol.Get(Name.String));
    if(Tail) cg.EmitReturn();
  }
  
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

  public override void Emit(CodeGenerator cg)
  { Label endlbl=cg.ILG.DefineLabel(), falselbl=cg.ILG.DefineLabel();

    cg.EmitIsTrue(Test);
    cg.ILG.Emit(OpCodes.Brfalse, falselbl);
    IfTrue.Emit(cg);
    cg.ILG.Emit(OpCodes.Br, endlbl);
    cg.ILG.MarkLabel(falselbl);
    cg.EmitExpression(IfFalse);
    cg.ILG.MarkLabel(endlbl);
    if(Tail) cg.EmitReturn();
  }

  public override object Evaluate()
  { if(Ops.IsTrue(Test.Evaluate())) return IfTrue.Evaluate();
    else if(IfFalse!=null) return IfFalse.Evaluate();
    else return null;
  }
  
  public override void MarkTail(bool tail)
  { Tail=tail;
    Test.MarkTail(false);
    IfTrue.MarkTail(tail);
    if(IfFalse!=null) IfFalse.MarkTail(tail);
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

  public override void Emit(CodeGenerator cg)
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
    if(Tail) cg.EmitReturn();
  }

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

  public override void Emit(CodeGenerator cg)
  { Slot pair = cg.AllocLocalTemp(typeof(Pair));
    cg.EmitList(Items);
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
#endregion

#region LiteralNode
public sealed class LiteralNode : Node
{ public LiteralNode(object value) { Value=value; }
  public override void Emit(CodeGenerator cg)
  { cg.EmitConstantObject(Value);
    if(Tail) cg.EmitReturn();
  }
  public override object Evaluate() { return Value; }
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

  public override void Emit(CodeGenerator cg)
  { for(int i=0; i<Inits.Length; i++)
      if(Inits[i]!=null)
      { Inits[i].Emit(cg);
        cg.EmitSet(Names[i]);
      }
      else if(Options.Debug)
      { cg.EmitFieldGet(typeof(Binding), "Unbound");
        cg.EmitSet(Names[i]);
      }
    Body.Emit(cg);
    for(int i=0; i<Names.Length; i++) cg.Namespace.RemoveSlot(Names[i]);
  }

  public override void MarkTail(bool tail)
  { foreach(Node n in Inits) if(n!=null) n.MarkTail(false);
    Body.MarkTail(tail);
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

  public override void Emit(CodeGenerator cg)
  { TopLevel.Current.SetModule(Name, ModuleGenerator.Generate(this));
    cg.EmitConstantObject(Symbol.Get(Name));
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

  public override void Emit(CodeGenerator cg)
  { Value.Emit(cg);
    cg.ILG.Emit(OpCodes.Dup);
    cg.EmitSet(Name);
    if(Tail) cg.EmitReturn();
  }

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

  public override void Emit(CodeGenerator cg)
  { cg.EmitGet(Name);
    if(Options.Debug)
    { cg.EmitString(Name.String);
      cg.EmitCall(typeof(Ops), "CheckVariable");
    }
    if(Tail) cg.EmitReturn();
  }

  public Name Name;
}
#endregion

} // namespace NetLisp.Backend
