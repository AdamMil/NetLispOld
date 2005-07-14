using System;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region FieldWrapper
public abstract class FieldWrapper : IProcedure
{ public FieldWrapper(string name) { Name=name; }

  public int MinArgs { get { return 1; } }
  public int MaxArgs { get { return 2; } }

  public abstract object Call(object[] args);
  public override string ToString() { return "#<field '"+Name+"'>"; }

  public string Name;
}
#endregion

#region FunctionWrapper
public abstract class FunctionWrapper : IProcedure
{ public abstract int MinArgs { get; }
  public abstract int MaxArgs { get; }

  public abstract object Call(object[] args);
  public abstract Conversion TryMatch(object[] args, Type[] types);

  // TODO: see if we can optimize this even further?
  protected static Conversion TryMatch(object[] args, Type[] types, Type[] ptypes, int numNP, int min, bool paramArray)
  { if(args.Length<min || !paramArray && args.Length>ptypes.Length) return Conversion.None; // check number of required parameters
    Conversion ret=Conversion.Identity;

    // check types of all normal (non-paramarray) parameters
    for(int i=0; i<numNP; i++)
    { Conversion conv = Ops.ConvertTo(types[i], ptypes[i]);
      if(conv==Conversion.None) return Conversion.None;
      if(conv<ret) ret=conv;
    }

    if(paramArray)
    { Type etype = ptypes[numNP].GetElementType();

      if(args.Length==ptypes.Length && types[numNP].IsArray)
      { Conversion conv = Ops.ConvertTo(types[numNP], ptypes[numNP]);
        if(conv==Conversion.Identity || conv==Conversion.Reference)
        { if(conv<ret) ret = conv;
          return ret | Conversion.RefAPA;
        }

        conv = Ops.ConvertTo(types[numNP], etype);
        if(conv==Conversion.Identity || conv==Conversion.Reference)
          return (conv<ret ? conv : ret) | Conversion.PacksPA;

        // otherwise check that the remaining arguments can be converted to the element type
        conv = Ops.ConvertTo(types[numNP].GetElementType(), etype);
        if(conv==Conversion.None) return Conversion.None;
        if(conv<ret) ret=conv;
        return ret | (conv==Conversion.Unsafe ? Conversion.UnsafeAPA : Conversion.SafeAPA);
      }

      // check if extra parameters can be converted to the element type
      for(int i=numNP; i<args.Length; i++)
      { Conversion conv = Ops.ConvertTo(types[i], etype);
        if(conv==Conversion.None) return Conversion.None;
        if(conv<ret) ret=conv;
      }
      ret |= Conversion.PacksPA;
    }

    return ret;
  }
}

public abstract class FunctionWrapperI : FunctionWrapper
{ public FunctionWrapperI(IntPtr method) { methodPtr=method; }
  protected readonly IntPtr methodPtr;
}
#endregion

#region ReflectedConstructor
public sealed class ReflectedConstructor : SimpleProcedure
{ public ReflectedConstructor(Type type) : base("#<constructor '"+type.FullName+"'>", 0, -1)
  { this.type=type;
  }

  public override object Call(object[] args)
  { if(funcs==null)
    { ConstructorInfo[] ci = type.GetConstructors();
      type  = null;
      funcs = new FunctionWrapper[ci.Length];
      for(int i=0; i<ci.Length; i++) funcs[i] = Interop.MakeFunctionWrapper(ci[i]);
    }
    return Interop.Call(funcs, args);
  }

  FunctionWrapper[] funcs;
  Type type;
}
#endregion

#region ReflectedField
public sealed class ReflectedField : SimpleProcedure
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

#region ReflectedFunction
public sealed class ReflectedFunction : SimpleProcedure
{ public ReflectedFunction(Type type, string name) : base("#<method '"+name+"'>", 0, -1)
  { this.type=type; methodName=name;
  }

  public override object Call(object[] args)
  { if(funcs==null)
    { ArrayList list = new ArrayList();
      foreach(MethodInfo mi in type.GetMethods(BindingFlags.Public|BindingFlags.Static))
        if(mi.Name==methodName) list.Add(Interop.MakeFunctionWrapper(mi));
      funcs = (FunctionWrapper[])list.ToArray(typeof(FunctionWrapper));
      type  = null;
      methodName = null;
    }
    return Interop.Call(funcs, args);
  }

  FunctionWrapper[] funcs;
  Type type;
  string methodName;
}
#endregion

#region ReflectedMethod
public sealed class ReflectedMethod : SimpleProcedure
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
      if(list.Count==0) throw new ArgumentException("type "+inst.GetType().FullName+" does not have a '"+MethodName+"' method");
      hash[inst.GetType()] = funcs = (FunctionWrapper[])list.ToArray(typeof(FunctionWrapper));
    }

    return Interop.Call(funcs, args);
  }

  HybridDictionary hash;
}
#endregion

