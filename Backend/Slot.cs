using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region Slot
public abstract class Slot
{ public abstract Type Type { get; }

  public abstract void EmitGet(CodeGenerator cg);
  public abstract void EmitGetAddr(CodeGenerator cg);

  public abstract void EmitSet(CodeGenerator cg);
  public virtual void EmitSet(CodeGenerator cg, Slot val) { val.EmitGet(cg); EmitSet(cg); }
}
#endregion

#region ArgSlot
public sealed class ArgSlot : Slot
{ public ArgSlot(MethodBuilder mb, int index, string name) : this(mb, index, name, typeof(object)) { }
  public ArgSlot(MethodBuilder mb, int index, string name, Type type)
    : this(mb, mb.DefineParameter(index+1, ParameterAttributes.None, name), type) { }
  public ArgSlot(MethodBase mb, ParameterBuilder parameterBuilder, Type type)
  { builder   = parameterBuilder;
    isStatic  = mb.IsStatic;
    this.type = type;
  }

  public override Type Type { get { return type; } }

  public override void EmitGet(CodeGenerator cg) { cg.EmitArgGet(builder.Position-1); }
  public override void EmitGetAddr(CodeGenerator cg) { cg.EmitArgGetAddr(builder.Position-1); }
  public override void EmitSet(CodeGenerator cg) { cg.EmitArgSet(builder.Position-1); }

  ParameterBuilder builder;
  Type type;
  bool isStatic;
}
#endregion

#region EnvironmentSlot
public sealed class EnvironmentSlot : Slot
{ public EnvironmentSlot(int depth, int pos) { this.depth=depth; this.pos=pos; }

  public override Type Type { get { return typeof(object); } }

  public override void EmitGet(CodeGenerator cg)
  { cg.EmitArgGet(0);
    for(int i=0; i<depth; i++) cg.EmitFieldGet(typeof(LocalEnvironment), "Parent");
    cg.EmitFieldGet(typeof(LocalEnvironment), "Values");
    cg.EmitInt(pos);
    cg.ILG.Emit(OpCodes.Ldelem_Ref);
  }
  
  public override void EmitGetAddr(CodeGenerator cg)
  { cg.EmitArgGet(0);
    for(int i=0; i<depth; i++) cg.EmitFieldGet(typeof(LocalEnvironment), "Parent");
    cg.EmitFieldGet(typeof(LocalEnvironment), "Values");
    cg.EmitInt(pos);
    cg.ILG.Emit(OpCodes.Ldelema);
  }

  public override void EmitSet(CodeGenerator cg)
  { Slot temp = cg.AllocLocalTemp(typeof(object));
    temp.EmitSet(cg);
    EmitSet(cg, temp);
    cg.FreeLocalTemp(temp);
  }

  public override void EmitSet(CodeGenerator cg, Slot val)
  { cg.EmitArgGet(0);
    for(int i=0; i<depth; i++) cg.EmitFieldGet(typeof(LocalEnvironment), "Parent");
    cg.EmitFieldGet(typeof(LocalEnvironment), "Values");
    cg.EmitInt(pos);
    val.EmitGet(cg);
    cg.ILG.Emit(OpCodes.Stelem_Ref);
  }

  int pos, depth;
}
#endregion

#region FieldSlot
public sealed class FieldSlot : Slot
{ public FieldSlot(FieldInfo fi) { Info=fi; }
  public FieldSlot(Slot instance, FieldInfo fi) { Instance=instance; Info=fi; }

  public override Type Type { get { return Info.FieldType; } }

  public override void EmitGet(CodeGenerator cg)
  { if(Instance!=null) Instance.EmitGet(cg);
    cg.EmitFieldGet(Info);
  }

  public override void EmitGetAddr(CodeGenerator cg)
  { if(Instance!=null) Instance.EmitGet(cg);
    cg.EmitFieldGetAddr(Info);
  }

  public override void EmitSet(CodeGenerator cg)
  { if(Instance==null) cg.EmitFieldSet(Info);
    Slot temp = cg.AllocLocalTemp(Info.FieldType);
    temp.EmitSet(cg);
    EmitSet(cg, temp);
    cg.FreeLocalTemp(temp);
  }
  public override void EmitSet(CodeGenerator cg, Slot val)
  { if(Instance!=null) Instance.EmitGet(cg);
    val.EmitGet(cg);
    cg.EmitFieldSet(Info);
  }

