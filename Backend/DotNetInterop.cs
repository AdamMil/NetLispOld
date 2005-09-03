/*
NetLisp is the reference implementation for a language similar to
Scheme, also called NetLisp. This implementation is both interpreted
and compiled, targetting the Microsoft .NET Framework.

http://www.adammil.net/
Copyright (C) 2005 Adam Milazzo

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region FieldWrapper
public abstract class FieldWrapper : IProcedure
{ public FieldWrapper(string name) { Name=name; }

  public int MinArgs { get { return 1; } }
  public int MaxArgs { get { return 2; } }
  public bool NeedsFreshArgs { get { return false; } }

  public abstract object Call(object[] args);
  public override string ToString() { return "#<field '"+Name+"'>"; }

  public string Name;
}
#endregion

#region FunctionWrapper
public abstract class FunctionWrapper : IProcedure
{ public abstract int MinArgs { get; }
  public abstract int MaxArgs { get; }
  public bool NeedsFreshArgs { get { return false; } }

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

#region StructCreator
public abstract class StructCreator : FunctionWrapper
{ public override int MaxArgs { get { return 0; } }
  public override int MinArgs { get { return 0; } }
  public override Conversion TryMatch(object[] args, Type[] types)
  { return TryMatch(args, types, Type.EmptyTypes, 0, 0, false);
  }
}
#endregion

#region ReflectedConstructors
public sealed class ReflectedConstructors : SimpleProcedure
{ public ReflectedConstructors(Type type) : base("#<constructor '"+type.FullName+"'>", 0, -1)
  { this.type=type;
  }

  public override object Call(object[] args)
  { if(funcs==null)
      lock(this)
      { funcs = ReflectedType.GetConstructors(type);
        type = null;
      }
    return Interop.Call(funcs, args);
  }

  FunctionWrapper[] funcs;
  Type type;
}
#endregion

#region ReflectedFunctions
public sealed class ReflectedFunctions : SimpleProcedure
{ public ReflectedFunctions(MethodInfo[] mis, string name) : base("#<method '"+name+"'>", 0, -1)
  { methods = mis;
  }

  public override object Call(object[] args)
  { if(funcs==null)
      lock(this)
      { funcs = new FunctionWrapper[methods.Length];
        for(int i=0; i<methods.Length; i++) funcs[i] = ReflectedType.MakeFunctionWrapper(methods[i]);
        methods = null;
      }
    return Interop.Call(funcs, args);
  }

  FunctionWrapper[] funcs;
  MethodInfo[] methods;
}
#endregion

#region ReflectedNamespace
public sealed class ReflectedNamespace : MemberContainer
{ public ReflectedNamespace(string name) { this.name=name; dict=new Hashtable(); }

  public override object GetMember(string name)
  { object ret = dict[name];
    if(ret==null && !dict.Contains(name))
      throw new ArgumentException(this.name+" does not contain a member named '"+name+"'");
    return ret;
  }

  public override bool GetMember(string name, out object ret)
  { ret = dict[name];
    return ret!=null || dict.Contains(name);
  }

  public override ICollection GetMemberNames() { return dict.Keys; }

  public override void Import(TopLevel top, string[] names, string[] asNames)
  { Interop.Import(top, dict, names, asNames, "namespace '"+name+"'");
  }

  public override string ToString() { return "#<namespace '"+name+">"; }

  public void AddNamespace(ReflectedNamespace ns)
  { Debug.Assert(ns.name.StartsWith(name+'.'));
    dict[ns.name.Substring(ns.name.LastIndexOf('.')+1)] = ns;
  }

  public void AddType(Type type)
  { Debug.Assert(type.Namespace==name);
    dict[type.Name] = ReflectedType.FromType(type);
  }

  string name;
  Hashtable dict;
}
#endregion

#region ReflectedType
public sealed class ReflectedType : MemberContainer
{ internal ReflectedType(Type type) { Type=type; }

  #region Static constructor
  static ReflectedType()
  { opnames["op_Addition"]        = "op+";
    opnames["op_BitwiseAnd"]      = "op_bitand";
    opnames["op_BitwiseOr"]       = "op_bitor";
    opnames["op_Decrement"]       = "op--";
    opnames["op_Division"]        = "op/";
    opnames["op_Equality"]        = "op==";
    opnames["op_ExclusiveOr"]     = "op_bitxor";
    opnames["op_GreaterThan"]     = "op>";
    opnames["op_GreaterThanOrEqual"] = "op>=";
    opnames["op_Inequality"]      = "op!=";
    opnames["op_Increment"]       = "op++";
    opnames["op_LeftShift"]       = "op_lshift";
    opnames["op_LessThan"]        = "op<";
    opnames["op_LessThanOrEqual"] = "op<=";
    opnames["op_Modulus"]         = "op%";
    opnames["op_Multiply"]        = "op*";
    opnames["op_OnesComplement"]  = "op_bitnot";
    opnames["op_RightShift"]      = "op_rshift";
    opnames["op_Subtraction"]     = "op-";
    opnames["op_UnaryNegation"]   = "op_neg";
    opnames["op_UnaryPlus"]       = "op_unary-plus";
  }
  #endregion

  public Hashtable Dict
  { get
    { if(dict==null) Initialize();
      return dict;
    }
  }

  public override object GetMember(string name)
  { if(dict==null) Initialize();
    object ret = dict[name];
    if(ret==null && !dict.Contains(name))
      throw new ArgumentException(Ops.TypeName(Type)+" does not contain a member named '"+name+"'");
    return ret;
  }

  public override bool GetMember(string name, out object ret)
  { if(dict==null) Initialize();
    ret = dict[name];
    return ret!=null || dict.Contains(name);
  }

  public override ICollection GetMemberNames() { return Dict.Keys; }

  public override void Import(TopLevel top, string[] names, string[] asNames)
  { Interop.Import(top, Dict, names, asNames, "type '"+Ops.TypeName(Type)+"'");
  }

  public override string ToString() { return "#<type '"+Ops.TypeName(Type)+"'>"; }

  public readonly Type Type;

  public static ReflectedType FromType(Type type)
  { ReflectedType rt = (ReflectedType)types[type];
    if(rt==null) types[type] = rt = new ReflectedType(type);
    return rt;
  }

  public static readonly ReflectedType NullType = new ReflectedType(null);

  sealed class Methods
  { public Methods(Type type) { List=new ArrayList(); Type=type; }
    public ArrayList List;
    public Type Type;
  }

  sealed class Properties
  { public Properties(Type type) { Gets=new ArrayList(); Sets=new ArrayList(); Type=type; }
    public ArrayList Gets, Sets;
    public Type Type;
  }

  struct Event
  { public Event(EventInfo ei)
    { DeclaringType=ei.DeclaringType; Name=GetMemberName(ei); Add=ei.GetAddMethod(); Rem=ei.GetRemoveMethod();
    }
    public Event(Type type, string name, MethodInfo add, MethodInfo rem)
    { DeclaringType=type; Name=name; Add=add; Rem=rem;
    }
    public Type DeclaringType;
    public string Name;
    public MethodInfo Add, Rem;
  }

  #region Initialize
  void Initialize()
  { dict = new Hashtable();
    if(Type==null) return;

    // TODO: handle certain types specially? eg, delegates and enums?
    // TODO: add [] for arrays
    // TODO: add the values for the names more lazily, if possible
    // TODO: speed this up!

    if(!Type.IsPrimitive) // add constructors
    { ConstructorInfo[] ci = Type.GetConstructors();
      bool needDefault = Type.IsValueType && !Type.IsPrimitive;
      if(ci.Length!=0 || needDefault)
        dict[Type.Name] = ci.Length+(needDefault ? 1 : 0) != 1 ? (object)new ReflectedConstructors(Type)
                            : needDefault ? MakeStructCreator(Type) : MakeFunctionWrapper(ci[0]);
    }

    Type[] interfaces = Type.GetInterfaces();
    InterfaceMapping[] maps = new InterfaceMapping[interfaces.Length];
    for(int i=0; i<interfaces.Length; i++) maps[i] = Type.GetInterfaceMap(interfaces[i]);

    // add events
    ArrayList events = new ArrayList();
    foreach(EventInfo ei in Type.GetEvents()) events.Add(new Event(ei));
    for(int i=0; i<interfaces.Length; i++)
      foreach(EventInfo ei in interfaces[i].GetEvents())
      { MethodInfo mi = FindMethod(maps[i], ei.GetAddMethod());
        if(!mi.IsPublic)
          events.Add(new Event(ei.DeclaringType, GetMemberName(ei), mi, FindMethod(maps[i], ei.GetRemoveMethod())));
      }
    foreach(Event e in events)
    { string add="add/"+e.Name, rem="rem/"+e.Name;
      if(e.DeclaringType!=Type)
      { IDictionary dec = FromType(e.DeclaringType).Dict;
        dict[add] = dec[add];
        dict[rem] = dec[rem];
      }
      else
      { dict[add] = MakeFunctionWrapper(e.Add);
        dict[rem] = MakeFunctionWrapper(e.Rem);
      }
    }
    events = null;

    // add fields
    foreach(FieldInfo fi in Type.GetFields())
    { string name=GetMemberName(fi), get=name, set="set/"+name;
      bool readOnly = fi.IsInitOnly || fi.IsLiteral;
      if(fi.DeclaringType!=Type)
      { IDictionary dec = FromType(fi.DeclaringType).Dict;
        dict[get] = dec[get];
        if(!readOnly) dict[set] = dec[set];
      }
      else
      { dict[get] = MakeGetWrapper(fi);
        if(!readOnly) dict[set] = MakeSetWrapper(fi);
      }
    }

    // add properties
    ListDictionary overloads = new ListDictionary();
    for(int i=0; i<interfaces.Length; i++)
      foreach(PropertyInfo pi in interfaces[i].GetProperties())
      { MethodInfo get = pi.CanRead ? FindMethod(maps[i], pi.GetGetMethod()) : null;
        MethodInfo set = pi.CanWrite ? FindMethod(maps[i], pi.GetSetMethod()) : null;
        if(get!=null && !get.IsPublic || set!=null && !set.IsPublic)
          AddProperty(overloads, GetMemberName(pi), get, set);
      }
    foreach(PropertyInfo pi in Type.GetProperties()) AddProperty(overloads, pi);
    foreach(DictionaryEntry de in overloads)
    { string get=(string)de.Key, set="set/"+(string)de.Key;
      Properties ps = (Properties)de.Value;
      if(ps.Type!=Type)
      { IDictionary dec = FromType(ps.Type).Dict;
        if(ps.Gets.Count!=0) dict[get] = dec[get];
        if(ps.Sets.Count!=0) dict[set] = dec[set];
      }
      else
      { if(ps.Gets.Count!=0)
          dict[get] = ps.Gets.Count==1 ? MakeFunctionWrapper((MethodInfo)ps.Gets[0])
                        : (object)new ReflectedFunctions((MethodInfo[])ps.Gets.ToArray(typeof(MethodInfo)), get);
        if(ps.Sets.Count!=0)
          dict[set] = ps.Sets.Count==1 ? MakeFunctionWrapper((MethodInfo)ps.Sets[0])
                        : (object)new ReflectedFunctions((MethodInfo[])ps.Sets.ToArray(typeof(MethodInfo)), set);
      }
    }

    // add methods
    overloads.Clear();
    for(int i=0; i<interfaces.Length; i++)
      foreach(MethodInfo mi in interfaces[i].GetMethods())
        if(!IsSpecialMethod(mi))
        { MethodInfo tmi = FindMethod(maps[i], mi);
          if(!tmi.IsPublic) AddMethod(overloads, tmi, GetMemberName(mi));
        }
    foreach(MethodInfo mi in Type.GetMethods()) if(!IsSpecialMethod(mi)) AddMethod(overloads, mi);
    foreach(DictionaryEntry de in overloads)
    { Methods ov = (Methods)de.Value;
      dict[de.Key] = ov.Type!=Type ? FromType(ov.Type).Dict[de.Key]
                       : ov.List.Count==1 ? MakeFunctionWrapper((MethodInfo)ov.List[0])
                           : (object)new ReflectedFunctions((MethodInfo[])ov.List.ToArray(typeof(MethodInfo)),
                                                            (string)de.Key);
    }

    foreach(Type subtype in Type.GetNestedTypes(BindingFlags.Public))
      if(subtype.IsSubclassOf(typeof(Primitive)))
      { Primitive prim = (Primitive)subtype.GetConstructor(Type.EmptyTypes).Invoke(null);
        dict[prim.Name] = prim;
      }
      else dict[GetMemberName(subtype)] = ReflectedType.FromType(subtype);
  }
  #endregion

  Hashtable dict;

  internal static FunctionWrapper[] GetConstructors(Type type)
  { ConstructorInfo[] ci = type.GetConstructors();
    bool needDefault = type.IsValueType && !type.IsPrimitive;
    FunctionWrapper[] ret = new FunctionWrapper[ci.Length + (needDefault ? 1 : 0)];
    for(int i=0; i<ci.Length; i++) ret[i] = MakeFunctionWrapper(ci[i]);
    if(needDefault) ret[ci.Length] = MakeStructCreator(type);
    return ret;
  }

  static void AddMethod(IDictionary overloads, MethodInfo mi) { AddMethod(overloads, mi, GetMemberName(mi)); }
  static void AddMethod(IDictionary overloads, MethodInfo mi, string name)
  { string op=(string)opnames[name];
    if(op!=null) name = op;
    Methods ov = (Methods)overloads[name];
    if(ov==null) overloads[name] = ov = new Methods(mi.DeclaringType);
    else if(mi.DeclaringType.IsSubclassOf(ov.Type)) ov.Type = mi.DeclaringType;
    ov.List.Add(mi);
  }

  static void AddProperty(IDictionary overloads, PropertyInfo pi)
  { AddProperty(overloads, GetMemberName(pi), pi.CanRead ? pi.GetGetMethod() : null,
                pi.CanWrite ? pi.GetSetMethod() : null);
  }

  static void AddProperty(IDictionary overloads, string name, MethodInfo get, MethodInfo set)
  { Properties ps = (Properties)overloads[name];
    Type declaringType = MostDerived(get, set);
    if(ps==null) overloads[name] = ps = new Properties(declaringType);
    else if(declaringType.IsSubclassOf(ps.Type)) ps.Type = declaringType;
    if(get!=null) ps.Gets.Add(get);
    if(set!=null) ps.Sets.Add(set);
  }

  static MethodInfo FindMethod(InterfaceMapping map, MethodInfo mi)
  { int pos = Array.IndexOf(map.InterfaceMethods, mi);
    return pos==-1 ? null : map.TargetMethods[pos];
  }

  static string GetMemberName(MemberInfo mi)
  { object[] attrs = mi.GetCustomAttributes(typeof(SymbolNameAttribute), false);
    return attrs.Length==0 ? mi.Name : ((SymbolNameAttribute)attrs[0]).Name;
  }

  static bool IsSpecialMethod(MethodInfo mi)
  { return mi.IsSpecialName && (mi.Name.StartsWith("get_") || mi.Name.StartsWith("set_") ||
                                mi.Name!="op_Implicit" || mi.Name!="op_Explicit"); // TODO: handle these ops somehow?
  }
  
  static Type MostDerived(MethodInfo f1, MethodInfo f2)
  { if(f1==null) return f2.DeclaringType;
    else if(f2==null || f1.DeclaringType.IsSubclassOf(f2.DeclaringType)) return f1.DeclaringType;
    else return f2.DeclaringType;
  }

  #region MakeFunctionWrapper
  internal static FunctionWrapper MakeFunctionWrapper(MethodBase mi)
  { bool isCons = mi is ConstructorInfo;
    return (FunctionWrapper)MakeSignatureWrapper(mi)
            .GetConstructor(isCons ? Type.EmptyTypes : new Type[] { typeof(IntPtr) })
            .Invoke(isCons ? Ops.EmptyArray : new object[] { mi.MethodHandle.GetFunctionPointer() });
  }
  #endregion

  #region MakeGetWrapper
  static FieldWrapper MakeGetWrapper(FieldInfo fi)
  { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                        "fg"+fwi.Next+"$"+fi.Name, typeof(FieldWrapper));
    CodeGenerator cg = tg.DefineMethodOverride("Call", true);
    if(!fi.IsStatic)
    { if(Options.Debug)
      { Label good = cg.ILG.DefineLabel();
        cg.EmitArgGet(0);
        cg.ILG.Emit(OpCodes.Ldlen);
        cg.EmitInt(1);
        cg.ILG.Emit(OpCodes.Bge_S, good);
        cg.EmitString("non-static field getter expects 1 argument");
        cg.EmitNew(typeof(TargetParameterCountException), new Type[] { typeof(string) });
        cg.ILG.Emit(OpCodes.Throw);
        cg.ILG.MarkLabel(good);
      }
      cg.EmitArgGet(0);
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

    return (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }
  #endregion

  #region MakeSetWrapper
  static FieldWrapper MakeSetWrapper(FieldInfo fi)
  { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                        "fs"+fwi.Next+"$"+fi.Name, typeof(FieldWrapper));
    CodeGenerator cg = tg.DefineMethodOverride("Call", true);

    if(Options.Debug)
    { Label good = cg.ILG.DefineLabel();
      cg.EmitArgGet(0);
      cg.ILG.Emit(OpCodes.Ldlen);
      cg.EmitInt(fi.IsStatic ? 1 : 2);
      cg.ILG.Emit(OpCodes.Bge_S, good);
      cg.EmitString((fi.IsStatic ? "static" : "non-static")+" field getter expects "+
                    (fi.IsStatic ? "1 argument" : "2 arguments"));
      cg.EmitNew(typeof(TargetParameterCountException), new Type[] { typeof(string) });
      cg.ILG.Emit(OpCodes.Throw);
      cg.ILG.MarkLabel(good);
    }

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
      Interop.EmitConvertTo(cg, fi.FieldType, true);
      cg.ILG.Emit(OpCodes.Cpobj, fi.FieldType);
    }
    else
    { Interop.EmitConvertTo(cg, fi.FieldType);
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

    return (FieldWrapper)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
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
          Interop.EmitConvertTo(cg, typeof(Reference));
          cg.EmitFieldGet(typeof(Reference), "Value");
          Interop.EmitConvertTo(cg, etype);
          Slot tmp = cg.AllocLocalTemp(etype);
          tmp.EmitSet(cg);
          tmp.EmitGetAddr(cg);
          refs[refi++] = new Ref(i, tmp);
        }
        else if(sig.Params[i].IsPointer) Interop.EmitConvertTo(cg, typeof(IntPtr));
        else Interop.EmitConvertTo(cg, sig.Params[i], i==0 && sig.PointerHack!=IntPtr.Zero);
      }

      if(min<numnp) // default arguments
      { Slot len = cg.AllocLocalTemp(typeof(int)); // TODO: test this code
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
          Interop.EmitConvertTo(cg, sig.Params[i]);
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
            cg.ILG.Emit(Options.Debug ? OpCodes.Beq : OpCodes.Beq_S, pack);
            iv.EmitGet(cg);
            cg.EmitInt((int)Conversion.Reference);
            cg.ILG.Emit(Options.Debug ? OpCodes.Beq : OpCodes.Beq_S, pack);
            
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
            cg.ILG.Emit(Options.Debug ? OpCodes.Beq : OpCodes.Beq_S, call);
            cg.ILG.Emit(OpCodes.Dup);
            iv.EmitGet(cg);
            if(ind) cg.ILG.Emit(OpCodes.Ldelema);
            sa.EmitGet(cg); // sa[i]
            iv.EmitGet(cg);
            cg.EmitCall(typeof(Array), "GetValue", new Type[] { typeof(int) });
            Interop.EmitConvertTo(cg, etype, ind);
            if(ind) cg.ILG.Emit(OpCodes.Cpobj, etype);
            else cg.EmitArrayStore(etype);
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
            Interop.EmitConvertTo(cg, etype, ind);
            if(ind) cg.ILG.Emit(OpCodes.Cpobj, etype);
            else cg.EmitArrayStore(etype);
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
      { // TODO: report this to microsoft and see if we can get a straight answer
        if(sig.PointerHack!=IntPtr.Zero) cg.ILG.Emit(OpCodes.Ldftn, (MethodInfo)mi); // HACK: we hardcode the function pointer in this case because MethodHandle.GetFunctionPointer() doesn't return the correct value for instance calls on value types. i'm not sure if this is safe, but it seems to work.
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

  #region MakeStructCreator
  internal static StructCreator MakeStructCreator(Type type)
  { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
                                                        "sc$"+sci.Next, typeof(StructCreator));
    CodeGenerator cg = tg.DefineMethodOverride("Call", true);
    Slot slot = cg.AllocLocalTemp(type);
    slot.EmitGetAddr(cg);
    cg.ILG.Emit(OpCodes.Initobj, type);
    slot.EmitGet(cg);
    cg.ILG.Emit(OpCodes.Box, type);
    cg.EmitReturn();
    cg.Finish();
    return (StructCreator)tg.FinishType().GetConstructor(Type.EmptyTypes).Invoke(null);
  }
  #endregion

  static bool UsesIndirectCopy(Type type) // TODO: see if it's faster to always use indirect copying
  { if(!type.IsValueType || type.IsPointer || type==typeof(IntPtr)) return false;
    TypeCode code = Type.GetTypeCode(type);
    return code==TypeCode.Object || code==TypeCode.DateTime || code==TypeCode.DBNull || code==TypeCode.Decimal;
  }

  static readonly Hashtable types=new Hashtable(), sigs=new Hashtable(), opnames=new Hashtable();
  static readonly Index fwi=new Index(), swi=new Index(), sci=new Index();
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
    for(int i=0; i<args.Length; i++)
    { object o = args[i];
      if(o==null) types[i] = null;
      else
      { Type type = o.GetType();
        if(type==Disambiguator.ClassType)
        { Disambiguator dis = o as Disambiguator;
          args[i]  = dis.Value;
          types[i] = dis.Type;
        }
        else types[i] = type;
      }
    }

    unsafe
    { Conversion *rets = stackalloc Conversion[funcs.Length];

      for(int i=0; i<funcs.Length; i++)
      { Conversion conv = funcs[i].TryMatch(args, types), qual=conv&Conversion.QualityMask;
        if(qual>bqual || qual==bqual && (conv>best && (best&Conversion.PacksPA)!=0 ||
                                         conv<best && (conv&Conversion.PacksPA)==0))
        { best=conv; bqual=qual; besti=i;
          if(bqual==Conversion.Identity) break; // this complements the check down below
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

  public static Type GetType(string name) { return GetType(name, false); }
  public static Type GetType(string name, bool throwOnError)
  { Type type = Type.GetType(name);
    if(type==null)
      foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())
      { type = a.GetType(name);
        if(type!=null) break;
      }
    if(type==null && throwOnError) throw new ArgumentException("Unable to load type: "+name, "name");
    return type;
  }

  public static void LoadAssemblyByName(string name)
  { if(Assembly.LoadWithPartialName(name)==null) throw new ArgumentException("Assembly "+name+" could not be loaded");
  }
  public static void LoadAssemblyFromFile(string name) { Assembly.LoadFrom(name); }

  #region MakeDelegateWrapper
  public static object MakeDelegateWrapper(IProcedure proc, Type delegateType)
  { Create cr;
    lock(handlers) cr = (Create)handlers[delegateType];

    if(cr==null)
    { MethodInfo mi = delegateType.GetMethod("Invoke", BindingFlags.Public|BindingFlags.Instance);
      Signature sig = new Signature(mi, true);

      lock(dsigs)
      { cr = (Create)dsigs[sig];
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
          dsigs[sig] = cr = (Create)Delegate.CreateDelegate(typeof(Create), type.GetMethod("Create"));
        }
      }

      lock(handlers) handlers[delegateType] = cr;
    }

    return cr(proc);
  }
  #endregion

  internal static void Import(TopLevel top, IDictionary dict, string[] names, string[] asNames, string myName)
  { if(names==null)
      foreach(DictionaryEntry de in dict) top.Globals.Bind((string)de.Key, de.Value, null);
    else
      for(int i=0; i<names.Length; i++)
      { object obj = dict[names[i]];
        if(obj==null && !dict.Contains(names[i]))
          throw new ArgumentException(myName+" does not contain a member called '"+names[i]+"'");
        top.Globals.Bind(asNames[i], obj, null);
      }
  }

  internal static void EmitConvertTo(CodeGenerator cg, Type type) { EmitConvertTo(cg, type, false); }
  internal static void EmitConvertTo(CodeGenerator cg, Type type, bool useIndirect)
  { if(type==typeof(void)) throw new ArgumentException("Can't convert to void!");

    if(type.IsValueType || type.IsSubclassOf(typeof(Delegate))) // TODO: be sure to handle all special object types
    { cg.EmitTypeOf(type);
      cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(object), typeof(Type) });
    }

    if(Options.Debug)
    { Slot tmp = cg.AllocLocalTemp(typeof(object));
      Label good = cg.ILG.DefineLabel();

      cg.ILG.Emit(OpCodes.Dup);
      tmp.EmitSet(cg);
      cg.ILG.Emit(OpCodes.Isinst, type);
      cg.ILG.Emit(OpCodes.Brtrue_S, good);
      if(!type.IsValueType)
      { tmp.EmitGet(cg);
        cg.ILG.Emit(OpCodes.Brfalse_S, good); // null reference types are okay
      }
      cg.EmitString("expected argument of type "+Ops.TypeName(type)+", but received ");
      tmp.EmitGet(cg);
      cg.EmitCall(typeof(Ops), "TypeName", new Type[] { typeof(object) });
      cg.EmitCall(typeof(string), "Concat", new Type[] { typeof(string), typeof(string) });
      cg.EmitCall(typeof(Ops), "TypeError", new Type[] { typeof(string) });
      cg.ILG.Emit(OpCodes.Throw);
      cg.ILG.MarkLabel(good);
      tmp.EmitGet(cg);

      cg.FreeLocalTemp(tmp);
    }

    if(type.IsValueType)
    { cg.ILG.Emit(OpCodes.Unbox, type);
      if(!useIndirect) cg.EmitIndirectLoad(type);
    }
    else if(type!=typeof(object)) cg.ILG.Emit(OpCodes.Castclass, type);
  }

  delegate object Create(IProcedure proc);

  static readonly Hashtable handlers=new Hashtable(), dsigs=new Hashtable();
  static readonly Index dwi=new Index();
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
      PointerHack = mi.DeclaringType.IsValueType ? mi.MethodHandle.Value : IntPtr.Zero;
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
        IsCons!=o.IsCons || PointerHack!=o.PointerHack ||
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
  public IntPtr PointerHack;
  public bool     IsCons, ParamArray;
}
#endregion

} // namespace NetLisp.Backend