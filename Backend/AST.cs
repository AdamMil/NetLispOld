using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

public enum Scope : byte { Free, Local, Global, Temporary }

public sealed class Name
{ public Name(string name) { String=name; Scope=Scope.Free; }
  public Name(string name, Scope scope) { String=name; Scope=scope; }

  public override int GetHashCode() { return String.GetHashCode(); }

  public string String;
  public Scope  Scope;
}

public sealed class AST
{ public static Node Create(object obj)
  { Node node = Parse(obj);
    node.Preprocess();
    return node;
  }

  public static long NextIndex { get { lock(indexLock) return index++; } }

  static Node Parse(object obj, bool tail)
  { Symbol sym = obj as Symbol;
    if(sym!=null) return new VariableNode(sym.Name);

    Pair pair = obj as Pair;
    if(pair==null) return new LiteralNode(obj);

    sym = pair.Car as Symbol;
    if(sym!=null)
      switch(sym.Name)
      { case "if":
        { if(Modules.Builtins.length(pair)<3) throw new Exception("too few for if"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          return new IfNode(Parse(pair.Car), Parse(Ops.FastCadr(pair)), ParseBody(Ops.FastCddr(pair) as Pair));
        }
        case "lambda":
        { if(Modules.Builtins.length(pair)<3) throw new Exception("too few for lambda"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          bool hasList;
          return new LambdaNode(ParseLambaList((Pair)pair.Car, out hasList), hasList, ParseBody((Pair)pair.Cdr));
        }
        case "quote":
        { if(Modules.Builtins.length(pair)!=2) throw new Exception("wrong number for quote"); // FIXME: ex
          return Quote(Ops.FastCadr(pair));
        }
        case "set!":
        { if(Modules.Builtins.length(pair)!=3) throw new Exception("wrong number for set!"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          sym = pair.Car as Symbol;
          if(sym==null) throw new Exception("set must set symbol"); // FIXME: SyntaxException
          return new SetNode(sym.Name, ParseBody((Pair)pair.Cdr));
        }
        case "define":
        { if(Modules.Builtins.length(pair)!=3) throw new Exception("wrong number for define"); // FIXME: ex
          pair = (Pair)pair.Cdr;
          sym = pair.Car as Symbol;
          if(sym!=null) return new DefineNode(sym.Name, ParseBody((Pair)pair.Cdr)); // (define name value)
          Pair names = (Pair)pair.Car;
          if(names.Cdr is Symbol) // (define (name . list) body ...)
            return new DefineNode(((Symbol)names.Car).Name,
                                  new LambdaNode(new string[] { ((Symbol)names.Cdr).Name },
                                                 true, ParseBody((Pair)pair.Cdr)));
          else // define (name a0 a1 ...) body ...)
          { bool hasList;
            return new DefineNode(sym.Name, new LambdaNode(ParseLambaList((Pair)names.Cdr, out hasList),
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
  public abstract object Evaluate();

  public virtual void Preprocess()
  { MarkTail();
  }
  
  public bool Tail;
  
  protected abstract void MarkTail();
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

  public readonly Node[] Forms;
  
  protected override void MarkTail() { Forms[Forms.Length-1].MarkTail(); }
}

public sealed class CallNode : Node
{ public CallNode(Node func, Node[] args) { Function=func; Args=args; }

  public override void Emit(CodeGenerator cg)
  { Function.Emit(cg);
    cg.EmitObjectArray(Args);
    cg.EmitCall(typeof(Ops), "Call", new Type[] { typeof(object), typeof(object[]) });
  }

  public override object Evaluate()
  { ICallable func = Function.Evaluate() as ICallable;
    if(func==null) throw new Exception("not a function"); // FIXME: use other exception
    
    object[] args = new object[Args.Length];
    for(int i=0; i<args.Length; i++) args[i] = Args[i].Evaluate();
    return func.Call(args);
  }

  public readonly Node Function;
  public readonly Node[] Args;

  protected override void MarkTail() { }
}

public sealed class DefineNode : Node
{ public DefineNode(string name, Node value) { Name=new Name(name, Scope.Global); Value=value; }

  public override void Emit(CodeGenerator cg)
  { cg.EmitFieldGet(typeof(Frame), "Current");
    cg.EmitString(Name.String);
    Value.Emit(cg);
    cg.EmitCall(typeof(Frame), "BindGlobal");
  }

  public override object Evaluate()
  { object value = Value.Evaluate();
    Frame.Current.BindGlobal(Name.String, value);
    return value;
  }

  public readonly Name Name;
  public readonly Node Value;
  
  protected override void MarkTail() { Value.MarkTail(); }
}

public sealed class IfNode : Node
{ public IfNode(Node test, Node iftrue, Node iffalse) { Test=test; IfTrue=iftrue; IfFalse=iffalse; }

  public override void Emit(CodeGenerator cg)
  { Label end=cg.ILG.DefineLabel(), falselbl=cg.ILG.DefineLabel();
    cg.EmitIsTrue(Test);
    cg.ILG.Emit(OpCodes.Brfalse, falselbl);
    IfTrue.Emit(cg);
    cg.ILG.Emit(OpCodes.Br, end);
    cg.ILG.MarkLabel(falselbl);
    cg.EmitExpression(IfFalse);
    cg.ILG.MarkLabel(end);
  }

  public override object Evaluate()
  { if(Ops.IsTrue(Test.Evaluate())) return IfTrue.Evaluate();
    else if(IfFalse!=null) return IfFalse.Evaluate();
    else return null;
  }

  public readonly Node Test, IfTrue, IfFalse;
  
  protected override void MarkTail()
  { IfTrue.MarkTail();
    if(IfFalse!=null) IfFalse.MarkTail();
  }
}

public sealed class LambdaNode : Node
{ public LambdaNode(string[] names, bool hasList, Node body)
  { Parameters = new Name[names.Length];
    for(int i=0; i<names.Length; i++) Parameters[i] = new Name(names[i], Scope.Local);
    this.names = names;

    Body=body; HasList=hasList;
  }

  public override void Emit(CodeGenerator cg)
  { index = AST.NextIndex;
    CodeGenerator impl = MakeImplMethod(cg);
    
    string[] names = new string[Parameters.Length];
    for(int i=0; i<names.Length; i++) names[i] = Parameters[i].String;

    cg.EmitStringArray(names);
    cg.EmitBool(HasList);
    if(Inherit==null)
    { cg.ILG.Emit(OpCodes.Ldnull); // create delegate
      cg.ILG.Emit(OpCodes.Ldftn, (MethodInfo)impl.MethodBase);
      cg.EmitNew((ConstructorInfo)typeof(CallTargetN).GetMember(".ctor")[0]); // FIXME: make this more portable
      cg.EmitNew(typeof(CompiledFunctionN), new Type[] { typeof(string[]), typeof(bool), typeof(CallTargetN) });
    }
    else
    { Type type = impl.TypeGenerator.TypeBuilder;
      cg.EmitNew(type, new Type[] { typeof(string[]), typeof(bool) });
      for(int i=0; i<Inherit.Length; i++)
      { cg.ILG.Emit(OpCodes.Dup);
        cg.Namespace.GetSlotForGet(Inherit[i]).EmitGet(cg);
        cg.EmitFieldSet(((FieldSlot)impl.Namespace.GetLocalSlot(Inherit[i])).Info);
      }
    }
  }

  public override object Evaluate() { return new LambdaFunction(Frame.Current, names, HasList, Body); }

  public readonly Name[] Parameters, Inherit;
  public readonly Node Body;
  public readonly bool HasList;

  protected override void MarkTail() { Body.MarkTail(); }

  CodeGenerator MakeImplMethod(CodeGenerator cg)
  { CodeGenerator icg;
    Slot[] closedSlots=null;
    if(Inherit==null || Inherit.Length==0)
      icg = cg.TypeGenerator.DefineMethod("lambda$" + index, typeof(object), new Type[] { typeof(object[]) });
    else
    { closedSlots = new Slot[Inherit.Length];
      TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                          "cfunc$"+index, typeof(CompiledFunction));
      for(int i=0; i<Inherit.Length; i++) closedSlots[i] = tg.DefineField("cv$"+AST.NextIndex, typeof(object));

      CodeGenerator ccg = tg.DefineChainedConstructor(typeof(CompiledFunction).GetConstructors()[0]);
      ccg.EmitReturn();
      ccg.Finish();

      icg = tg.DefineMethodOverride(typeof(CompiledFunction), "DoCall", true);
    }

    LocalNamespace ns = new LocalNamespace(cg.Namespace, icg);
    icg.Namespace = ns;
    ns.SetArgs(Parameters, icg, new ArgSlot((MethodBuilder)icg.MethodBase, 0, "$names", typeof(object[])));
    if(Inherit!=null) ns.AddClosedVars(Inherit, closedSlots);
    Body.Emit(icg);
    icg.EmitReturn();
    icg.Finish();
    if(Inherit!=null) icg.TypeGenerator.FinishType();
    return icg;
  }

  readonly string[] names;
  long index;
}

public sealed class ListNode : Node
{ public ListNode(Node[] items, Node dot) { Items=items; Dot=dot; }

  public override void Emit(CodeGenerator cg)
  { Slot pair = cg.AllocLocalTemp(typeof(Pair));
    MethodInfo cons = typeof(Modules.Builtins).GetMethod("cons");
    cg.EmitExpression(Items[Items.Length-1]);
    cg.EmitExpression(Dot);
    cg.EmitCall(cons);
    pair.EmitSet(cg);
    for(int i=Items.Length-2; i>=0; i--)
    { Items[i].Emit(cg);
      pair.EmitGet(cg);
      cg.EmitCall(cons);
      pair.EmitSet(cg);
    }
    pair.EmitGet(cg);
    cg.FreeLocalTemp(pair);
  }

  public override object Evaluate()
  { object obj = Dot==null ? null : Dot.Evaluate();
    for(int i=Items.Length-1; i>=0; i--) obj = Modules.Builtins.cons(Items[i].Evaluate(), obj);
    return obj;
  }
  
  public readonly Node[] Items;
  public readonly Node Dot;
}

public sealed class LiteralNode : Node
{ public LiteralNode(object value) { Value=value; }
  public override void Emit(CodeGenerator cg) { cg.EmitConstant(Value); }
  public override object Evaluate() { return Value; }
  public readonly object Value;
}

public sealed class Options
{ private Options() { }
  public static bool Debug, Optimize;
}

public sealed class SetNode : Node
{ public SetNode(string name, Node value) { Name=new Name(name); Value=value; }

  public override void Emit(CodeGenerator cg)
  { Value.Emit(cg);
    cg.ILG.Emit(OpCodes.Dup);
    cg.EmitSet(Name);
  }

  public override object Evaluate()
  { object value = Value.Evaluate();
    Frame.Current.Set(Name.String, value);
    return value;
  }

  public readonly Name Name;
  public readonly Node Value;
  
  protected override void MarkTail() { Value.MarkTail(); }
}

public sealed class VariableNode : Node
{ public VariableNode(string name) { Name=new Name(name); }
  public override void Emit(CodeGenerator cg) { cg.EmitGet(Name); }
  public override object Evaluate() { return Frame.Current.Get(Name.String); }
  public readonly Name Name;
}

} // namespace NetLisp.Backend
