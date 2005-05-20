using System;
using System.Collections;
using System.Diagnostics;

namespace NetLisp.Backend
{

public sealed class AST
{ public static Node Create(object obj)
  { Symbol sym = obj as Symbol;
    if(sym!=null) return new SymbolNode(sym.Name);

    Pair pair = obj as Pair;
    if(pair==null) return new LiteralNode(obj);

    sym = pair.Car as Symbol;
    if(sym!=null)
      switch(sym.Name)
      { case "if":
        { if(Ops.Length(pair)<3) throw new Exception("too few for if"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          return new IfNode(Create(pair.Car), Create(Ops.FastCadr(pair)), ParseBody(Ops.FastCddr(pair) as Pair));
        }
        case "lambda":
        { if(Ops.Length(pair)<3) throw new Exception("too few for lambda"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          bool hasList;
          return new LambdaNode(ParseLambaList((Pair)pair.Car, out hasList), hasList, ParseBody((Pair)pair.Cdr));
        }
        case "let":
        { if(Ops.Length(pair)<3) throw new Exception("too few for let"); // FIXME: SyntaxException
          string[] names;
          Node[] values;
          pair = (Pair)pair.Cdr;
          ParseLetList((Pair)pair.Car, out names, out values);
          return new LetNode(names, values, ParseBody((Pair)pair.Cdr));
        }
        case "set!":
        { if(Ops.Length(pair)!=3) throw new Exception("wrong number for set!"); // FIXME: SyntaxException
          pair = (Pair)pair.Cdr;
          sym = pair.Car as Symbol;
          if(sym==null) throw new Exception("set must set symbol"); // FIXME: SyntaxException
          return new SetNode(sym.Name, ParseBody((Pair)pair.Cdr));
        }
      }

    Node func = Create(pair.Car);
    ArrayList args = new ArrayList();
    while(true)
    { pair = pair.Cdr as Pair;
      if(pair==null) break;
      args.Add(Create(pair.Car));
    }
    return new CallNode(func, (Node[])args.ToArray(typeof(Node)));
  }

  static BodyNode ParseBody(Pair start)
  { if(start==null) return null;
    ArrayList items = new ArrayList();
    while(start!=null)
    { items.Add(Create(start.Car));
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
  
  static void ParseLetList(Pair list, out string[] names, out Node[] values)
  { if(list==null)
    { names  = new string[0];
      values = new Node[0];
    }
    else
    { ArrayList nams=new ArrayList(), vals=new ArrayList();
      do
      { Pair pair = list.Car as Pair;
        if(pair==null) throw new Exception("expecting a list of lists"); // FIXME: SyntaxException
        if(Ops.Length(pair)!=2) throw new Exception("expecting a list of length 2");
        Symbol sym = pair.Car as Symbol;
        if(sym==null) throw new Exception("can only bind symbols"); // FIXME: SyntaxException
        nams.Add(sym.Name);
        vals.Add(Create(Ops.FastCadr(pair)));
        list = list.Cdr as Pair;
      } while(list!=null);
      names  = (string[])nams.ToArray(typeof(string));
      values = (Node[])vals.ToArray(typeof(Node));
    }
  }
}

public abstract class Node
{ //public abstract void Emit(CodeGenerator cg);
  public abstract object Evaluate(Frame frame);
}

public sealed class BodyNode : Node
{ public BodyNode(Node[] forms) { Forms=forms; }

  public override object Evaluate(Frame frame)
  { object ret=null;
    foreach(Node n in Forms) ret = n.Evaluate(frame);
    return ret;
  }

  public readonly Node[] Forms;
}

public sealed class CallNode : Node
{ public CallNode(Node func, Node[] args) { Function=func; Args=args; }

  public override object Evaluate(Frame frame)
  { Function func = Function.Evaluate(frame) as Function;
    if(func==null) throw new Exception("not a function"); // FIXME: use other exception
    
    object[] args = new object[Args.Length];
    for(int i=0; i<args.Length; i++) args[i] = Args[i].Evaluate(frame);
    return func.Call(args);
  }

  public readonly Node Function;
  public readonly Node[] Args;
}

public sealed class IfNode : Node
{ public IfNode(Node test, Node iftrue, Node iffalse) { Test=test; IfTrue=iftrue; IfFalse=iffalse; }

  public override object Evaluate(Frame frame)
  { if(Ops.IsTrue(Test.Evaluate(frame))) return IfTrue.Evaluate(frame);
    else if(IfFalse!=null) return IfFalse.Evaluate(frame);
    else return null;
  }

  public readonly Node Test, IfTrue, IfFalse;
}

public sealed class LambdaNode : Node
{ public LambdaNode(string[] names, bool hasList, Node body) { ParamNames=names; Body=body; HasList=hasList; }
  public override object Evaluate(Frame frame) { return new LambdaFunction(frame, ParamNames, HasList, Body); }

  public readonly string[] ParamNames;
  public readonly Node Body;
  public readonly bool HasList;
}

public sealed class LetNode : Node
{ public LetNode(string[] names, Node[] values, Node body) { Names=names; Values=values; Body=body; }

  public override object Evaluate(Frame frame)
  { Frame child = new Frame(frame);
    for(int i=0; i<Names.Length; i++) child.Bind(Names[i], Values[i].Evaluate(frame));
    return Body.Evaluate(child);
  }

  public readonly string[] Names;
  public readonly Node[] Values;
  public readonly Node Body;
}

public sealed class LiteralNode : Node
{ public LiteralNode(object value) { Value=value; }
  public override object Evaluate(Frame frame) { return Value; }
  public readonly object Value;
}

public sealed class SetNode : Node
{ public SetNode(string name, Node value) { Name=name; Value=value; }

  public override object Evaluate(Frame frame)
  { object value = Value.Evaluate(frame);
    frame.Set(Name, value);
    return value;
  }

  public readonly string Name;
  public readonly Node Value;
}

public sealed class SymbolNode : Node
{ public SymbolNode(string name) { Name=name; }
  public override object Evaluate(Frame frame) { return frame.Get(Name); }
  public readonly string Name;
}

} // namespace NetLisp.Backend
