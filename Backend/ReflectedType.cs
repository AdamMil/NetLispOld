using System;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region DelegateProxy
public abstract class DelegateProxy
{ protected DelegateProxy(object callable) { this.callable = callable; }
	protected object callable;

	public static object Make(object callable, Type delegateType)
	{ ConstructorInfo ci;
	  lock(handlers) ci = (ConstructorInfo)handlers[delegateType];
	  if(ci==null)
	  { Type[] ctypes = { typeof(object) };

	    MethodInfo mi = delegateType.GetMethod("Invoke", BindingFlags.Public|BindingFlags.Instance);
		  if(mi==null) throw new ArgumentException("This doesn't seem to be a delegate.", "delegateType");

		  ParameterInfo[] pis = mi.GetParameters();
		  Type[] ptypes = new Type[pis.Length];
		  for(int i=0; i<pis.Length; i++) ptypes[i] = pis[i].ParameterType;

		  Key key = new Key(mi.ReturnType, ptypes);
      lock(sigs) ci = (ConstructorInfo)sigs[key];

		  if(ci==null)
		  { TypeGenerator tg = SnippetMaker.Assembly.DefineType(TypeAttributes.Public|TypeAttributes.Sealed,
		                                                                "EventHandler"+AST.NextIndex,
		                                                                typeof(DelegateProxy));
		    ConstructorInfo pci =
		      typeof(DelegateProxy).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic, null, ctypes, null);
		    CodeGenerator cg = tg.DefineChainedConstructor(pci);
		    cg.EmitReturn();
		    cg.Finish();

		    cg = tg.DefineMethod(MethodAttributes.Public, "Handle", mi.ReturnType, ptypes);
        cg.EmitThis();
        cg.EmitFieldGet(typeof(DelegateProxy).GetField("callable", BindingFlags.Instance|BindingFlags.NonPublic));
        if(pis.Length==0) cg.EmitCall(typeof(Ops), "Call0");
		    else
		    { cg.EmitNewArray(typeof(object), pis.Length);
          for(int i=0; i<pis.Length; i++)
          { cg.ILG.Emit(OpCodes.Dup);
            cg.EmitInt(i);
            cg.EmitArgGet(i);
            cg.ILG.Emit(OpCodes.Stelem_Ref);
          }
          cg.EmitCall(typeof(Ops), "Call", new Type[] { typeof(object), typeof(Type[]) });
		    }
		    if(mi.ReturnType==typeof(void)) cg.ILG.Emit(OpCodes.Pop);
		    else if(mi.ReturnType!=typeof(object))
		    { cg.EmitTypeOf(mi.ReturnType);
		      cg.EmitCall(typeof(Ops), "ConvertTo", new Type[] { typeof(object), typeof(Type) });
		    }
		    cg.EmitReturn();
		    cg.Finish();
		    
		    ci = tg.FinishType().GetConstructor(ctypes);
		    lock(sigs) sigs[key] = ci;
		  }

		  lock(handlers) handlers[delegateType] = ci;
	  }

	  return ci.Invoke(new object[] { callable });
	}

  struct Key
  { public Key(Type returnType, Type[] paramTypes) { ReturnType=returnType; ParamTypes=paramTypes; }

    public override bool Equals(object obj)
    { if(!(obj is Key)) return false;
      Key other = (Key)obj;
      if(ReturnType!=other.ReturnType || ParamTypes.Length!=other.ParamTypes.Length) return false;
      for(int i=0; i<ParamTypes.Length; i++) if(ParamTypes[i]!=other.ParamTypes[i]) return false;
      return true;
    }

    public override int GetHashCode()
    { int hash = ReturnType.GetHashCode();
      for(int i=0; i<ParamTypes.Length; i++) hash ^= ParamTypes[i].GetHashCode();
      return hash;
    }

    Type ReturnType;
    Type[] ParamTypes;
  }

	static Hashtable handlers = new Hashtable();
	static Hashtable sigs = new Hashtable();
}
#endregion

