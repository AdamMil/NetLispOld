using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region Namespace
public abstract class Namespace
{ public Namespace(Namespace parent, CodeGenerator cg)
  { Parent  = parent;
    codeGen = cg;
  }

  // TODO: implement a way to free temporaries
  public virtual Slot AllocTemp(Type type) { throw new NotImplementedException(); }

  public Slot GetSlot(Name name) { return GetSlot(name, true); }
  public Slot GetSlot(Name name, bool makeIt)
  { if(name.Depth==Name.Global && Parent!=null) return Parent.GetSlot(name, true);
    Slot ret = (Slot)slots[name];
    if(ret==null)
    { if(Parent!=null) ret = Parent.GetSlot(name, false);
      if(ret==null && makeIt) slots[name] = ret = MakeSlot(name);
    }
    return ret;
  }

  public Namespace Parent;

  protected abstract Slot MakeSlot(Name name);

  protected HybridDictionary slots = new HybridDictionary();
  protected CodeGenerator codeGen;
}
#endregion

#region LocalNamespace
public sealed class LocalNamespace : Namespace
{ public LocalNamespace(Namespace parent, CodeGenerator cg) : base(parent, cg) { }

  protected override Slot MakeSlot(Name name)
  { if(name.Depth==Name.Local) return new LocalSlot(codeGen.ILG.DeclareLocal(typeof(object)), name.String);
    return new EnvironmentSlot(name.Depth, name.Index);
  }
}
#endregion

#region TopLevelNamespace
public sealed class TopLevelNamespace : Namespace
{ public TopLevelNamespace(CodeGenerator cg) : base(null, cg) { TopSlot = new TopLevelSlot(); }

  public TopLevelSlot TopSlot;

  protected override Slot MakeSlot(Name name) { return new NamedFrameSlot(TopSlot, name.String); }
}
#endregion

} // namespace NetLisp.Backend
