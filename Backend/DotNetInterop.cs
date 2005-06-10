using System;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region FieldWrapper
public abstract class FieldWrapper : IProcedure
{ public int MinArgs { get { return 1; } }
  public int MaxArgs { get { return 2; } }
  public abstract object Call(object[] args);
}
#endregion

#region FunctionWrapper
public abstract class FunctionWrapper : IProcedure
{ public FunctionWrapper(IntPtr method)
  { methodPtr  = method;
    paramArray = pi.Length>0 && pi[pi.Length-1].IsDefined(typeof(ParamArrayAttribute), false);
    numNP  = paramArray ? pi.Length-1 : pi.Length;
    ptypes = pi.Length==0 ? Type.EmptyTypes : new Type[pi.Length];

    int min=-1, max;

    for(int i=0; i<ptypes.Length; i++)
    { ptypes[i] = pi[i].ParameterType;
      if(pi[i].IsOptional)
      { if(min==-1)
        { min = i;
          defaults = new object[ptypes.Length - i - (paramArray ? 1 : 0)];
        }
        defaults[i-min] = pi[i].DefaultValue;
      }
    }

    if(min==-1) min=numNP;
    max = paramArray ? -1 : numNP;
  }

  public abstract int MinArgs { get; }
  public abstract int MaxArgs { get; }

  public abstract object Call(object[] args);
  public abstract Conversion TryMatch(object[] args, Type[] types);

  protected static Conversion TryMatch(object[] args, Type[] types, Type[] ptypes, int numNP, bool paramArray)
  { if(args.Length<numRP) return Conversion.None; // check number of required parameters
    Conversion ret=Conversion.Identity;

    // check types of all normal (non-paramarray) parameters
    for(int i=0; i<numNP; i++)
    { Conversion conv = Ops.ConvertTo(types[i], ptypes[i]);
      if(conv==Conversion.None) return conv;
      if(conv<ret) ret=conv;
    }

    if(paramArray)
    { Type etype = ptypes[numNP].GetElementType();

      if(args.Length==ptypes.Length)
      { // check if final argument is an array already
        Conversion conv = Ops.ConvertTo(types[numNP], ptypes[numNP]);
        if(conv==Conversion.Identity || conv==Conversion.Reference) return conv | Conversion.RefAPA;

        // otherwise check that the remaining arguments can be converted to the member type
        Conversion pac = Conversion.SafeAPA;
        if(types[numNP].IsArray)
        { Array arr  = (Array)args[numNP];
          for(int i=0; i<items.Length; i++)
          { object obj = arr.GetValue(i);
            Conversion conv = Ops.ConvertTo(obj==null ? null : obj.GetType(), etype);
            if(conv==Conversion.None) goto notCPA;
            else if(conv==Conversion.Unsafe) pac = Conversion.UnsafeAPA;
          }
          if(Conversion.Reference < ret) ret = Conversion.Reference;
          return ret | pac;
        }
      }

      // check if extra parameters can be converted to the 
      notCPA:
      for(int i=lastNP; i<args.Length; i++)
      { Conversion conv = Ops.ConvertTo(types[i], etype);
        if(conv==Conversion.None) return conv;
        if(conv<ret) ret=conv;
      }
    }

    return conv;
  }

  readonly IntPtr methodPtr;
}
#endregion

#region ReflectedField
public sealed class ReflectedField : Primitive
{ public ReflectedField(string name) : base("#<field '"+name+"'>", 1, 2) { FieldName=name; }

  public readonly string FieldName;

  public override object Call(object[] args)
  { CheckArity(args);

    object inst = args[0];
    if(inst==null) throw new ArgumentNullException("field '"+FieldName+"': instance cannot be null");

    FieldInfo fi = inst.GetType().GetField(FieldName);
    if(fi==null) throw new Exception("Object "+Ops.TypeName(inst)+" has no field "+FieldName);

    if(args.Length==1) return fi.GetValue(inst);
    else { fi.SetValue(inst, args[1]); return args[1]; }
  }
}
#endregion

#region ReflectedMethod
public sealed class ReflectedMethod : Primitive
{ public ReflectedMethod(string name) : base("#<method '"+name+"'>", 1, -1) { MethodName=name; }

  public readonly string methodname;

  public override object Call(object[] args)
  { CheckArity(args);

    object inst = args[0];
    if(inst==null) throw new ArgumentNullException("property '"+methodname+"': instance cannot be null");

    if(hash==null) hash = new HybridDictionary();
    FunctionWrapper[] funcs = (FunctionWrapper[])hash[inst.GetType()]; // TODO: thread safety?

    if(funcs==null)
    { ArrayList list = new ArrayList();
      foreach(MethodInfo mi in inst.GetType().GetMethods())
        if(mi.Name==MethodName) list.Add(Interop.MakeFunctionWrapper(mi));
      hash[inst.GetType()] = funcs = (FunctionWrapper)list.ToArray(typeof(FunctionWrapper));
    }

    return Interop.Call(funcs, args);
  }

  HybridDictionary hash;
}
#endregion