#region ReflectedMember
public class ReflectedMember
{ public ReflectedMember() { }
  public ReflectedMember(string docs) { __doc__=docs; }
  public ReflectedMember(MemberInfo mi)
  { object[] docs = mi.GetCustomAttributes(typeof(DocStringAttribute), false);
    if(docs.Length!=0) __doc__ = ((DocStringAttribute)docs[0]).Docs;
  }

  public string __doc__;
}
#endregion

#region ReflectedConstructor
public sealed class ReflectedConstructor : ReflectedMethodBase
{ public ReflectedConstructor(MethodBase ci) : base(ci) { }
}
#endregion

#region ReflectedEvent
public sealed class ReflectedEvent : ReflectedMember, IDescriptor
{ public ReflectedEvent(EventInfo ei) : base(ei) { info=ei; }
  public ReflectedEvent(EventInfo ei, object instance) : base(ei) { info=ei; this.instance=instance; }

  public void add(object func)
  { Delegate handler = func as Delegate;
    if(handler==null) handler = Ops.MakeDelegate(func, info.EventHandlerType);
    info.AddEventHandler(instance, handler);
  }

  public void sub(object func)
  { Delegate handler = func as Delegate;
    if(handler==null) throw new NotImplementedException("removing non-delegate event handlers");
    info.RemoveEventHandler(instance, handler);
  }

  public object Get(object instance) { return instance==null ? this : new ReflectedEvent(info, instance); }

  internal EventInfo info;
  object instance;
}
#endregion

#region ReflectedField
public sealed class ReflectedField : ReflectedMember, IDataDescriptor
{ public ReflectedField(FieldInfo fi) : base(fi) { info=fi; }

  public object Get(object instance)
  { return instance!=null || info.IsStatic ? info.GetValue(instance) : this;
  }

  public void Set(object instance, object value)
  { if(info.IsInitOnly || info.IsLiteral) throw Ops.TypeError("{0} is a read-only attribute", info.Name);
    if(instance==null && !info.IsStatic) throw Ops.TypeError("{0} is an instance field", info.Name);
    info.SetValue(instance, Ops.ConvertTo(value, info.FieldType));
  }

  internal FieldInfo info;
}
#endregion

#region ReflectedMethod
public sealed class ReflectedMethod : ReflectedMethodBase, IDescriptor
{ public ReflectedMethod(MethodInfo mi) : base(mi) { }
  public ReflectedMethod(MethodBase[] sigs, object instance, string docs) : base(sigs, instance, docs) { }

  public object Get(object instance)
  { return instance==null ? this : new ReflectedMethod(sigs, instance, __doc__);
  }
}
#endregion

// TODO: respect default parameters
#region ReflectedMethodBase
public abstract class ReflectedMethodBase : ReflectedMember, ICallable
{ protected ReflectedMethodBase(MethodBase mb) : base(mb)
  { sigs = new MethodBase[] { mb };
    hasStatic = allStatic = mb.IsStatic;
  }
  protected ReflectedMethodBase(MethodBase[] sigs, object instance, string docs) : base(docs)
  { this.sigs=sigs; this.instance=instance;
  }

  public object Call(params object[] args)
  { Type[] types  = new Type[args.Length];
    Match[] res   = new Match[sigs.Length];
    int bestMatch = -1;

    for(int i=0; i<args.Length; i++) types[i] = args[i]==null ? null : args[i].GetType();

    for(int mi=0; mi<sigs.Length; mi++) // TODO: speed the binding up somehow?
    { if(instance==null && !sigs[mi].IsStatic && !sigs[mi].IsConstructor) continue;
      ParameterInfo[] parms = sigs[mi].GetParameters();
      bool paramArray = parms.Length>0 && IsParamArray(parms[parms.Length-1]);
      int lastRP      = paramArray ? parms.Length-1 : parms.Length;
      if(args.Length<lastRP || !paramArray && args.Length!=parms.Length) continue;

      TryMatch(parms, args, types, lastRP, paramArray, res, mi, ref bestMatch);
    }

    return DoCall(args, types, res, bestMatch);
  }

