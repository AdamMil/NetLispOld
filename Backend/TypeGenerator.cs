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

  public Slot DefineField(string name, Type type) { return DefineField(name, type, FieldAttributes.Public); }
  public Slot DefineField(string name, Type type, FieldAttributes access)
  { return new FieldSlot(new ThisSlot(TypeBuilder), TypeBuilder.DefineField(name, type, access));
  }

  public CodeGenerator DefineMethod(string name, Type retType, Type[] paramTypes)
  { return DefineMethod(MethodAttributes.Public|MethodAttributes.Static, name, retType, paramTypes);
  }
  public CodeGenerator DefineMethod(MethodAttributes attrs, string name, Type retType, Type[] paramTypes)
  { MethodBuilder mb = TypeBuilder.DefineMethod(name, attrs, retType, paramTypes);
    return new CodeGenerator(this, mb, mb.GetILGenerator());
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

  public Slot DefineStaticField(string name, Type type)
  { return new StaticSlot(TypeBuilder.DefineField(name, type, FieldAttributes.Public|FieldAttributes.Static));
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

  public Slot GetConstant(object value)
  { Slot slot;
    bool hash = Convert.GetTypeCode(value)!=TypeCode.Object || !(value is Symbol || value is Pair);

    if(hash) slot = (Slot)constants[value];
    else
    { if(constobjs==null) { constobjs = new ArrayList(); constslots = new ArrayList(); }
      else
      { int index = constobjs.IndexOf(value);
        if(index!=-1) return (Slot)constslots[index];
      }
      slot = null;
    }

    if(slot==null)
    { FieldBuilder fb = TypeBuilder.DefineField("c$"+constants.Count, typeof(object), FieldAttributes.Static);
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
    { case TypeCode.Double:
        cg.ILG.Emit(OpCodes.Ldc_R8, (double)value);
        cg.ILG.Emit(OpCodes.Box, typeof(double));
        break;
      case TypeCode.Int32:
        cg.EmitInt((int)value);
        cg.ILG.Emit(OpCodes.Box, typeof(int));
        break;
      case TypeCode.Int64:
        cg.ILG.Emit(OpCodes.Ldc_I8, (long)value);
        cg.ILG.Emit(OpCodes.Box, typeof(long));
        break;
      case TypeCode.Object:
        if(value is Symbol)
        { Symbol sym = (Symbol)value;
          cg.EmitString(sym.Name);
          cg.EmitCall(typeof(Symbol), "Get");
        }
        else goto default;
        break;
      default: throw new NotImplementedException("constant: "+value.GetType());
    }
  }

  Type[] GetParamTypes(ParameterInfo[] pi)
  { Type[] paramTypes = new Type[pi.Length];
    for(int i=0; i<pi.Length; i++) paramTypes[i] = pi[i].ParameterType;
    return paramTypes;
  }

  HybridDictionary constants = new HybridDictionary();
  ArrayList nestedTypes, constobjs, constslots;
  CodeGenerator initGen;
}

} // namespace NetLisp.Backend