#region ReflectedProperty
public sealed class ReflectedProperty : Primitive
{ public ReflectedProperty(string name) : base("#<property '"+name+"'>", 1, -1) { PropertyName=name; }

  public readonly string PropertyName;

  public override object Call(object[] args)
  { CheckArity(args);

    object inst = args[0];
    if(inst==null) throw new ArgumentNullException("property '"+PropertyName+"': instance cannot be null");

    if(hash==null) hash = new HybridDictionary();
    FunctionWrapper[] funcs = (FunctionWrapper[])hash[inst.GetType()]; // TODO: thread safety?

    if(funcs==null)
    { PropertyInfo pi = inst.GetType().GetProperty(PropertyName);
      if(pi==null) throw new Exception("Object "+Ops.TypeName(inst)+" has no property "+PropertyName);

      hash[inst.GetType()] = funcs = new FunctionWrapper[pi.CanRead && pi.CanWrite ? 2 : 1];
      int idx=0;
      if(pi.CanRead)  funcs[idx++] = Interop.MakeFunctionWrapper(pi.GetGetMethod());
      if(pi.CanWrite) funcs[idx]   = Interop.MakeFunctionWrapper(pi.GetSetMethod());
    }

    return Interop.Call(funcs, args);
  }

  HybridDictionary hash;
}
#endregion

#region Interop
public sealed class Interop
{ 
  #region Import
  public static void Import(string typename)
  { Type type = Type.GetType(typename);
    if(type==null) throw new Exception("no such type:"+typename);
    
    foreach(FieldInfo fi in type.GetFields())
    { string nameBase = fi.IsStatic ? ":"+fi.DeclaringType.Name+"." : ":",
             getName  = nameBase+"get/"+fi.Name, setName = nameBase+"set/"+fi.Name;

      object cur;
      if(fi.IsStatic || !TopLevel.Current.Get(getName, out cur) || !(cur is ReflectedField) ||
         ((ReflectedField)cur).FieldName!=fi.Name)
        TopLevel.Current.Bind(getName, fi.IsStatic ? MakeGetWrapper(fi) : new ReflectedField(fi.Name));

      if(fi.IsStatic && !fi.IsInitOnly && !fi.IsLiteral) TopLevel.Current.Bind(setName, MakeSetWrapper(fi));
    }

    foreach(PropertyInfo pi in type.GetProperties())
    { object cur;

      if(pi.CanRead)
      { MethodInfo mi = pi.GetGetMethod();
        string name = (mi.IsStatic ? ":"+pi.DeclaringType.Name+".get/" : ":get/");
        if(mi.IsStatic || !TopLevel.Current.Get(name, out cur) || !(cur is ReflectedMethod) ||
           ((ReflectedProperty)cur).PropertyName != mi.Name)
        TopLevel.Current.Bind(name, mi.IsStatic ? MakeMethodWrapper(mi) : new ReflectedProperty(pi));
      }

      if(pi.CanWrite)
      { MethodInfo mi = pi.GetSetMethod();
        string name = (mi.IsStatic ? ":"+pi.DeclaringType.Name+".set/" : ":set/");
        if(mi.IsStatic || !TopLevel.Current.Get(name, out cur) || !(cur is ReflectedMethod) ||
           ((ReflectedProperty)cur).PropertyName != mi.Name)
        TopLevel.Current.Bind(name, mi.IsStatic ? MakeMethodWrapper(mi) : new ReflectedProperty(pi));
      }
    }

    foreach(MethodInfo mi in type.GetMethods())
    { string name = (fi.IsStatic ? ":"+fi.DeclaringType.Name+"." : ":") + mi.Name;

      object cur;
      if(mi.IsStatic || !TopLevel.Current.Get(getName, out cur) || !(cur is ReflectedMethod) ||
         ((ReflectedMethod)cur).MethodName!=mi.Name)
        TopLevel.Current.Bind(getName, mi.IsStatic ? MakeFunctionWrapper(mi) : new ReflectedMethod(mi.Name));
    }
  }
  #endregion

  #region Signature
  sealed class Signature
  { public Signature(MethodInfo mi)
    { ParameterInfo[] pi = mi.GetParameters();

      Return     = mi.ReturnType;
      Params     = new Type[pi.Length];
      ParamArray = pi.Length>0 && pi[pi.Length-1].IsDefined(typeof(ParamArrayAttribute), false);

      for(int i=0,req=-1; i<Params.Length; i++)
      { Params[i] = pi[i].ParameterType;
        if(pi[i].IsOptional)
        { if(req==-1)
          { req = i;
            Defaults = new object[pi.Length - i - (paramArray ? 1 : 0)];
          }
          Defaults[i-req] = pi[i].DefaultValue;
        }
      }
    }