  internal void Add(MethodBase sig)
  { if(__doc__==null)
    { object[] docs = sig.GetCustomAttributes(typeof(DocStringAttribute), false);
      if(docs.Length!=0) __doc__ = ((DocStringAttribute)docs[0]).Docs;
    }

    if(sig.IsStatic) hasStatic=true;
    else allStatic=false;

    MethodBase[] narr = new MethodBase[sigs.Length+1];
    sigs.CopyTo(narr, 0);
    narr[sigs.Length] = sig;
    sigs = narr;
  }

  struct Match
  { public Match(Conversion conv, ParameterInfo[] parms, int last, byte apa, bool pa)
    { Conv=conv; Parms=parms; Last=last; APA=apa; PA=pa;
    }

    public static bool operator<(Match a, Match b) { return a.Conv<b.Conv || a.PA && (!b.PA || a.APA<b.APA); }
    public static bool operator>(Match a, Match b) { return a.Conv>b.Conv || b.PA && (!a.PA || a.APA>b.APA); }
    public static bool operator==(Match a, Match b) { return a.Conv==b.Conv && a.PA==b.PA && a.APA==b.APA; }
    public static bool operator!=(Match a, Match b) { return a.Conv!=b.Conv || a.PA!=b.PA || a.APA!=b.APA; }
    
    public override bool Equals(object obj) { return  obj is Match ? this==(Match)obj : false; }
    public override int GetHashCode() { throw new NotSupportedException(); }

    public Conversion Conv;
    public ParameterInfo[] Parms;
    public int Last;
    public byte APA;
    public bool PA;
  }

  object DoCall(object[] args, Type[] types, Match[] res, int bestMatch)
  { if(bestMatch==-1) throw new Exception(); // FIXME: throw Ops.TypeError("unable to bind arguments to method '{0}' on {1}", __name__, sigs[0].DeclaringType.FullName);
    Match best = res[bestMatch];

    // check for ambiguous bindings
    if(sigs.Length>1 && best.Conv!=Conversion.Identity)
      for(int i=bestMatch+1; i<res.Length; i++)
        if(res[i]==best)
          throw new Exception(); // FIXME: Ops.TypeError("ambiguous argument types (multiple functions matched method '{0}' on {1})", __name__, sigs[0].DeclaringType.FullName);

    // do the actual conversion
    for(int i=0, end=best.APA==2 ? args.Length : best.Last; i<end; i++)
      args[i] = Ops.ConvertTo(args[i], best.Parms[i].ParameterType);

    if(best.Last!=best.Parms.Length && best.APA==0)
    { object[] narr = new object[best.Parms.Length];
      Array.Copy(args, 0, narr, 0, best.Last);

      Type type = best.Parms[best.Last].ParameterType.GetElementType();
      Array pa = Array.CreateInstance(type, args.Length-best.Last);
      for(int i=0; i<pa.Length; i++) pa.SetValue(Ops.ConvertTo(args[i+best.Last], type), i);
      args=narr; args[best.Last]=pa;
    }
    else if(best.APA==1) // FIXME: aeou
    { /*Type type = best.Parms[best.Last].ParameterType.GetElementType();
      object[] items = ((Tuple)args[best.Last]).items;
      if(type==typeof(object)) args[best.Last] = items;
      else
      { Array pa = Array.CreateInstance(type, items.Length);
        for(int i=0; i<items.Length; i++) pa.SetValue(Ops.ConvertTo(items[i], type), i);
        args[best.Last] = pa;
      }*/
    }

    try
    { return sigs[bestMatch].IsConstructor ? ((ConstructorInfo)sigs[bestMatch]).Invoke(args)
                                           : sigs[bestMatch].Invoke(instance, args);
    }
    catch(TargetInvocationException e) { throw e.InnerException; }
  }