  public FieldInfo Info;
  public Slot Instance;
}
#endregion

#region TopLevelSlot
public sealed class TopLevelSlot : Slot
{ public override Type Type { get { return typeof(TopLevel); } }

  public override void EmitGet(CodeGenerator cg) { cg.EmitFieldGet(typeof(TopLevel), "Current"); }
  public override void EmitGetAddr(CodeGenerator cg) { cg.EmitFieldGetAddr(typeof(TopLevel), "Current"); }
  public override void EmitSet(CodeGenerator cg) { cg.EmitFieldSet(typeof(TopLevel), "Current"); }
}
#endregion

#region LocalSlot
public sealed class LocalSlot : Slot
{ public LocalSlot(LocalBuilder lb) { builder = lb; }
  public LocalSlot(LocalBuilder lb, string name)
  { builder = lb; 
    if(Options.Debug) lb.SetLocalSymInfo(name);
  }
  
  public override Type Type { get { return builder.LocalType; } }

  public override void EmitGet(CodeGenerator cg) { cg.ILG.Emit(OpCodes.Ldloc, builder); }
  public override void EmitGetAddr(CodeGenerator cg) { cg.ILG.Emit(OpCodes.Ldloca, builder); }
  public override void EmitSet(CodeGenerator cg) { cg.ILG.Emit(OpCodes.Stloc, builder); }

  LocalBuilder builder;
}
#endregion

#region NamedFrameSlot
public sealed class NamedFrameSlot : Slot
{ public NamedFrameSlot(Slot frame, string name) { Frame=frame; Name=name; }

  public override Type Type { get { return typeof(object); } }

  public override void EmitGet(CodeGenerator cg)
  { SetupBinding(cg);
    Binding.EmitGet(cg);
    if(Options.Debug) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitFieldGet(typeof(Binding), "Value");
  }
  
  public override void EmitGetAddr(CodeGenerator cg)
  { SetupBinding(cg);
    Binding.EmitGet(cg);
    if(Options.Debug) cg.EmitCall(typeof(Ops), "CheckBinding");
    cg.EmitFieldGetAddr(typeof(Binding), "Value");
  }

  // FIXME: check for unbound variables
  public override void EmitSet(CodeGenerator cg)
  { Slot temp = cg.AllocLocalTemp(typeof(object));
    temp.EmitSet(cg);
    EmitSet(cg, temp);
    cg.FreeLocalTemp(temp);
  }

  public override void EmitSet(CodeGenerator cg, Slot val)
  { SetupBinding(cg);
    Binding.EmitGet(cg);
    val.EmitGet(cg);
    cg.EmitFieldSet(typeof(Binding), "Value");
  }

  public Slot Frame, Binding;
  public string Name;

  void SetupBinding(CodeGenerator cg)
  { if(Binding==null)
    { if(TopLevel.Current!=null) Binding = cg.TypeGenerator.GetConstant(TopLevel.Current.GetBinding(Name));
      else Binding = cg.TypeGenerator.GetConstant(new Binding(Name));
    }
  }
}
#endregion

#region StaticSlot
public sealed class StaticSlot : Slot
{ public StaticSlot(FieldInfo field) { this.field=field; }

  public override Type Type { get { return field.FieldType; } }

  public override void EmitGet(CodeGenerator cg) { cg.EmitFieldGet(field); }
  public override void EmitGetAddr(CodeGenerator cg) { cg.EmitFieldGetAddr(field); }
  public override void EmitSet(CodeGenerator cg) { cg.EmitFieldSet(field); }

  FieldInfo field;
}
#endregion

#region ThisSlot
public sealed class ThisSlot : Slot
{ public ThisSlot(Type type) { this.type=type; }

  public override Type Type { get { return type; } }

  public override void EmitGet(CodeGenerator cg) { cg.ILG.Emit(OpCodes.Ldarg_0); }
  public override void EmitGetAddr(CodeGenerator cg) { cg.ILG.Emit(OpCodes.Ldarga, 0); }
  public override void EmitSet(CodeGenerator cg) { cg.ILG.Emit(OpCodes.Starg, 0); }
  
  Type type;
}
#endregion

} // namespace NetLisp.Backend
