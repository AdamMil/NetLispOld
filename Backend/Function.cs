using System;
using System.Diagnostics;

namespace NetLisp.Backend
{

public abstract class Function : ICallable
{ public Function(string[] paramNames, bool hasList) { ParamNames=paramNames; HasList=hasList; }
  public abstract object Call(params object[] args);
  public readonly string[] ParamNames;
  public readonly bool HasList;
  public bool Macro;
}

#region Compiled functions
public delegate object CallTarget1(object args);
public delegate object CallTargetN(params object[] args);

public abstract class CompiledFunction : Function
{ public CompiledFunction(string[] names, bool hasList) : base(names, hasList) { }

  public override object Call(params object[] args)
  { if(HasList)
    { int positional = ParamNames.Length-1;
      if(args.Length<positional) throw new Exception("too few arguments"); // FIXME: use other exception
      else if(args.Length!=positional)
      { object[] nargs = new object[ParamNames.Length];
        Array.Copy(args, nargs, positional);
        nargs[positional] = Ops.List2(positional, args);
      }
      else args[positional] = Modules.Builtins.cons(args[positional], null);
    }
    else if(args.Length!=ParamNames.Length) throw new Exception("wrong number of arguments"); // FIXME: use other exception

    return DoCall(args);
  }

  protected abstract object DoCall(object[] args);
}

public sealed class CompiledFunction1 : CompiledFunction
{ public CompiledFunction1(string[] names, bool hasList, CallTarget1 target) : base(names, hasList) { Target=target; }
  protected override object DoCall(object[] args) { return Target(args[0]); }
  CallTarget1 Target;
}

public sealed class CompiledFunctionN : CompiledFunction
{ public CompiledFunctionN(string[] names, bool hasList, CallTargetN target) : base(names, hasList) { Target=target; }
  protected override object DoCall(object[] args) { return Target(args); }
  CallTargetN Target;
}
#endregion

public sealed class LambdaFunction : Function
{ public LambdaFunction(Frame frame, string[] paramNames, bool hasList, Node body) : base(paramNames, hasList) { Frame=frame; Body=body; }

  public override object Call(params object[] args)
  { int positional;
    if(HasList)
    { positional = ParamNames.Length-1;
      if(args.Length<positional) throw new Exception("too few arguments"); // FIXME: use other exception
    }
    else
    { positional = ParamNames.Length;
      if(args.Length!=positional) throw new Exception("wrong number of arguments"); // FIXME: use other exception
    }

    Frame child = new Frame(Frame);
    for(int i=0; i<positional; i++) child.Bind(ParamNames[i], args[i]);
    if(HasList) child.Bind(ParamNames[positional], Ops.List2(positional, args));

    try
    { Frame.Current = child;
      return Body.Evaluate();
    }
    finally { Frame.Current = child.Parent; }
  }
  
  public Frame Frame;
  public Node Body;
}

} // namespace NetLisp.Backend