  bool TryMatch(ParameterInfo[] parms, object[] args, Type[] types, int lastRP, bool paramArray,
                Match[] res, int mi, ref int bestMatch)
  { byte alreadyPA = 0;
    res[mi].Conv = Conversion.Identity;

    // check types of all parameters except the parameter array if there is one
    for(int i=0; i<lastRP; i++)
    { Conversion conv = Ops.ConvertTo(types[i], parms[i].ParameterType);
      if(conv==Conversion.None || conv<res[mi].Conv)
      { res[mi].Conv=conv;
        if(conv==Conversion.None) return false;
      }
    }

    if(paramArray)
    { if(args.Length==parms.Length) // check if the last argument is an array already
      { Conversion conv = Ops.ConvertTo(types[lastRP], parms[lastRP].ParameterType);
        if(conv==Conversion.Identity || conv==Conversion.Reference)
        { if(conv<res[mi].Conv) res[mi].Conv=conv;
          alreadyPA = 2;
          goto done;
        }
      }

      // check that all remaining arguments can be converted to the member type of the parameter array
      Type type = parms[lastRP].ParameterType.GetElementType();
      if(args.Length==parms.Length && false) // FIXME: types[lastRP]==typeof(Tuple))
      { /*if(type!=typeof(object))
        { object[] items = ((Tuple)args[lastRP]).items;
          for(int i=0; i<items.Length; i++)
          { Conversion conv = Ops.ConvertTo(items[i].GetType(), type);
            if(conv==Conversion.None) goto notCPA;
          }
        }*/
        alreadyPA = 1;
        goto done;
      }

      notCPA:
      for(int i=lastRP; i<args.Length; i++)
      { Conversion conv = Ops.ConvertTo(types[i], type);
        if(conv==Conversion.None || conv<res[mi].Conv)
        { res[mi].Conv=conv;
          if(conv==Conversion.None) return false;
        }
      }
    }

    done:
    res[mi] = new Match(res[mi].Conv, parms, lastRP, alreadyPA, paramArray);
    if(bestMatch==-1 || res[mi]>res[bestMatch]) { bestMatch=mi; return true; }
    return false;
  }

  static bool IsParamArray(ParameterInfo pi) { return pi.IsDefined(typeof(ParamArrayAttribute), false); }

  internal MethodBase[] sigs;
  internal bool hasStatic, allStatic;

  protected object instance;
}
#endregion

#region ReflectedProperty
public sealed class ReflectedProperty : ReflectedMember, IDataDescriptor
{ public ReflectedProperty(PropertyInfo info) : base(info) { state=new State(info); Add(info); }
  ReflectedProperty(State state, object instance, string docs) : base(docs)
  { this.state=state; this.instance=instance;
  }

  public object Get(object instance)
  { if(state.index) return instance==null ? this : new ReflectedProperty(state, instance, __doc__);
    if(!state.canRead) throw Ops.TypeError("{0} is a non-readable attribute", state.info.Name);
    MethodInfo mi = state.info.GetGetMethod();
    return instance!=null || mi.IsStatic ? mi.Invoke(instance, Ops.EmptyArray) : this;
  }

  public object __getitem__(params object[] args)
  { if(!state.canRead) throw Ops.TypeError("{0} is a non-readable attribute", state.info.Name);
    return ((ReflectedMethod)state.get.Get(instance)).Call(args);
  }

  public void Set(object instance, object value) { setitem(instance, value); }
  public void __setitem__(params object[] args) { setitem(instance, args); }

  internal void Add(PropertyInfo info)
  { if(info.GetIndexParameters().Length>0) state.index = true;

    if(info.CanRead)
    { if(state.get==null) state.get = new ReflectedMethod(info.GetGetMethod());
      else state.get.Add(info.GetGetMethod());
      state.canRead = true;
    }
    if(info.CanWrite)
    { if(state.set==null) state.set = new ReflectedMethod(info.GetSetMethod());
      else state.set.Add(info.GetSetMethod());
      state.canWrite = true;
    }
  }

  void setitem(object instance, params object[] args)
  { if(!state.canWrite) throw Ops.TypeError("{0} is a non-writeable attribute", state.info.Name);
    ((ReflectedMethod)state.set.Get(instance)).Call(args);
  }

  internal class State
  { public State(PropertyInfo info) { this.info=info; }
    public PropertyInfo info;
    public ReflectedMethod get, set;
    public bool canRead, canWrite, index;
  }

  internal State state;

  object instance;
}
#endregion

