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
    Global  = parent==null || parent.Parent==null ? parent : parent.Parent;
    codeGen = cg;
  }

  // TODO: implement a way to free temporaries
  public virtual Slot AllocTemp(Type type) { throw new NotImplementedException(); }

  public bool Contains(string name) { return slots[name]!=null; }
  public Slot GetLocalSlot(string name) { return (Slot)slots[name]; } // does NOT make the slot!
  public Slot GetGlobalSlot(string name) { return Parent==null ? GetSlot(name) : Parent.GetGlobalSlot(name); }

  public Slot GetSlotForGet(string name)
  { Slot s = (Slot)slots[name];
    return s!=null ? s : Parent!=null ? Parent.GetSlotForGet(name) : GetSlot(name);
  }
  public Slot GetSlotForSet(string name) { return GetSlot(name); }

  public virtual void SetArgs(string[] names, int offset, MethodBase mb)
  { throw new NotSupportedException("SetArgs: "+GetType());
  }

  public Namespace Parent, Global;

  protected abstract Slot MakeSlot(string name);

  protected HybridDictionary slots = new HybridDictionary();
  protected CodeGenerator codeGen;

  Slot GetSlot(string name)
  { Slot ret = (Slot)slots[name];
    if(ret==null) slots[name] = ret = MakeSlot(name);
    return ret;
  }
}
#endregion

#region LocalNamespace
public sealed class LocalNamespace : Namespace
{ public LocalNamespace(Namespace parent, CodeGenerator cg) : base(parent, cg) { }

  public override void SetArgs(string[] names, int offset, MethodBase mb)
  { for(int i=0; i<names.Length; i++) slots[names[i]] = new ArgSlot((MethodBuilder)mb, i+offset, names[i]);
  }

  public void SetArgs(string[] names, CodeGenerator cg, Slot objArray)
  { if(names.Length==0) return;
    // TODO: this should be optimized out by not using object[] param arrays everywhere
    objArray.EmitGet(cg);
    for(int i=0; i<names.Length; i++)
    { if(i!=names.Length-1) cg.ILG.Emit(OpCodes.Dup);
      cg.EmitInt(i);
      cg.ILG.Emit(OpCodes.Ldelem_Ref);
      Slot slot = new LocalSlot(cg.ILG.DeclareLocal(typeof(object)), names[i]);
      slot.EmitSet(cg);
      slots[names[i]] = slot;
    }
  }

  protected override Slot MakeSlot(string name)
  { return new LocalSlot(codeGen.ILG.DeclareLocal(typeof(object)), name);
  }
}
#endregion

#region TopLevelNamespace
public sealed class TopLevelNamespace : Namespace
{ public TopLevelNamespace(CodeGenerator cg) : base(null, cg) { TopSlot = new TopLevelSlot(); }

  public override void SetArgs(string[] names, int offset, MethodBase mb)
  { foreach(string name in names) slots[name] = MakeSlot(name);
  }

  public TopLevelSlot TopSlot;

  protected override Slot MakeSlot(string name) { return new NamedFrameSlot(TopSlot, name); }
}
#endregion

#region EnvironmentNamespace
public sealed class EnvironmentNamespace : Namespace
{ public EnvironmentNamespace(Namespace parent, CodeGenerator cg, string[] names) : base(parent, cg)
  { this.names = names;
    for(int i=0; i<names.Length; i++) slots[names[i]] = new EnvironmentSlot(i);
  }

  public Slot GetSlot(int index) { return (Slot)slots[names[index]]; }

  protected override Slot MakeSlot(string name) { throw new NotSupportedException(); }

  string[] names;
}
#endregion

} // namespace NetLisp.Backend