#region ReflectedProperty
public sealed class ReflectedProperty : SimpleProcedure
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

    Type[] types = Type.GetTypeArray(args);
    for(int i=0; i<args.Length; i++) types[i] = args[i]==null ? null : args[i].GetType();

    unsafe
    { Conversion *rets = stackalloc Conversion[funcs.Length];
      
      for(int i=0; i<funcs.Length; i++)
      { Conversion conv = funcs[i].TryMatch(args, types), qual=conv&Conversion.QualityMask;
        if(qual>bqual || qual==bqual && (conv>best && (best&Conversion.PacksPA)!=0 ||
                                         conv<best && (conv&Conversion.PacksPA)==0))
        { best=conv; bqual=qual; besti=i;
        }
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

  // TODO: handle operator overloads
  // TODO: handle arrays
  #region Import
  public static void Import(TopLevel top, Type type)
  { top.Bind("::"+type.Name, type);
    top.Bind("::"+type.FullName, type);

    ImportConstructors(top, type);
    foreach(EventInfo ei in type.GetEvents()) ImportEvent(top, ei);
    foreach(FieldInfo fi in type.GetFields()) ImportField(top, fi);
    ImportMethods(top, type);
    ImportProperties(top, type);
  }
  #endregion

  public static void LoadAssemblyByName(string name)
  { if(Assembly.LoadWithPartialName(name)==null) throw new ArgumentException("Assembly "+name+" could not be loaded");
  }
  public static void LoadAssemblyFromFile(string name) { Assembly.LoadFrom(name); }

  delegate object Create(IProcedure proc);

  #region Emit helpers
  static void EmitArrayStore(CodeGenerator cg, Type type)
  { switch(Type.GetTypeCode(type))
    { case TypeCode.Boolean: case TypeCode.Byte: case TypeCode.SByte: cg.ILG.Emit(OpCodes.Stelem_I1); break;
      case TypeCode.Int16: case TypeCode.UInt16: cg.ILG.Emit(OpCodes.Stelem_I2); break;
      case TypeCode.Int32: case TypeCode.UInt32: cg.ILG.Emit(OpCodes.Stelem_I4); break;
      case TypeCode.Int64: case TypeCode.UInt64: cg.ILG.Emit(OpCodes.Stelem_I8); break;
      case TypeCode.Single: cg.ILG.Emit(OpCodes.Stelem_R4); break;
      case TypeCode.Double: cg.ILG.Emit(OpCodes.Stelem_R8); break;
      default:
        if(type.IsPointer || type==typeof(IntPtr)) cg.ILG.Emit(OpCodes.Stelem_I);
        else if(type.IsValueType)
        { cg.ILG.Emit(OpCodes.Ldelema);
          cg.ILG.Emit(OpCodes.Stobj, type);
        }
        else cg.ILG.Emit(OpCodes.Stelem_Ref);
        break;
    }
  }

  static void EmitConvertTo(CodeGenerator cg, Type type) { EmitConvertTo(cg, type, false); }
  static void EmitConvertTo(CodeGenerator cg, Type type, bool useIndirect)
  { if(type==typeof(void)) throw new ArgumentException("Can't convert to void!");

    if(type.IsValueType || type.IsSubclassOf(typeof(Delegate))) // TODO: be sure to handle all special object types
    { cg.EmitTypeOf(type);
      cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(object), typeof(Type) });
    }

    if(type.IsValueType)
    { cg.ILG.Emit(OpCodes.Unbox, type);
      if(!useIndirect) cg.EmitIndirectLoad(type);
    }
    else if(type!=typeof(object)) cg.ILG.Emit(OpCodes.Castclass, type);
  }

  static bool UsesIndirectCopy(Type type) // TODO: see if it's faster to always use indirect copying
  { if(!type.IsValueType || type.IsPointer || type==typeof(IntPtr)) return false;
    TypeCode code = Type.GetTypeCode(type);
    return code==TypeCode.Object || code==TypeCode.DateTime || code==TypeCode.DBNull || code==TypeCode.Decimal;
  }
  #endregion

  #region ImportConstructors
  static void ImportConstructors(TopLevel top, Type type)
  { if(type.IsPrimitive) return;
    ConstructorInfo[] ci = type.GetConstructors();
    if(ci.Length==0) return;
    object obj = ci.Length==1 ? MakeFunctionWrapper(ci[0]) : (object)new ReflectedConstructor(type);
    top.Bind(":new/"+type.Name, obj);
    top.Bind(":new/"+type.FullName, obj);
  }
  #endregion

  #region ImportEvent
  static void ImportEvent(TopLevel top, EventInfo ei)
  { ImportMethods(top, ei.DeclaringType, new MethodInfo[] { ei.GetAddMethod() }, "add/", ei.Name);
    ImportMethods(top, ei.DeclaringType, new MethodInfo[] { ei.GetRemoveMethod() }, "rem/", ei.Name);
  }
  #endregion

  #region ImportField
  static void ImportField(TopLevel top, FieldInfo fi)
  { string shortBase = fi.IsStatic ? ":"+fi.DeclaringType.Name+"." : ":",
            fullBase = ":"+fi.DeclaringType.FullName+".", getSuf="get/"+fi.Name, setSuf="set/"+fi.Name;

    object obj;
    if(fi.IsStatic)
    { obj = MakeGetWrapper(fi);
      top.Bind(shortBase+getSuf, obj);
      top.Bind(fullBase+getSuf, obj);
    }
    else if(!top.Get(shortBase+getSuf, out obj) || !(obj is ReflectedField) ||
            ((ReflectedField)obj).FieldName!=fi.Name)
    { obj = new ReflectedField(fi.Name);
      top.Bind(shortBase+getSuf, obj);
      top.Bind(fullBase+getSuf, obj);
    }
    else top.Bind(fullBase+getSuf, obj);

    if(fi.IsStatic && !fi.IsInitOnly && !fi.IsLiteral)
    { obj = MakeSetWrapper(fi);
      top.Bind(shortBase+setSuf, obj);
      top.Bind(fullBase+setSuf, obj);
    }
  }
  #endregion

  #region ImportMethods
  static void ImportMethods(TopLevel top, Type type) { ImportMethods(top, type, type.GetMethods(), "", null); }

  static void ImportMethods(TopLevel top, Type type, MethodInfo[] methods, string prefix, string forceName)
  { ListDictionary dict = new ListDictionary();
    foreach(MethodInfo mi in methods)
    { string key = (mi.IsStatic ? "1$" : "0$") + mi.Name;
      ArrayList list = (ArrayList)dict[key];
      if(list==null) dict[key]=list=new ArrayList();
      list.Add(mi);
    }
    foreach(DictionaryEntry de in dict)
    { bool isStatic = ((string)de.Key)[0]=='1';
      MethodBase[] mi = (MethodBase[])((ArrayList)de.Value).ToArray(typeof(MethodBase));
      string realName=((string)de.Key).Substring(2), baseName=(forceName==null ? realName : forceName),
                 name=":"+(isStatic ? type.Name+"." : "")+prefix+baseName,
             fullName=":"+type.FullName+"."+prefix+baseName;

      object obj;
      if(isStatic)
      { obj = mi.Length==1 ? MakeFunctionWrapper(mi[0]) : (object)new ReflectedFunction(type, baseName);
        top.Bind(name, obj);
        top.Bind(fullName, obj);
      }
      else if(!top.Get(name, out obj) || !(obj is ReflectedMethod) || ((ReflectedMethod)obj).MethodName!=realName)
      { obj = new ReflectedMethod(realName);
        top.Bind(name, obj);
        top.Bind(fullName, obj); // TODO: optimize this by making the full version not dispatch on type
      }
      else top.Bind(fullName, obj); // ^-- this will be affected by the optimization too
    }
  }
  #endregion

  #region ImportProperties
  static void ImportProperties(TopLevel top, Type type)
  { ListDictionary dict = new ListDictionary();
    foreach(PropertyInfo pi in type.GetProperties())
    { ArrayList list = (ArrayList)dict[pi.Name];
      if(list==null) dict[pi.Name]=list=new ArrayList();
      list.Add(pi);
    }
    foreach(DictionaryEntry de in dict)
    { ArrayList gets=new ArrayList(), sets=new ArrayList();
      foreach(PropertyInfo pi in (ArrayList)de.Value)
      { if(pi.CanRead)  gets.Add(pi.GetGetMethod());
        if(pi.CanWrite) sets.Add(pi.GetSetMethod());
      }
      if(gets.Count!=0)
        ImportMethods(top, type, (MethodInfo[])gets.ToArray(typeof(MethodInfo)), "get/", (string)de.Key);
      if(sets.Count!=0)
        ImportMethods(top, type, (MethodInfo[])sets.ToArray(typeof(MethodInfo)), "set/", (string)de.Key);
    }
  }
  #endregion

  #region MakeDelegateWrapper
  public static object MakeDelegateWrapper(IProcedure proc, Type delegateType)
  { Create cr;
    lock(handlers) cr = (Create)handlers[delegateType];
    
    if(cr==null)
    { MethodInfo mi = delegateType.GetMethod("Invoke", BindingFlags.Public|BindingFlags.Instance);
      Signature sig = new Signature(mi, true);

      lock(dsigs) cr = (Create)dsigs[sig]; // FIXME: improve thread safety elsewhere
      if(cr==null)
      { for(int i=0; i<sig.Params.Length; i++)
          if(sig.Params[i].IsByRef) throw new NotImplementedException(); // TODO: implement this
        TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                            "dw$"+dwi.Next, null);
        Slot pslot = tg.DefineField(FieldAttributes.Private, "proc", typeof(IProcedure));

        CodeGenerator cg = tg.DefineConstructor(new Type[] { typeof(IProcedure) });
        cg.EmitArgGet(0);
        pslot.EmitSet(cg);
        cg.EmitReturn();
        cg.Finish();
        ConstructorInfo cons = (ConstructorInfo)cg.MethodBase;
        
        cg = tg.DefineMethod("Handle", sig.Return, sig.Params);
        pslot.EmitGet(cg);
        if(sig.Params.Length==0) cg.EmitFieldGet(typeof(Ops), "EmptyArray");
        else
        { cg.EmitNewArray(typeof(object), sig.Params.Length);
          for(int i=0; i<sig.Params.Length; i++)
          { cg.ILG.Emit(OpCodes.Dup);
            cg.EmitInt(i);
            cg.EmitArgGet(i);
            if(sig.Params[i].IsValueType) cg.ILG.Emit(OpCodes.Box, sig.Params[i]);
            cg.ILG.Emit(OpCodes.Stelem_Ref);
          }
          if(sig.Return==typeof(object)) cg.ILG.Emit(OpCodes.Tailcall);
          cg.EmitCall(typeof(IProcedure), "Call");
          if(sig.Return==typeof(void)) cg.ILG.Emit(OpCodes.Pop);
          else EmitConvertTo(cg, sig.Return);
          cg.EmitReturn();
          cg.Finish();
        }
        
        cg = tg.DefineStaticMethod("Create", typeof(object), new Type[] { typeof(IProcedure) });
        cg.EmitArgGet(0);
        cg.EmitNew(cons);
        cg.EmitReturn();
        cg.Finish();
        
        Type type = tg.FinishType();
        cr = (Create)Delegate.CreateDelegate(typeof(Create), type.GetMethod("Create"));
        lock(dsigs) dsigs[sig] = cr;
      }
      
      lock(handlers) handlers[delegateType] = cr;
    }
    
    return cr(proc);
  }
  #endregion
  
  #region MakeFunctionWrapper
  internal static FunctionWrapper MakeFunctionWrapper(MethodBase mi) { return MakeFunctionWrapper(mi, false); }
  internal static FunctionWrapper MakeFunctionWrapper(MethodBase mi, bool isPrimitive)
  { IntPtr ptr = mi.MethodHandle.Value;
    FunctionWrapper ret = (FunctionWrapper)funcs[ptr];
    if(ret==null)
    { bool isCons = mi is ConstructorInfo;
      funcs[ptr] = ret = (FunctionWrapper)MakeSignatureWrapper(mi)
                           .GetConstructor(isCons ? Type.EmptyTypes : new Type[] { typeof(IntPtr) })
                           .Invoke(isCons ? Ops.EmptyArray : new object[] { mi.MethodHandle.GetFunctionPointer() });
    }
    return ret;
  }
  #endregion

  // TODO: check arity
  #region MakeGetWrapper
  static FieldWrapper MakeGetWrapper(FieldInfo fi)
  { IntPtr ptr = fi.FieldHandle.Value;
    FieldWrapper ret = (FieldWrapper)gets[ptr];
    if(ret==null)
    { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                          "fg"+fwi.Next+"$"+fi.Name, typeof(FieldWrapper));
      CodeGenerator cg = tg.DefineMethodOverride("Call", true);
      if(!fi.IsStatic)
      { cg.EmitArgGet(0);
        cg.EmitInt(0);
        cg.ILG.Emit(OpCodes.Ldelem_Ref);
        if(fi.DeclaringType != typeof(object))
          cg.ILG.Emit(fi.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, fi.DeclaringType);
      }
      cg.EmitFieldGet(fi.DeclaringType, fi.Name);
      if(fi.FieldType.IsValueType) cg.ILG.Emit(OpCodes.Box, fi.FieldType);
      cg.EmitReturn();
      cg.Finish();

      cg = tg.DefineConstructor(Type.EmptyTypes);
      cg.EmitThis();
      cg.EmitString(fi.IsStatic ? fi.DeclaringType.FullName+"."+fi.Name : fi.Name);
      cg.EmitCall(typeof(FieldWrapper).GetConstructor(new Type[] { typeof(string) }));
      cg.EmitReturn();
      cg.Finish();

      gets[ptr] = ret = (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
    }
    return ret;
  }
  #endregion

  // TODO: check arity
  #region MakeSetWrapper
  static FieldWrapper MakeSetWrapper(FieldInfo fi)
  { IntPtr ptr = fi.FieldHandle.Value;
    FieldWrapper ret = (FieldWrapper)sets[ptr];
    if(ret==null)
    { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                          "fs"+fwi.Next+"$"+fi.Name, typeof(FieldWrapper));
      CodeGenerator cg = tg.DefineMethodOverride("Call", true);

      if(!fi.IsStatic)
      { cg.EmitArgGet(0);
        cg.EmitInt(0);
        cg.ILG.Emit(OpCodes.Ldelem_Ref);
        if(fi.DeclaringType != typeof(object))
          cg.ILG.Emit(fi.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, fi.DeclaringType);
      }
      
      cg.EmitArgGet(0);
      cg.EmitInt(fi.IsStatic ? 0 : 1);
      cg.ILG.Emit(OpCodes.Ldelem_Ref);
      if(UsesIndirectCopy(fi.FieldType))
      { cg.EmitFieldGetAddr(fi.DeclaringType, fi.Name);
        EmitConvertTo(cg, fi.FieldType, true);
        cg.ILG.Emit(OpCodes.Cpobj, fi.FieldType);
      }
      else
      { EmitConvertTo(cg, fi.FieldType);
        cg.EmitFieldSet(fi.DeclaringType, fi.Name);
      }

      cg.ILG.Emit(OpCodes.Ldnull);
      cg.EmitReturn();
      cg.Finish();

      cg = tg.DefineConstructor(Type.EmptyTypes);
      cg.EmitThis();
      cg.EmitString(fi.IsStatic ? fi.DeclaringType.FullName+"."+fi.Name : fi.Name);
      cg.EmitCall(typeof(FieldWrapper).GetConstructor(new Type[] { typeof(string) }));
      cg.EmitReturn();
      cg.Finish();

      sets[ptr] = ret = (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
    }
    return ret;
  }
  #endregion
  
  #region MakeSignatureWrapper
  struct Ref
  { public Ref(int i, Slot slot) { Index=i; Slot=slot; }
    public Slot Slot;
    public int Index;
  }

  static Type MakeSignatureWrapper(MethodBase mi)
  { Signature sig = new Signature(mi);
    Type type = (Type)sigs[sig];

    if(type==null)
    { bool isCons = mi is ConstructorInfo;
      TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                          "sw$"+swi.Next, isCons ? typeof(FunctionWrapper)
                                                                                      : typeof(FunctionWrapperI));
      int numnp = sig.ParamArray ? sig.Params.Length-1 : sig.Params.Length;
      int min = sig.Params.Length - (sig.Defaults==null ? 0 : sig.Defaults.Length) - (sig.ParamArray ? 1 : 0);
      int max = sig.ParamArray ? -1 : sig.Params.Length;
      int refi=0, numrefs=0;
      for(int i=0; i<sig.Params.Length; i++) if(sig.Params[i].IsByRef) numrefs++;
      Ref[] refs = new Ref[numrefs];

      #region Initialize statics
      Slot ptypes =
        sig.Params.Length==0 ? null : tg.DefineStaticField(FieldAttributes.Private, "ptypes", typeof(Type[]));
      Slot defaults =
        sig.Defaults==null ? null : tg.DefineStaticField(FieldAttributes.Private, "defaults", typeof(object[]));

      CodeGenerator cg = tg.GetInitializer();
      if(ptypes!=null) // ptypes
      { cg.EmitNewArray(typeof(Type), sig.Params.Length);
        for(int i=0; i<sig.Params.Length; i++)
        { cg.ILG.Emit(OpCodes.Dup);
          cg.EmitInt(i);
          cg.EmitTypeOf(sig.Params[i]);
          cg.ILG.Emit(OpCodes.Stelem_Ref);
        }
        ptypes.EmitSet(cg);
      }

      if(defaults!=null) // defaults
      { cg.EmitObjectArray(sig.Defaults);
        defaults.EmitSet(cg);
      }
      #endregion

      #region Constructor
      if(!isCons)
      { cg = tg.DefineChainedConstructor(new Type[] { typeof(IntPtr) });
        cg.EmitReturn();
        cg.Finish();
      }
      #endregion

      #region MinArgs and MaxArgs
      cg = tg.DefinePropertyOverride("MinArgs", true);
      cg.EmitInt(min);
      cg.EmitReturn();
      cg.Finish();
      
      cg = tg.DefinePropertyOverride("MaxArgs", true);
      cg.EmitInt(max);
      cg.EmitReturn();
      cg.Finish();
      #endregion

      #region TryMatch
      cg = tg.DefineMethodOverride(tg.BaseType.GetMethod("TryMatch", BindingFlags.Public|BindingFlags.Instance), true);
      cg.EmitArgGet(0);
      cg.EmitArgGet(1);
      if(ptypes!=null) ptypes.EmitGet(cg);
      else cg.EmitFieldGet(typeof(Type), "EmptyTypes");
      cg.EmitInt(numnp);
      cg.EmitInt(min);
      cg.EmitBool(sig.ParamArray);
      cg.EmitCall(tg.BaseType.GetMethod("TryMatch", BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.FlattenHierarchy));
      cg.EmitReturn();
      cg.Finish();
      #endregion

      MethodInfo checkArity = null;
      #region CheckArity
      if(!sig.ParamArray || min!=0)
      { // CheckArity
        cg = tg.DefineStaticMethod(MethodAttributes.Private, "CheckArity",
                                   typeof(void), new Type[] { typeof(object[]) });
        checkArity = (MethodInfo)cg.MethodBase;
        Label bad = cg.ILG.DefineLabel();
        Slot  len = cg.AllocLocalTemp(typeof(int));
        cg.EmitArgGet(0);
        cg.ILG.Emit(OpCodes.Ldlen);
        if(sig.ParamArray)
        { cg.EmitInt(min);
          cg.ILG.Emit(OpCodes.Blt_S, bad);
          cg.EmitReturn();
          cg.ILG.MarkLabel(bad);
          cg.EmitString((isCons ? "constructor" : "function")+" expects at least "+min.ToString()+
                        " arguments, but received ");
          cg.EmitArgGet(0);
          cg.ILG.Emit(OpCodes.Ldlen);
          len.EmitSet(cg);
          len.EmitGetAddr(cg);
          cg.EmitCall(typeof(int), "ToString", Type.EmptyTypes);
          cg.EmitCall(typeof(String), "Concat", new Type[] { typeof(string), typeof(string) });
          cg.EmitNew(typeof(ArgumentException), new Type[] { typeof(string) });
          cg.ILG.Emit(OpCodes.Throw);
        }
        else
        { cg.ILG.Emit(OpCodes.Dup);
          len.EmitSet(cg);
          cg.EmitInt(min);
          if(min==max) cg.ILG.Emit(OpCodes.Bne_Un_S, bad);
          else
          { cg.ILG.Emit(OpCodes.Blt_S, bad);
            len.EmitGet(cg);
            cg.EmitInt(max);
            cg.ILG.Emit(OpCodes.Bgt_S, bad);
          }
          cg.EmitReturn();
          cg.ILG.MarkLabel(bad);
          cg.EmitString((isCons ? "constructor" : "function")+" expects "+min.ToString()+
                        (min==max ? "" : "-"+max.ToString())+" arguments, but received ");
          len.EmitGetAddr(cg);
          cg.EmitCall(typeof(int), "ToString", Type.EmptyTypes);
          cg.EmitCall(typeof(String), "Concat", new Type[] { typeof(string), typeof(string) });
          cg.EmitNew(typeof(ArgumentException), new Type[] { typeof(string) });
          cg.ILG.Emit(OpCodes.Throw);
        }
        cg.FreeLocalTemp(len);
        cg.Finish();
      }
      #endregion

      #region Call
      cg = tg.DefineMethodOverride("Call", true);
      
      if(checkArity!=null)
      { cg.EmitArgGet(0);
        cg.EmitCall(checkArity);
      }
      
      for(int i=0; i<min; i++) // required arguments
      { cg.EmitArgGet(0);
        cg.EmitInt(i);
        cg.ILG.Emit(OpCodes.Ldelem_Ref);
        if(sig.Params[i].IsByRef)
        { Type etype = sig.Params[i].GetElementType();
          EmitConvertTo(cg, typeof(Reference));
          cg.EmitFieldGet(typeof(Reference), "Value");
          EmitConvertTo(cg, etype);
          Slot tmp = cg.AllocLocalTemp(etype);
          tmp.EmitSet(cg);
          tmp.EmitGetAddr(cg);
          refs[refi++] = new Ref(i, tmp);
        }
        else if(sig.Params[i].IsPointer) EmitConvertTo(cg, typeof(IntPtr));
        else EmitConvertTo(cg, sig.Params[i], i==0 && sig.IndirectThis);
      }

      if(min<numnp) // default arguments
      { Slot len = cg.AllocLocalTemp(typeof(int)); // TODO: test this code somehow
        cg.EmitArgGet(0);
        cg.ILG.Emit(OpCodes.Ldlen);
        len.EmitSet(cg);

        for(int i=min; i<numnp; i++)
        { Label next=cg.ILG.DefineLabel(), useArg=cg.ILG.DefineLabel();
          len.EmitGet(cg);
          cg.EmitInt(i);
          cg.ILG.Emit(OpCodes.Bgt_Un_S, useArg);
          cg.EmitConstant(sig.Defaults[i]);
          cg.ILG.Emit(OpCodes.Br_S, next);
          cg.ILG.MarkLabel(useArg);
          cg.EmitArgGet(0);
          cg.EmitInt(i);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          EmitConvertTo(cg, sig.Params[i]);
          cg.ILG.MarkLabel(next);
        }

        cg.FreeLocalTemp(len);
      }

      #region ParamArray handling
      if(sig.ParamArray)
      { Type etype = sig.Params[numnp].GetElementType();
        if(min==0 && etype==typeof(object)) cg.EmitArgGet(0);
        else
        { Slot iv=cg.AllocLocalTemp(typeof(int)), sa=cg.AllocLocalTemp(typeof(Array));
          Label pack=cg.ILG.DefineLabel(), call=cg.ILG.DefineLabel(), loop;
          bool ind = UsesIndirectCopy(etype);

          #region Handle array casting
          cg.EmitArgGet(0); // if(args.Length==ptypes.Length) {
          cg.ILG.Emit(OpCodes.Ldlen);
          cg.EmitInt(sig.Params.Length);
          cg.ILG.Emit(OpCodes.Bne_Un, pack);
          
          cg.EmitArgGet(0); // sa = args[numNP] as Array;
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.ILG.Emit(OpCodes.Isinst, typeof(Array));
          cg.ILG.Emit(OpCodes.Dup);
          sa.EmitSet(cg);
          cg.ILG.Emit(OpCodes.Brfalse, pack); // if(sa==null) goto pack
          
          sa.EmitGet(cg); // conv = Ops.ConvertTo(sa.GetType(), ptypes[numNP]);
          cg.EmitCall(typeof(Array), "GetType");
          cg.EmitTypeOf(sig.Params[numnp]);
          cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(Type), typeof(Type) });
          cg.ILG.Emit(OpCodes.Dup); // used below
          iv.EmitSet(cg);
          
          // if(conv==Identity || conv==Reference) { stack.push(castTo(ptypes[numNP], sa)); goto call; }
          Label not2=cg.ILG.DefineLabel(), is2=cg.ILG.DefineLabel();
          cg.EmitInt((int)Conversion.Identity);
          cg.ILG.Emit(OpCodes.Beq_S, is2);
          iv.EmitGet(cg);
          cg.EmitInt((int)Conversion.Reference);
          cg.ILG.Emit(OpCodes.Bne_Un_S, not2);
          cg.ILG.MarkLabel(is2);
          sa.EmitGet(cg);
          cg.ILG.Emit(OpCodes.Castclass, sig.Params[numnp]);
          cg.ILG.Emit(OpCodes.Br, call);
          #endregion

          #region Handle array conversion
          cg.ILG.MarkLabel(not2);
          if(etype!=typeof(object))
          { sa.EmitGet(cg); // conv = Ops.ConvertTo(sa.GetType(), ptypes[numnp].GetElementType());
            cg.EmitCall(typeof(Array), "GetType");
            cg.EmitTypeOf(etype);
            cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(Type), typeof(Type) });
            cg.ILG.Emit(OpCodes.Dup); // used below
            iv.EmitSet(cg);
            
            // if(conv==Identity || conv==Reference) goto pack;
            cg.EmitInt((int)Conversion.Identity);
            cg.ILG.Emit(OpCodes.Beq_S, pack);
            iv.EmitGet(cg);
            cg.EmitInt((int)Conversion.Reference);
            cg.ILG.Emit(OpCodes.Beq_S, pack);
            
            sa.EmitGet(cg); // etype[] pa = new etype[sa.Length];
            cg.EmitPropGet(typeof(Array), "Length");
            cg.ILG.Emit(OpCodes.Newarr, etype);

            cg.EmitInt(numnp); // for(int i=0; i<pa.Length; i++) pa[i] = ConvertTo(sa[i], etype);
            iv.EmitSet(cg);
            loop = cg.ILG.DefineLabel();
            cg.ILG.MarkLabel(loop);
            cg.ILG.Emit(OpCodes.Dup);
            cg.ILG.Emit(OpCodes.Ldlen);
            iv.EmitGet(cg);
            cg.ILG.Emit(OpCodes.Beq_S, call);
            cg.ILG.Emit(OpCodes.Dup);
            iv.EmitGet(cg);
            if(ind) cg.ILG.Emit(OpCodes.Ldelema);
            sa.EmitGet(cg); // sa[i]
            iv.EmitGet(cg);
            cg.EmitCall(typeof(Array), "GetValue", new Type[] { typeof(int) });
            EmitConvertTo(cg, etype, ind);
            if(ind) cg.ILG.Emit(OpCodes.Cpobj, etype);
            else EmitArrayStore(cg, etype);
            iv.EmitGet(cg); // i++
            cg.EmitInt(1);
            cg.ILG.Emit(OpCodes.Add);
            iv.EmitSet(cg);
            cg.ILG.Emit(OpCodes.Br_S, loop);
          }
          #endregion

          #region Handle new array packing
          cg.ILG.MarkLabel(pack); // pack:
          cg.EmitArgGet(0);       // etype[] pa = new etype[args.Length - numnp];
          cg.ILG.Emit(OpCodes.Ldlen);
          cg.EmitInt(numnp);
          cg.ILG.Emit(OpCodes.Sub);
          if(etype==typeof(object))
          { cg.ILG.Emit(OpCodes.Dup); // used below
            iv.EmitSet(cg);
          }
          cg.ILG.Emit(OpCodes.Newarr, etype);

          if(etype==typeof(object)) // Array.Copy(args, numnp, pa, 0, pa.Length)
          { Slot pa=cg.AllocLocalTemp(sig.Params[numnp]);
            pa.EmitSet(cg);
            cg.EmitArgGet(0);
            cg.EmitInt(numnp);
            pa.EmitGet(cg);
            cg.EmitInt(0);
            iv.EmitGet(cg);
            cg.EmitCall(typeof(Array), "Copy",
                        new Type[] { typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int) });
            pa.EmitGet(cg);
            cg.FreeLocalTemp(pa);
          }
          else
          { cg.EmitInt(numnp); // for(int i=numnp; i<args.Length; i++) pa[i-numnp] = ConvertTo(args[i], etype);
            iv.EmitSet(cg);
            loop = cg.ILG.DefineLabel();
            cg.ILG.MarkLabel(loop);
            iv.EmitGet(cg);
            cg.EmitArgGet(0);
            cg.ILG.Emit(OpCodes.Ldlen);
            cg.ILG.Emit(OpCodes.Beq_S, call);
            
            cg.ILG.Emit(OpCodes.Dup); // dup pa
            iv.EmitGet(cg);
            cg.EmitInt(numnp);
            cg.ILG.Emit(OpCodes.Sub);
            if(ind) cg.ILG.Emit(OpCodes.Ldelema);
            cg.EmitArgGet(0);
            iv.EmitGet(cg);
            cg.ILG.Emit(OpCodes.Ldelem_Ref);
            EmitConvertTo(cg, etype, ind);
            if(ind) cg.ILG.Emit(OpCodes.Cpobj, etype);
            else EmitArrayStore(cg, etype);
            iv.EmitGet(cg);
            cg.EmitInt(1);
            cg.ILG.Emit(OpCodes.Add);
            iv.EmitSet(cg);
            cg.ILG.Emit(OpCodes.Br_S, loop);
          }
          #endregion

          cg.ILG.MarkLabel(call);
          cg.FreeLocalTemp(iv);
          cg.FreeLocalTemp(sa);
        }
      }
      #endregion

      if(isCons)
      { cg.EmitNew((ConstructorInfo)mi);
        if(mi.DeclaringType.IsValueType) cg.ILG.Emit(OpCodes.Box, mi.DeclaringType);
      }
      else
      { if(sig.IndirectThis) cg.ILG.Emit(OpCodes.Ldftn, (MethodInfo)mi); // HACK: we hardcode the function pointer in this case because MethodHandle.GetFunctionPointer() doesn't return the correct value for instance calls on value types. this should be a safe hack
        else
        { cg.EmitThis();
          cg.EmitFieldGet(typeof(FunctionWrapperI), "methodPtr");
        }
        if(!sig.Return.IsValueType && numrefs==0) cg.ILG.Emit(OpCodes.Tailcall);
        cg.ILG.EmitCalli(OpCodes.Calli, sig.Convention, sig.Return, sig.Params, null);
        if(sig.Return==typeof(void)) cg.ILG.Emit(OpCodes.Ldnull);
        else if(sig.Return.IsValueType) cg.ILG.Emit(OpCodes.Box, sig.Return);
        
        foreach(Ref r in refs)
        { Type etype = sig.Params[r.Index].GetElementType();
          cg.EmitArgGet(0);
          cg.EmitInt(r.Index);
          cg.ILG.Emit(OpCodes.Ldelem_Ref);
          cg.ILG.Emit(OpCodes.Castclass, typeof(Reference));
          r.Slot.EmitGet(cg);
          if(etype.IsValueType) cg.ILG.Emit(OpCodes.Box, etype);
          cg.EmitFieldSet(typeof(Reference), "Value");
          cg.FreeLocalTemp(r.Slot);
        }
      }

      cg.EmitReturn();
      cg.Finish();
      #endregion
      sigs[sig] = type = tg.FinishType();
    }
    return type;
  }
  #endregion

  static readonly Hashtable sigs=new Hashtable(), funcs=new Hashtable(), gets=new Hashtable(), sets=new Hashtable();
  static Hashtable handlers=new Hashtable(), dsigs=new Hashtable();
  static Index dwi=new Index(), fwi=new Index(), swi=new Index();
}
#endregion

