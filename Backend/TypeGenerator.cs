using System;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

public sealed class TypeGenerator
{ public TypeGenerator(AssemblyGenerator assembly, TypeBuilder typeBuilder)
  { Assembly=assembly; TypeBuilder=typeBuilder;
  }

  public Type BaseType { get { return TypeBuilder.BaseType; } }

  public CodeGenerator DefineConstructor(Type[] types) { return DefineConstructor(MethodAttributes.Public, types); }
  public CodeGenerator DefineConstructor(MethodAttributes attrs, Type[] types)
  { ConstructorBuilder cb = TypeBuilder.DefineConstructor(attrs, CallingConventions.Standard, types);
    return new CodeGenerator(this, cb, cb.GetILGenerator());
  }

  public CodeGenerator DefineChainedConstructor(Type[] paramTypes)
  { return DefineChainedConstructor(TypeBuilder.BaseType.GetConstructor(paramTypes));
  }
  public CodeGenerator DefineChainedConstructor(ConstructorInfo parent)
  { ParameterInfo[] pi = parent.GetParameters();
    Type[] types = GetParamTypes(pi);
    ConstructorBuilder cb = TypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, types);
    for(int i=0; i<pi.Length; i++)
    { ParameterBuilder pb = cb.DefineParameter(i+1, pi[i].Attributes, pi[i].Name);
      if(pi[i].IsDefined(typeof(ParamArrayAttribute), false))
        pb.SetCustomAttribute(
          new CustomAttributeBuilder(typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes), Ops.EmptyArray));
    }
    
    CodeGenerator cg = new CodeGenerator(this, cb, cb.GetILGenerator());
    cg.EmitThis();
    for(int i=0; i<pi.Length; i++) cg.EmitArgGet(i);
    cg.ILG.Emit(OpCodes.Call, parent);
    return cg;
  }

  public CodeGenerator DefineDefaultConstructor(MethodAttributes attrs)
  { ConstructorBuilder cb = TypeBuilder.DefineDefaultConstructor(attrs);
    return new CodeGenerator(this, cb, cb.GetILGenerator());
  }

  public Slot DefineField(string name, Type type) { return DefineField(FieldAttributes.Public, name, type); }
  public Slot DefineField(FieldAttributes attrs, string name, Type type)
  { return new FieldSlot(new ThisSlot(TypeBuilder), TypeBuilder.DefineField(name, type, attrs));
  }

  public CodeGenerator DefineMethod(string name, Type retType, Type[] paramTypes)
  { return DefineMethod(MethodAttributes.Public|MethodAttributes.Static, name, retType, paramTypes);
  }
  public CodeGenerator DefineMethod(MethodAttributes attrs, string name, Type retType, Type[] paramTypes)
  { MethodBuilder mb = TypeBuilder.DefineMethod(name, attrs, retType, paramTypes);
    return new CodeGenerator(this, mb, mb.GetILGenerator());
  }

  public CodeGenerator DefineMethodOverride(string name)
  { return DefineMethodOverride(TypeBuilder.BaseType, name, false);
  }
  public CodeGenerator DefineMethodOverride(string name, bool final)
  { return DefineMethodOverride(TypeBuilder.BaseType.GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public),
                                final);
  }
  public CodeGenerator DefineMethodOverride(Type type, string name) { return DefineMethodOverride(type, name, false); }
  public CodeGenerator DefineMethodOverride(Type type, string name, bool final)
  { return DefineMethodOverride(type.GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public),
                                final);
  }
  public CodeGenerator DefineMethodOverride(MethodInfo baseMethod) { return DefineMethodOverride(baseMethod, false); }
  public CodeGenerator DefineMethodOverride(MethodInfo baseMethod, bool final)
  { MethodAttributes attrs = baseMethod.Attributes & ~(MethodAttributes.Abstract|MethodAttributes.NewSlot) |
                             MethodAttributes.HideBySig;
    if(final) attrs |= MethodAttributes.Final;
    MethodBuilder mb = TypeBuilder.DefineMethod(baseMethod.Name, attrs, baseMethod.ReturnType,
                                                GetParamTypes(baseMethod.GetParameters()));
    // TODO: figure out how to use this properly
    //TypeBuilder.DefineMethodOverride(mb, baseMethod);
    return new CodeGenerator(this, mb, mb.GetILGenerator());
  }

  public TypeGenerator DefineNestedType(string name, Type parent) { return DefineNestedType(0, name, parent); }
  public TypeGenerator DefineNestedType(TypeAttributes attrs, string name, Type parent)
  { if(nestedTypes==null) nestedTypes = new ArrayList();
    TypeAttributes ta = attrs | TypeAttributes.Class | TypeAttributes.NestedPublic;
    TypeGenerator ret = new TypeGenerator(Assembly, TypeBuilder.DefineNestedType(name, ta, parent));
    nestedTypes.Add(ret);
    return ret;
  }

  public CodeGenerator DefineProperty(string name, Type type)
  { return DefineProperty(MethodAttributes.Public, name, type, Type.EmptyTypes);
  }
  public CodeGenerator DefineProperty(MethodAttributes attrs, string name, Type type)
  { return DefineProperty(attrs, name, type, Type.EmptyTypes);
  }
  public CodeGenerator DefineProperty(string name, Type type, Type[] paramTypes)
  { return DefineProperty(MethodAttributes.Public, name, type, paramTypes);
  }
  public CodeGenerator DefineProperty(MethodAttributes attrs, string name, Type type, Type[] paramTypes)
  { PropertyBuilder pb = TypeBuilder.DefineProperty(name, PropertyAttributes.None, type, paramTypes);
    CodeGenerator cg = DefineMethod(attrs, "get_"+name, type, paramTypes);
    pb.SetGetMethod((MethodBuilder)cg.MethodBase);
    return cg;
  }

  public void DefineProperty(string name, Type type, out CodeGenerator get, out CodeGenerator set)
  { DefineProperty(MethodAttributes.Public, name, type, Type.EmptyTypes, out get, out set);
  }
  public void DefineProperty(MethodAttributes attrs, string name, Type type,
                             out CodeGenerator get, out CodeGenerator set)
  { DefineProperty(attrs, name, type, Type.EmptyTypes, out get, out set);
  }
  public void DefineProperty(string name, Type type, Type[] paramTypes, out CodeGenerator get, out CodeGenerator set)
  { DefineProperty(MethodAttributes.Public, name, type, paramTypes, out get, out set);
  }
  public void DefineProperty(MethodAttributes attrs, string name, Type type, Type[] paramTypes,
                             out CodeGenerator get, out CodeGenerator set)
  { PropertyBuilder pb = TypeBuilder.DefineProperty(name, PropertyAttributes.None, type, paramTypes);
    get = DefineMethod(attrs, "get_"+name, type, paramTypes);
    set = DefineMethod(attrs, "set_"+name, null, paramTypes);
    pb.SetGetMethod((MethodBuilder)get.MethodBase);
    pb.SetSetMethod((MethodBuilder)set.MethodBase);
  }

  public CodeGenerator DefinePropertyOverride(string name)
  { return DefinePropertyOverride(TypeBuilder.BaseType, name, false);
  }
  public CodeGenerator DefinePropertyOverride(string name, bool final)
  { return DefinePropertyOverride(TypeBuilder.BaseType, name, final);
  }
  public CodeGenerator DefinePropertyOverride(Type type, string name)
  { return DefinePropertyOverride(type.GetProperty(name), false);
  }
  public CodeGenerator DefinePropertyOverride(Type type, string name, bool final)
  { return DefinePropertyOverride(type.GetProperty(name), final);
  }
  public CodeGenerator DefinePropertyOverride(PropertyInfo baseProp)
  { return DefinePropertyOverride(baseProp, true);
  }
  public CodeGenerator DefinePropertyOverride(PropertyInfo baseProp, bool final)
  { return DefineMethodOverride(baseProp.CanRead ? baseProp.GetGetMethod() : baseProp.GetSetMethod(), final);
  }

  public void DefinePropertyOverride(string name, out CodeGenerator get, out CodeGenerator set)
  { DefinePropertyOverride(TypeBuilder.BaseType, name, false, out get, out set);
  }
  public void DefinePropertyOverride(string name, bool final, out CodeGenerator get, out CodeGenerator set)
  { DefinePropertyOverride(TypeBuilder.BaseType, name, final, out get, out set);
  }
  public void DefinePropertyOverride(Type type, string name, out CodeGenerator get, out CodeGenerator set)
  { DefinePropertyOverride(type.GetProperty(name), false, out get, out set);
  }
  public void DefinePropertyOverride(Type type, string name, bool final, out CodeGenerator get, out CodeGenerator set)
  { DefinePropertyOverride(type.GetProperty(name), final, out get, out set);
  }
  public void DefinePropertyOverride(PropertyInfo baseProp, out CodeGenerator get, out CodeGenerator set)
  { DefinePropertyOverride(baseProp, false, out get, out set);
  }
  public void DefinePropertyOverride(PropertyInfo baseProp, bool final, out CodeGenerator get, out CodeGenerator set)
  { get = baseProp.CanRead  ? DefineMethodOverride(baseProp.GetGetMethod(), final) : null;
    set = baseProp.CanWrite ? DefineMethodOverride(baseProp.GetSetMethod(), final) : null;
  }

  public Slot DefineStaticField(string name, Type type)
  { return DefineStaticField(FieldAttributes.Public, name, type);
  }
  public Slot DefineStaticField(FieldAttributes attrs, string name, Type type)
  { return new StaticSlot(TypeBuilder.DefineField(name, type, attrs|FieldAttributes.Static));
  }

  public Type FinishType()
  { if(initGen!=null)
    { initGen.EmitReturn();
      initGen.Finish();
    }
    Type ret = TypeBuilder.CreateType();
    if(nestedTypes!=null) foreach(TypeGenerator tg in nestedTypes) tg.FinishType();
    return ret;
  }

  public bool GetNamedConstant(string name, Type type, out Slot slot)
  { slot = (Slot)namedConstants[name];
    if(slot==null)
    { slot = new StaticSlot(TypeBuilder.DefineField("c$"+numConstants++, type, FieldAttributes.Static));
      return false;
    }
    return true;
  }

  public Slot GetConstant(object value)
  { Slot slot;
    bool hash = Convert.GetTypeCode(value)!=TypeCode.Object || value is Symbol || value is Binding;

    if(hash) slot = (Slot)constants[value];
    else
    { if(constobjs==null) { constobjs = new ArrayList(); constslots = new ArrayList(); }
      else
      { int index=-1;
        if(value is string[])
        { string[] val = (string[])value;
          for(int i=0; i<constobjs.Count; i++)
          { string[] other = constobjs[i] as string[];
            if(other!=null && val.Length==other.Length)
            { for(int j=0; j<val.Length; j++) if(val[j] != other[j]) goto nextSA;
              index = i;
              break;
            }
            nextSA:;
          }
        }
        else index = constobjs.IndexOf(value);

        if(index!=-1) return (Slot)constslots[index];
      }
      slot = null;
    }

    if(slot==null)
    { Type type = Convert.GetTypeCode(value)==TypeCode.Object ? value.GetType() : typeof(object);
      FieldBuilder fb = TypeBuilder.DefineField("c$"+numConstants++, type, FieldAttributes.Static);
      slot = new StaticSlot(fb);
      if(hash) constants[value] = slot;
      else { constobjs.Add(value); constslots.Add(slot); }
      EmitConstantInitializer(value);
      initGen.EmitFieldSet(fb);
    }
    return slot;
  }

  public CodeGenerator GetInitializer()
  { if(initGen==null)
    { ConstructorBuilder cb = TypeBuilder.DefineTypeInitializer();
      initGen = new CodeGenerator(this, cb, cb.GetILGenerator());
    }
    return initGen;
  }

  public AssemblyGenerator Assembly;
  public TypeBuilder TypeBuilder;

  void EmitConstantInitializer(object value)
  { CodeGenerator cg = GetInitializer();

    switch(Convert.GetTypeCode(value))
    { case TypeCode.Byte:   cg.EmitInt((int)(byte)value); goto box;
      case TypeCode.Char:   cg.EmitInt((int)(char)value); goto box;
      case TypeCode.Double: cg.ILG.Emit(OpCodes.Ldc_R8, (double)value); goto box;
      case TypeCode.Int16:  cg.EmitInt((int)(short)value); goto box;
      case TypeCode.Int32:  cg.EmitInt((int)value); goto box;
      case TypeCode.Int64:  cg.ILG.Emit(OpCodes.Ldc_I8, (long)value); goto box;
      case TypeCode.Object:
        if(value is Symbol)
        { Symbol sym = (Symbol)value;
          cg.EmitString(sym.Name);
          cg.EmitCall(typeof(Symbol), "Get");
        }
        else if(value is Binding)
        { cg.EmitFieldGet(typeof(TopLevel), "Current");
          cg.EmitString(((Binding)value).Name);
          cg.EmitCall(typeof(TopLevel), "GetBinding");
        }
        else if(value is string[]) cg.EmitStringArray((string[])value);
        else goto default;
        return;
      case TypeCode.SByte:  cg.EmitInt((int)(sbyte)value); goto box;
      case TypeCode.Single: cg.ILG.Emit(OpCodes.Ldc_R4, (float)value); goto box;
      case TypeCode.UInt16: cg.EmitInt((int)(ushort)value); goto box;
      case TypeCode.UInt32: cg.EmitInt((int)(uint)value); goto box;
      case TypeCode.UInt64: cg.ILG.Emit(OpCodes.Ldc_I8, (long)(ulong)value); goto box;
      default: throw new NotImplementedException("constant: "+value.GetType());
    }

    box: cg.ILG.Emit(OpCodes.Box, value.GetType());
  }

  Type[] GetParamTypes(ParameterInfo[] pi)
  { Type[] paramTypes = new Type[pi.Length];
    for(int i=0; i<pi.Length; i++) paramTypes[i] = pi[i].ParameterType;
    return paramTypes;
  }

  HybridDictionary constants=new HybridDictionary(), namedConstants=new HybridDictionary();
  ArrayList nestedTypes, constobjs, constslots;
  CodeGenerator initGen;
  int numConstants;
}

} // namespace NetLisp.Backend
