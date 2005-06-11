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
{ public FunctionWrapper(IntPtr method) { methodPtr  = method; }

  public abstract int MinArgs { get; }
  public abstract int MaxArgs { get; }

  public abstract object Call(object[] args);
  public abstract Conversion TryMatch(object[] args, Type[] types);

  protected static Conversion TryMatch(object[] args, Type[] types, Type[] ptypes, int numNP, int min, bool paramArray)
  { if(args.Length<min) return Conversion.None; // check number of required parameters
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

        conv = Ops.ConvertTo(types[numNP], etype);
        if(conv==Conversion.Identity || conv==Conversion.Reference) return conv;

        // otherwise check that the remaining arguments can be converted to the member type
        if(types[numNP].IsArray)
        { conv = Ops.ConvertTo(types[numNP].GetElementType(), etype);
          if(conv!=Conversion.None)
          { if(conv<ret) ret=conv;
            return conv | (conv==Conversion.Unsafe ? Conversion.UnsafeAPA : Conversion.SafeAPA);
          }
        }
      }

      // check if extra parameters can be converted to the 
      for(int i=numNP; i<args.Length; i++)
      { Conversion conv = Ops.ConvertTo(types[i], etype);
        if(conv==Conversion.None) return conv;
        if(conv<ret) ret=conv;
      }
    }

    return ret;
  }

  protected readonly IntPtr methodPtr;
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

  public readonly string MethodName;

  public override object Call(object[] args)
  { CheckArity(args);

    object inst = args[0];
    if(inst==null) throw new ArgumentNullException("property '"+MethodName+"': instance cannot be null");

    if(hash==null) hash = new HybridDictionary();
    FunctionWrapper[] funcs = (FunctionWrapper[])hash[inst.GetType()]; // TODO: thread safety?

    if(funcs==null)
    { ArrayList list = new ArrayList();
      foreach(MethodInfo mi in inst.GetType().GetMethods())
        if(mi.Name==MethodName) list.Add(Interop.MakeFunctionWrapper(mi));
      hash[inst.GetType()] = funcs = (FunctionWrapper[])list.ToArray(typeof(FunctionWrapper));
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
  #region Call
  public static object Call(FunctionWrapper[] funcs, object[] args)
  { if(funcs.Length==1) return funcs[0].Call(args);

    Conversion best=Conversion.None, bqual=Conversion.None;
    int besti=-1;

    Type[] types = new Type[args.Length];
    for(int i=0; i<args.Length; i++) types[i] = args[i]==null ? null : args[i].GetType();

    unsafe
    { Conversion *rets = stackalloc Conversion[funcs.Length];
      
      for(int i=0; i<funcs.Length; i++)
      { Conversion conv=funcs[i].TryMatch(args, types), qual=conv&Conversion.QualityMask;
        if(qual>bqual || qual==bqual && conv>best) { best=conv; qual=bqual; besti=i; }
        rets[i] = conv;
      }

      if(besti==-1) throw new ArgumentException("Unable to bind arguments");
      if(bqual!=Conversion.Identity)
        for(int i=besti+1; i<funcs.Length; i++)
          if(rets[i]==best) throw new ArgumentException("Ambiguous arguments");
    }
    
    return funcs[besti].Call(args);
  }
  #endregion

  #region Import
  public static void Import(string typename)
  { Type type=Type.GetType(typename);
    if(type==null) throw new Exception("no such type:"+typename);
    
    foreach(FieldInfo fi in type.GetFields()) ImportField(fi);
    foreach(PropertyInfo pi in type.GetProperties()) ImportProperty(pi);
    foreach(MethodInfo mi in type.GetMethods()) ImportMethod(mi);
  }
  #endregion

  #region Signature
  sealed class Signature
  { public Signature(MethodInfo mi)
    { ParameterInfo[] pi = mi.GetParameters();

      int so = mi.IsStatic ? 0 : 1;

      Convention = mi.CallingConvention;
      IsStatic   = mi.IsStatic;
      Return     = mi.ReturnType;
      Params     = new Type[pi.Length + so];
      ParamArray = pi.Length>0 && pi[pi.Length-1].IsDefined(typeof(ParamArrayAttribute), false);

      if(!IsStatic) Params[0] = mi.DeclaringType;
      for(int i=0,req=-1; i<Params.Length; i++)
      { Params[i + so] = pi[i].ParameterType;
        if(pi[i].IsOptional)
        { if(req==-1)
          { req = i;
            Defaults = new object[pi.Length - i - (ParamArray ? 1 : 0)];
          }
          Defaults[i-req] = pi[i].DefaultValue;
        }
      }
    }

    public override bool Equals(object obj)
    { Signature o = (Signature)obj;
      if(Params.Length!=o.Params.Length || ParamArray!=o.ParamArray || Return!=o.Return || IsStatic!=o.IsStatic || 
         Convention!=o.Convention || (Defaults==null && o.Defaults!=null || Defaults!=null && o.Defaults==null ||
                                      Defaults!=null && Defaults.Length!=o.Defaults.Length))
        return false;
      for(int i=0; i<Params.Length; i++) if(Params[i] != o.Params[i]) return false;
      if(Defaults!=null)
        for(int i=0; i<Defaults.Length; i++) if(!object.Equals(Defaults[i], o.Defaults[i])) return false;
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
    public CallingConventions Convention;
    public bool     ParamArray, IsStatic;
  }
  #endregion

  #region MakeFunctionWrapper
  public static FunctionWrapper MakeFunctionWrapper(MethodInfo mi)
  { IntPtr funcPtr = mi.MethodHandle.GetFunctionPointer();
    FunctionWrapper ret = (FunctionWrapper)funcs[funcPtr];
    if(ret==null)
      funcs[funcPtr] = ret = (FunctionWrapper)MakeSignatureWrapper(mi).GetConstructor(new Type[] { typeof(IntPtr) })
                                .Invoke(new object[] { funcPtr });
    return ret;
  }
  #endregion

  #region MakeGetWrapper
  static FieldWrapper MakeGetWrapper(FieldInfo fi)
  { IntPtr ptr = fi.FieldHandle.Value;
    FieldWrapper ret = (FieldWrapper)gets[ptr];
    if(ret==null)
    { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                          "fg"+AST.NextIndex+"$"+fi.Name, typeof(FieldWrapper));
      CodeGenerator cg = tg.DefineMethodOverride("Call", true);
      cg.EmitArgGet(0);
      cg.EmitInt(0);
      cg.ILG.Emit(OpCodes.Ldelem_Ref);
      cg.ILG.Emit(fi.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, fi.DeclaringType);
      cg.EmitFieldGet(fi.DeclaringType, fi.Name);
      if(fi.FieldType.IsValueType) cg.ILG.Emit(OpCodes.Box, fi.FieldType);
      cg.EmitReturn();
      cg.Finish();
      gets[ptr] = ret = (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
    }
    return ret;
  }
  #endregion

  #region MakeSetWrapper
  static FieldWrapper MakeSetWrapper(FieldInfo fi)
  { IntPtr ptr = fi.FieldHandle.Value;
    FieldWrapper ret = (FieldWrapper)sets[ptr];
    if(ret==null)
    { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                          "fs"+AST.NextIndex+"$"+fi.Name, typeof(FieldWrapper));
      CodeGenerator cg = tg.DefineMethodOverride("Call", true);

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
      sets[ptr] = ret = (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
    }
    return ret;
  }
  #endregion
  
  #region MakeSignatureWrapper
  static Type MakeSignatureWrapper(MethodInfo mi)
  { Signature sig = new Signature(mi);
    Type type = (Type)sigs[sig];

    if(type==null)
    { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                          "sw$"+AST.NextIndex, typeof(FunctionWrapper));
      Slot ptypes =
        sig.Params.Length==0 ? null : tg.DefineStaticField(FieldAttributes.Private, "ptypes", typeof(Type[]));
      Slot defaults =
        sig.Defaults==null ? null : tg.DefineStaticField(FieldAttributes.Private, "defaults", typeof(object[]));
      Slot _min=tg.DefineStaticField(FieldAttributes.Private, "min", typeof(int));
      Slot max=tg.DefineStaticField(FieldAttributes.Private, "max", typeof(int));
      Slot paramArray=tg.DefineStaticField(FieldAttributes.Private, "paramArray", typeof(bool));

      int numnp = sig.ParamArray ? sig.Params.Length-1 : sig.Params.Length;

      // initialize static variables
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
      // min
      int min = sig.Params.Length - (sig.Defaults==null ? 0 : sig.Defaults.Length) - (sig.ParamArray ? 1 : 0);
      cg.EmitInt(min);
      _min.EmitSet(cg);
      // max
      cg.EmitInt(sig.ParamArray ? -1 : sig.Params.Length);
      max.EmitSet(cg);

      // constructor
      cg = tg.DefineChainedConstructor(typeof(FunctionWrapper).GetConstructor(new Type[] { typeof(IntPtr) }));
      cg.EmitReturn();
      cg.Finish();

      // MinArgs
      cg = tg.DefinePropertyOverride("MinArgs", true);
      _min.EmitGet(cg);
      cg.EmitReturn();
      cg.Finish();
      
      // MaxArgs
      cg = tg.DefinePropertyOverride("MaxArgs", true);
      max.EmitGet(cg);
      cg.EmitReturn();
      cg.Finish();

      // object[] args, Type[] types, Type[] ptypes, int numNP, int min, bool paramArray
      cg = tg.DefineMethodOverride(tg.BaseType.GetMethod("TryMatch", BindingFlags.Public|BindingFlags.Instance), true);
      cg.EmitArgGet(0);
      cg.EmitArgGet(1);
      ptypes.EmitGet(cg);
      cg.EmitInt(numnp);
      _min.EmitGet(cg);
      cg.EmitBool(sig.ParamArray);
      cg.EmitCall(tg.BaseType.GetMethod("TryMatch", BindingFlags.Static|BindingFlags.NonPublic));
      cg.EmitReturn();
      cg.Finish();

      // Call
      cg = tg.DefineMethodOverride("Call", true);

      if(sig.Params.Length!=0)
      { Slot  iv = cg.AllocLocalTemp(typeof(int));
        Slot apa = sig.ParamArray ? cg.AllocLocalTemp(typeof(int)) : null; // int apa;

        if(sig.ParamArray)
        { Label endlbl=cg.ILG.DefineLabel(), is0=cg.ILG.DefineLabel(), is2=cg.ILG.DefineLabel(),
                  not0=cg.ILG.DefineLabel(), not2=cg.ILG.DefineLabel();
          Slot  conv = cg.AllocLocalTemp(typeof(Conversion));

          cg.EmitArgGet(0); // if(args.Length==ptypes.Length) {
          cg.ILG.Emit(OpCodes.Ldlen);
          cg.EmitInt(sig.Params.Length);
          cg.ILG.Emit(OpCodes.Bne_Un_S, endlbl);
          
          cg.EmitArgGet(0); // conv = Ops.ConvertTo(args[numNP].GetType(), ptypes[numNP])
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.EmitCall(typeof(object), "GetType");
          ptypes.EmitGet(cg);
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(Type), typeof(Type) });
          cg.ILG.Emit(OpCodes.Dup); // used by the 'if' below
          conv.EmitSet(cg);

          cg.EmitInt((int)Conversion.Identity); // if(conv==Identity || conv==Reference) { apa=2; goto next; }
          cg.ILG.Emit(OpCodes.Bne_Un_S, is2);
          conv.EmitGet(cg);
          cg.EmitInt((int)Conversion.Reference);
          cg.ILG.Emit(OpCodes.Bne_Un_S, not2);
          cg.ILG.MarkLabel(is2);
          cg.EmitInt(2);
          apa.EmitSet(cg);
          cg.ILG.Emit(OpCodes.Br_S, endlbl);

          cg.ILG.MarkLabel(not2);
          cg.EmitArgGet(0); // conv = Ops.ConvertTo(args[numNP].GetType(), ptypes[numnp].GetElementType());
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.EmitCall(typeof(object), "GetType");
          cg.EmitTypeOf(sig.Params[numnp].GetElementType());
          cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(Type), typeof(Type) });
          cg.ILG.Emit(OpCodes.Dup); // used by the 'if' below
          conv.EmitSet(cg);
          
          cg.EmitInt((int)Conversion.Identity); // if(conv==Identity || conv==Reference) { apa=0; goto next; }
          cg.ILG.Emit(OpCodes.Beq_S, is0);
          conv.EmitGet(cg);
          cg.EmitInt((int)Conversion.Reference);
          cg.ILG.Emit(OpCodes.Bne_Un_S, not0);
          cg.ILG.MarkLabel(is0);
          cg.EmitInt(0);
          apa.EmitSet(cg);
          cg.ILG.Emit(OpCodes.Br_S, endlbl);
          
          cg.ILG.MarkLabel(not0); // apa=1;
          cg.EmitInt(1);
          apa.EmitSet(cg);

          cg.ILG.MarkLabel(endlbl); // next:; }
          cg.FreeLocalTemp(conv);
        }

        Slot end=cg.AllocLocalTemp(typeof(int)), dconv=sig.ParamArray ? cg.AllocLocalTemp(typeof(int)) : null;
        cg.EmitInt(0); // int i=0;
        iv.EmitSet(cg);
        
        if(sig.ParamArray)
        { Label no=cg.ILG.DefineLabel(), endlbl=cg.ILG.DefineLabel(); // int dconv=(apa==2 ? numnp : ptypes.length);
          apa.EmitGet(cg);
          cg.EmitInt(2);
          cg.ILG.Emit(OpCodes.Bne_Un_S, no);
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Br_S, endlbl);
          cg.ILG.MarkLabel(no);
          cg.EmitInt(sig.Params.Length);
          cg.ILG.MarkLabel(endlbl);
          cg.ILG.Emit(OpCodes.Dup); // used below by code to send 'end'
          dconv.EmitSet(cg);
        }
        else cg.EmitInt(sig.Params.Length);

        cg.EmitArgGet(0); // int min=Math.Min(dconv, args.Length)
        cg.ILG.Emit(OpCodes.Ldlen);
        cg.EmitCall(typeof(Math), "Min", new Type[] { typeof(int), typeof(int) });
        end.EmitSet(cg);

        // for(; i!=end; i++) nargs[i] = Ops.ConvertTo(args[i], ptypes[i]);
        { Label start=cg.ILG.DefineLabel(), endlbl=cg.ILG.DefineLabel();
          cg.ILG.MarkLabel(start);
          iv.EmitGet(cg);
          end.EmitGet(cg);
          cg.ILG.Emit(OpCodes.Beq_S, endlbl);

          cg.EmitArgGet(0); // pushes the result of the conversion onto the stack
          iv.EmitGet(cg);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          ptypes.EmitGet(cg);
          iv.EmitGet(cg);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(object), typeof(Type) });
          
          iv.EmitGet(cg);
          cg.EmitInt(1);
          cg.ILG.Emit(OpCodes.Add);
          iv.EmitSet(cg);
          cg.ILG.Emit(OpCodes.Br_S, start);
          cg.ILG.MarkLabel(endlbl);
        }

        if(sig.Defaults!=null) // for(; i!=dconv; i++) nargs[i] = defaults[i-min];
        { Label start=cg.ILG.DefineLabel(), endlbl=cg.ILG.DefineLabel();
          cg.ILG.MarkLabel(start);
          iv.EmitGet(cg);
          if(sig.ParamArray) dconv.EmitGet(cg);
          else cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Beq_S, endlbl);
          
          defaults.EmitGet(cg);
          iv.EmitGet(cg);
          cg.EmitInt(min);
          cg.ILG.Emit(OpCodes.Sub);
          cg.ILG.Emit(OpCodes.Ldelem_Ref); // leaves the result on the stack
        }

        if(sig.ParamArray)
        { cg.FreeLocalTemp(dconv);
          Type etype = sig.Params[numnp].GetElementType();
        
          Label endlbl=cg.ILG.DefineLabel(), not0=cg.ILG.DefineLabel();
          Slot array=cg.AllocLocalTemp(Type.GetType(etype.FullName+"[]"));

          apa.EmitGet(cg); // if(apa==0) {
          cg.EmitInt(0);
          cg.ILG.Emit(OpCodes.Bne_Un_S, not0);
          
          cg.EmitInt(0); // pa = Array.CreateInstance(etype, Math.Min(0, end-ptypes.Length))
          end.EmitGet(cg);
          cg.EmitInt(sig.Params.Length);
          cg.ILG.Emit(OpCodes.Sub);
          cg.EmitCall(typeof(Math), "Min", new Type[] { typeof(int), typeof(int) });
          cg.ILG.Emit(OpCodes.Newarr, etype);
          
          cg.EmitInt(0); // for(int i=0; i!=pa.Length; i++) pa[i] = Ops.ConvertTo(args[i+numnp], etype);
          iv.EmitSet(cg);
          Label loop=cg.ILG.DefineLabel();
          cg.ILG.MarkLabel(loop);
          array.EmitGet(cg); // used by pa[i] and also to leave it on the stack at the end
          iv.EmitGet(cg);
          array.EmitGet(cg);
          cg.ILG.Emit(OpCodes.Ldlen);
          cg.ILG.Emit(OpCodes.Br_S, endlbl);

          iv.EmitGet(cg);
          cg.EmitArgGet(0);
          iv.EmitGet(cg);
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Add);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.EmitTypeOf(etype);
          cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(object), typeof(Type) });
          cg.ILG.Emit(OpCodes.Stelem_Ref);
          
          iv.EmitGet(cg);
          cg.EmitInt(1);
          cg.ILG.Emit(OpCodes.Add);
          iv.EmitSet(cg);
          cg.ILG.Emit(OpCodes.Br_S, loop);
          
          cg.ILG.MarkLabel(not0); // else if(apa==1) {
          apa.EmitGet(cg);
          cg.EmitInt(1);
          cg.ILG.Emit(OpCodes.Bne_Un_S, endlbl);
          
          Slot sa = cg.AllocLocalTemp(typeof(Array)); // Array sa = (Array)args[numnp];
          cg.EmitArgGet(0);
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.ILG.Emit(OpCodes.Castclass, typeof(Array));
          cg.ILG.Emit(OpCodes.Dup); // used by "Length" get, below
          sa.EmitSet(cg);
          
          cg.EmitPropGet(typeof(Array), "Length"); // etype[] pa = new etype[sa.Length]
          cg.ILG.Emit(OpCodes.Newarr, etype);
          array.EmitSet(cg);
          
          cg.EmitInt(0); // for(int i=0; i<sa.Length; i++) pa[i] = Ops.ConvertTo(sa[i], type);
          iv.EmitSet(cg);
          loop = cg.ILG.DefineLabel();
          cg.ILG.MarkLabel(loop);
          array.EmitGet(cg); // used by pa[i] and also to leave it on the stack at the end
          iv.EmitGet(cg);
          array.EmitGet(cg);
          cg.ILG.Emit(OpCodes.Ldlen);
          cg.ILG.Emit(OpCodes.Bne_Un_S, endlbl);
          
          iv.EmitGet(cg);
          sa.EmitGet(cg);
          iv.EmitGet(cg);
          cg.EmitCall(typeof(Array), "GetValue", new Type[] { typeof(int) });
          cg.EmitTypeOf(etype);
          cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(object), typeof(Type) });
          cg.ILG.Emit(OpCodes.Stelem_Ref);

          iv.EmitGet(cg);
          cg.EmitInt(1);
          cg.ILG.Emit(OpCodes.Add);
          iv.EmitSet(cg);
          cg.ILG.Emit(OpCodes.Br_S, loop);

          cg.FreeLocalTemp(sa);
          cg.FreeLocalTemp(array);
          
          cg.ILG.MarkLabel(endlbl); // end:; }
        }

        if(apa!=null) cg.FreeLocalTemp(apa);
        cg.FreeLocalTemp(iv);
        cg.FreeLocalTemp(end);
      }

      cg.EmitThis();
      cg.EmitFieldGet(typeof(FunctionWrapper), "methodPtr");
      cg.ILG.Emit(OpCodes.Tailcall);
      cg.ILG.EmitCalli(OpCodes.Calli, sig.Convention, sig.Return, sig.Params, null);
      cg.EmitReturn();
      cg.Finish();

      sigs[sig] = type = tg.FinishType();
    }
    return type;
  }
  #endregion

  static void ImportField(FieldInfo fi)
  { string nameBase = fi.IsStatic ? ":"+fi.DeclaringType.Name+"." : ":",
            getName  = nameBase+"get/"+fi.Name, setName = nameBase+"set/"+fi.Name;

    object cur;
    if(fi.IsStatic || !TopLevel.Current.Get(getName, out cur) || !(cur is ReflectedField) ||
        ((ReflectedField)cur).FieldName!=fi.Name)
      TopLevel.Current.Bind(getName, fi.IsStatic ? MakeGetWrapper(fi) : (object)new ReflectedField(fi.Name));

    if(fi.IsStatic && !fi.IsInitOnly && !fi.IsLiteral) TopLevel.Current.Bind(setName, MakeSetWrapper(fi));
  }

  static void ImportMethod(MethodInfo mi)
  { string name = (mi.IsStatic ? ":"+mi.DeclaringType.Name+"." : ":") + mi.Name;

    object cur;
    if(mi.IsStatic || !TopLevel.Current.Get(name, out cur) || !(cur is ReflectedMethod) ||
        ((ReflectedMethod)cur).MethodName!=mi.Name)
      TopLevel.Current.Bind(name, mi.IsStatic ? MakeFunctionWrapper(mi) : (object)new ReflectedMethod(mi.Name));
  }
  
  static void ImportProperty(PropertyInfo pi)
  { object cur;

    if(pi.CanRead)
    { MethodInfo mi = pi.GetGetMethod();
      string name = (mi.IsStatic ? ":"+pi.DeclaringType.Name+".get/" : ":get/");
      if(mi.IsStatic || !TopLevel.Current.Get(name, out cur) || !(cur is ReflectedMethod) ||
          ((ReflectedProperty)cur).PropertyName != mi.Name)
      TopLevel.Current.Bind(name, mi.IsStatic ? MakeFunctionWrapper(mi) : (object)new ReflectedProperty(pi.Name));
    }

    if(pi.CanWrite)
    { MethodInfo mi = pi.GetSetMethod();
      string name = (mi.IsStatic ? ":"+pi.DeclaringType.Name+".set/" : ":set/");
      if(mi.IsStatic || !TopLevel.Current.Get(name, out cur) || !(cur is ReflectedMethod) ||
          ((ReflectedProperty)cur).PropertyName != mi.Name)
      TopLevel.Current.Bind(name, mi.IsStatic ? MakeFunctionWrapper(mi) : (object)new ReflectedProperty(pi.Name));
    }
  }

  static readonly Hashtable sigs=new Hashtable(), funcs=new Hashtable(), gets=new Hashtable(), sets=new Hashtable();
}
#endregion

} // namespace NetLisp.Backend