#region Signature
sealed class Signature
{ public Signature(MethodBase mi) : this(mi, false) { }
  public Signature(MethodBase mi, bool ignoreThis)
  { ParameterInfo[] pi = mi.GetParameters();

    IsCons   = mi is ConstructorInfo;
    int so   = IsCons || mi.IsStatic || ignoreThis ? 0 : 1;

    Convention = mi.CallingConvention==CallingConventions.VarArgs ? CallingConventions.VarArgs
                                                                  : CallingConventions.Standard;
    Return     = IsCons ? mi.DeclaringType : ((MethodInfo)mi).ReturnType;
    Params     = new Type[pi.Length + so];
    ParamArray = pi.Length>0 && pi[pi.Length-1].IsDefined(typeof(ParamArrayAttribute), false);

    if(so==1)
    { Params[0] = mi.DeclaringType;
      IndirectThis = mi.DeclaringType.IsValueType;
    }
    for(int i=0,req=-1; i<pi.Length; i++)
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
    if(Params.Length!=o.Params.Length || ParamArray!=o.ParamArray || Return!=o.Return || Convention!=o.Convention ||
        IsCons!=o.IsCons || IndirectThis!=o.IndirectThis ||
       (Defaults==null && o.Defaults!=null || Defaults!=null && o.Defaults==null ||
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
  public bool     IsCons, IndirectThis, ParamArray;
}
#endregion

} // namespace NetLisp.Backend
