using System;
using NetLisp.Runtime;

namespace NetLisp.AST
{

public abstract class Node
{ public bool IsConstant
  { get { return (Flags&Flag.Constant)!=0; }
    set { if(value) Flags|=Flag.Constant; else Flags&=~Flag.Constant; }
  }

  public virtual object GetValue() { throw new NotSupportedException(); }
  public virtual void Optimize() { }

  public string ToCode()
  { System.Text.StringBuilder sb = new System.Text.StringBuilder();
    ToCode(sb, 0);
    return sb.ToString();
  }
  public abstract void ToCode(System.Text.StringBuilder sb, int indent);

  [Flags] enum Flag : byte { Constant=1 }
  Flag Flags;
}

public sealed class IdentifierNode : Node
{ public IdentifierNode(string name) { Name=name; }
  public override void ToCode(System.Text.StringBuilder sb, int indent) { sb.Append(Name); }
  public string Name;
}

public sealed class ListNode : Node
{ public ListNode(Node[] items, Node dot) { Items=items; Dot=dot; }

  public override void ToCode(System.Text.StringBuilder sb, int indent)
  { sb.Append('(');
    bool space=false;
    foreach(Node n in Items)
    { if(space) sb.Append(' ');
      else space=true;
      n.ToCode(sb, 0);
    }
    if(Dot!=null)
    { sb.Append(" . ");
      Dot.ToCode(sb, 0);
    }
    sb.Append(')');
  }

  public Node[] Items;
  public Node Dot;
}

public sealed class LiteralNode : Node
{ public LiteralNode(object value) { IsConstant=true; Value=value; }
  public override object GetValue() { return Value; }
  public override void ToCode(System.Text.StringBuilder sb, int indent) { sb.Append(Ops.Repr(Value)); }
  public object Value;
}

public sealed class QuoteNode : Node
{ public QuoteNode(Token type, Node node) { Type=type; Node=node; }
  
  public override void ToCode(System.Text.StringBuilder sb, int indent)
  { switch(Type)
    { case Token.Comma: sb.Append(','); break;
      case Token.BackQuote: sb.Append('`'); break;
      case Token.Quote: sb.Append('\''); break;
      case Token.Splice: sb.Append(",@"); break;
    }
    Node.ToCode(sb, 0);
  }

  public Node Node;
  public Token Type;
}

public sealed class VectorNode : Node
{ public VectorNode(Node[] items) { Items=items; }

  public override void ToCode(System.Text.StringBuilder sb, int indent)
  { sb.Append("#(");
    bool space=false;
    foreach(Node n in Items)
    { if(space) sb.Append(' ');
      else space=true;
      n.ToCode(sb, 0);
    }
    sb.Append(')');
  }

  public Node[] Items;
}

} // namespace NetLisp.AST