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

  public Slot GetSlot(Name name) { return GetSlot(name, true); }
  public Slot GetSlot(Name name, bool makeIt)
  { if(name.Depth==Name.Global && Parent!=null) return Parent.GetSlot(name, true);
    Slot ret = (Slot)slots[name];
    if(ret==null)
    { if(Parent!=null) ret = Parent.GetSlot(name, false);
      if(ret==null && makeIt)
      { ret = name.Depth==Name.Local ? codeGen.AllocLocalTemp(typeof(object)) : MakeSlot(name);
        slots[name] = ret = ret;
      }
    }
    return ret;
  }

  public void RemoveSlot(Name name)
  { Slot slot = (Slot)slots[name];
    if(name.Depth==Name.Local) codeGen.FreeLocalTemp(slot);
    slots.Remove(name);
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
  protected override Slot MakeSlot(Name name) { return new EnvironmentSlot(name.Depth, name.Index); }
}
#endregion

#region TopLevelNamespace
public sealed class TopLevelNamespace : Namespace
{ public TopLevelNamespace(CodeGenerator cg) : base(null, cg) { TopSlot = new TopLevelSlot(); }
  public TopLevelNamespace(CodeGenerator cg, Slot top) : base(null, cg) { TopSlot = top; }

  public readonly Slot TopSlot;

  protected override Slot MakeSlot(Name name) { return new NamedFrameSlot(TopSlot, name.String); }
}
#endregion

} // namespace NetLisp.Backend