    public override bool Equals(object obj)
    { Signature other = (Signature)obj;
      if(Params.Length!=other.Params.Length || ParamArray!=other.ParamArray ||
         (Return!=other.Return && (Return.IsValueType || other.Return.IsValueType)) ||
         (Defaults==null && other.Defaults!=null || Defaults!=null && other.Defaults==null ||
          Defaults!=null && Defaults.Length!=other.Defaults.Length))
        return false;
      for(int i=0; i<Params.Length; i++) if(Params[i] != other.Params[i]) return false;
      if(Defaults!=null)
        for(int i=0; i<Defaults.Length; i++) if(!object.Equals(Defaults[i], other.Defaults[i])) return false;
      return true;
    }

    public override int GetHashCode()
    { int hash=Return.GetHashCode();
      for(int i=0; i<Params.Length; i++) hash ^= Params[i].GetHashCode();
      return hash;
    }

    public Type     Return;
    public Type[]   Params;
    public object[] Defaults;
    public bool     ParamArray;
  }
  #endregion

  #region MakeGetWrapper
  static FieldWrapper MakeGetWrapper(FieldInfo fi)
  { TypeGenerator tg = SnippetMaker.Assembly.DefineType("fg$"+fi.Name, typeof(FieldWrapper));
    CodeGenerator cg = tg.DefineMethodOverride(typeof(FieldWrapper), "Call");
    cg.EmitArgGet(0);
    cg.EmitInt(0);
    cg.ILG.Emit(OpCodes.Ldelem_Ref);
    cg.ILG.Emit(fi.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, fi.DeclaringType);
    cg.EmitFieldGet(fi.DeclaringType, fi.Name);
    if(fi.FieldType.IsValueType) cg.ILG.Emit(OpCodes.Box, fi.FieldType);
    cg.EmitReturn();
    cg.Finish();
    return (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }
  #endregion

  #region MakeSignatureWrapper
  static Type MakeSignatureWrapper(MethodInfo mi)
  { Signature sig = new Signature(mi);
    Type type = (Type)sigs[sig];

    if(type==null)
    { TypeGenerator tg = SnippetMaker.Assembly.DefineType("sw$"+AST.NextIndex, typeof(FunctionWrapper));

      Slot ptypes =
        sig.Params.Length==0 ? null : tg.DefineStaticField(FieldAttributes.Private, "ptypes", typeof(Type[]));
      Slot defaults =
        sig.Defaults==null ? null : tg.DefineStaticField(FieldAttributes.Private, "defaults", typeof(object[]));
      Slot numnp = tg.DefineStaticField(FieldAttributes.Private, "numnp", typeof(int));
      Slot min=tg.DefineStaticField(FieldAttributes.Private, "min", typeof(int));
      Slot max=tg.DefineStaticField(FieldAttributes.Private, "max", typeof(int));
      Slot paramArray=tg.DefineStaticField(FieldAttributes.Private, "paramArray", typeof(bool));

      CodeGenerator cg = tg.GetInitializer();
      // ptypes
      cg.EmitNewArray(typeof(Type), sig.Params.Length);
      for(int i=0; i<sig.Params.Length; i++)
      { cg.ILG.Emit(OpCodes.Dup);
        cg.EmitInt(i);
        cg.EmitTypeOf(sig.Params[i]);
        cg.ILG.Emit(OpCodes.Stelem_Ref);
      }
      ptypes.EmitSet(cg);
      // defaults
      if(sig.Defaults!=null)
      { cg.EmitObjectArray(sig.Defaults);
        defaults.EmitSet(cg);
      }
      // numnp
      cg.EmitInt(sig.ParamArray ? sig.Params.Length-1 : sig.Params.Length);
      numnp.EmitSet(cg);
      // min
      cg.EmitInt(sig.Params.Length - (sig.Defaults==null ? 0 : sig.Defaults.Length) - (sig.ParamArray ? 1 : 0));
      min.EmitSet(cg);
      // max
      cg.EmitInt(sig.ParamArray ? -1 : sig.Params.Length);
      max.EmitSet(cg);

      // constructor
      cg = tg.DefineChainedConstructor(typeof(FunctionWrapper).GetConstructor(new Type[] { typeof(IntPtr) }));
      cg.EmitReturn();
      cg.Finish();

      // MinArgs
      cg = tg.DefinePropertyOverride("MinArgs");
    }
  }
  #endregion

  #region MakeSetWrapper
  static FieldWrapper MakeSetWrapper(FieldInfo fi)
  { TypeGenerator tg = SnippetMaker.Assembly.DefineType("fs$"+fi.Name, typeof(FieldWrapper));
    CodeGenerator cg = tg.DefineMethodOverride(typeof(FieldWrapper), "Call");

    cg.EmitArgGet(0);
    cg.EmitInt(0);
    cg.ILG.Emit(OpCodes.Ldelem_Ref);
    cg.ILG.Emit(fi.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, fi.DeclaringType);
    
    cg.EmitArgGet(1);
    cg.EmitTypeOf(fi.FieldType);
    cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(object), typeof(Type) });
    cg.EmitFieldSet(fi.DeclaringType, fi.Name);

    cg.ILG.Emit(OpCodes.Ldnull);
    cg.EmitReturn();
    cg.Finish();
    return (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }
  #endregion
  
  static readonly Hashtable sigs = new Hashtable();
}
#endregion

} // namespace NetLisp.Backend
