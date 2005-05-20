using System;
using System.Diagnostics;

namespace NetLisp.Backend
{

public abstract class Function
{ public Function(Symbol name, string[] paramNames, bool hasList)
  { Name=name; ParamNames=paramNames; HasList=hasList;
  }

  public string FuncName { get { return Name==null ? "<lambda>" : Name.Name; } }
  public abstract object Call(params object[] args);

  public readonly Symbol Name;
  public readonly string[] ParamNames;
  public readonly bool HasList;
}

public sealed class LambdaFunction : Function
{ public LambdaFunction(Frame frame, string[] paramNames, bool hasList, Node body) : base(null, paramNames, hasList)
  { Frame=frame; Body=body;
  }

  public override object Call(params object[] args)
  { Frame child = new Frame(Frame);
    int positional;
    if(HasList)
    { positional = ParamNames.Length-1;
      if(args.Length<positional) throw new Exception("too few arguments"); // FIXME: use other exception
    }
    else
    { positional = ParamNames.Length;
      if(args.Length!=positional) throw new Exception("wrong number of arguments"); // FIXME: use other exception
    }

    for(int i=0; i<positional; i++) child.Bind(ParamNames[i], args[i]);
    if(HasList) child.Bind(ParamNames[positional], Ops.List2(positional, args));
    return Body.Evaluate(child);
  }

  public readonly Frame Frame;
  public readonly Node Body;
}

} // namespace NetLisp.Backend