#region ReflectedType
public sealed class ReflectedType : ICallable, IHasAttributes
{ ReflectedType(Type type) { this.type = type; }

  public ReflectedConstructor Constructor { get { Initialize(); return cons; } }

  public IDictionary Dict { get { Initialize(); return dict; } }

  public object Call(params object[] args)
  { Initialize();
    return cons.Call(args);
  }

  public bool GetAttr(string name, out object value)
  { object slot = RawGetSlot(name);
    if(slot!=Ops.Missing)
    { value = Ops.GetDescriptor(slot, null);
      return true;
    }
    value = null;
    return false;
  }

  public void SetAttr(string name, object value)
  { object slot = RawGetSlot(name);
    if(slot!=Ops.Missing && Ops.SetDescriptor(slot, null, value)) return;
    dict[name] = value;
  }

  public bool GetAttr(object self, string name, out object value)
  { object slot = RawGetSlot(name);
    if(slot!=Ops.Missing)
    { value = Ops.GetDescriptor(slot, self);
      return true;
    }
    value = null;
    return false;
  }

  public void SetAttr(object self, string name, object value)
  { object slot = RawGetSlot(name);
    if(slot==Ops.Missing || !Ops.SetDescriptor(slot, self, value))
    { if(self==null) dict[name] = value;
      else throw new Exception(); // FIXME: throw Ops.AttributeError("no such slot '{0}'", name);
    }
  }

  public static ReflectedType FromType(Type type)
  { ReflectedType rt;
    lock(types) rt = (ReflectedType)types[type];
    if(rt==null)
    { rt = new ReflectedType(type);
      lock(types) types[type] = rt;
    }
    return rt;
  }

  void Initialize()
  { if(initialized) return;
    dict = new HybridDictionary();
    foreach(ConstructorInfo ci in type.GetConstructors()) AddConstructor(ci);
    foreach(EventInfo ei in type.GetEvents()) AddEvent(ei);
    foreach(FieldInfo fi in type.GetFields()) AddField(fi);
    foreach(MethodInfo mi in type.GetMethods()) AddMethod(mi);
    foreach(PropertyInfo pi in type.GetProperties()) AddProperty(pi);
    foreach(Type t in type.GetNestedTypes()) AddNestedType(t);
    
    initialized = true;
  }

  void AddConstructor(ConstructorInfo ci)
  { if(!ci.IsPublic) return;
    if(cons==null) cons = new ReflectedConstructor(ci);
    else cons.Add(ci);
  }

  void AddFakeConstructor(MethodInfo mi)
  { if(cons==null) cons = new ReflectedConstructor(mi);
    else cons.Add(mi);
  }

  void AddEvent(EventInfo ei) { dict[GetName(ei)] = new ReflectedEvent(ei); }
  void AddField(FieldInfo fi) { dict[GetName(fi)] = new ReflectedField(fi); }

  void AddMethod(MethodInfo mi)
  { if(mi.IsSpecialName) return;
    string name = GetName(mi);
    ReflectedMethod rm = (ReflectedMethod)dict[name];
    if(rm==null) dict[name] = new ReflectedMethod(mi);
    else rm.Add(mi);
  }

  void AddNestedType(Type type) { dict[GetName(type)] = ReflectedType.FromType(type); }

  void AddProperty(PropertyInfo pi)
  { string name = GetName(pi);
    ReflectedProperty rp = (ReflectedProperty)dict[name];
    if(rp==null) dict[name] = new ReflectedProperty(pi);
    else rp.Add(pi);
  }

  object RawGetSlot(string name)
  { Initialize();
    object obj = dict[name];
    return obj!=null || dict.Contains(name) ? obj : Ops.Missing;
  }

  ReflectedConstructor cons;
  HybridDictionary dict;
  Type type;
  bool initialized;

  static string GetName(MemberInfo mi)
  { object[] name = mi.GetCustomAttributes(typeof(SymbolNameAttribute), false);
    return name.Length==0 ? mi.Name : ((SymbolNameAttribute)name[0]).Name;
  }

  static readonly Hashtable types=new Hashtable();
}
#endregion

} // namespace NetLisp.Backend