using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

public sealed class CodeGenerator
{ public CodeGenerator(TypeGenerator tg, MethodBase mb, ILGenerator ilg)
  { TypeGenerator = tg; MethodBase = mb; ILG = ilg;
  }

  public Slot AllocLocalTemp(Type type) { return AllocLocalTemp(type, false); }
  public Slot AllocLocalTemp(Type type, bool keepAround)
  { if(localTemps==null) localTemps = new ArrayList();
    if(keepAround && IsGenerator) return Namespace.AllocTemp(type);
    Slot ret;
    for(int i=localTemps.Count-1; i>=0; i--)
    { ret = (Slot)localTemps[i];
      if(ret.Type==type)
      { localTemps.RemoveAt(i);
        return ret;
      }
    }
    return new LocalSlot(ILG.DeclareLocal(type));
  }

  public void EmitArgGet(int index)
  { if(!MethodBase.IsStatic) index++;
    switch(index)
    { case 0: ILG.Emit(OpCodes.Ldarg_0); break;
      case 1: ILG.Emit(OpCodes.Ldarg_1); break;
      case 2: ILG.Emit(OpCodes.Ldarg_2); break;
      case 3: ILG.Emit(OpCodes.Ldarg_3); break;
      default: ILG.Emit(index<256 ? OpCodes.Ldarg_S : OpCodes.Ldarg, index); break;
    }
  }
  public void EmitArgGetAddr(int index)
  { if(!MethodBase.IsStatic) index++;
    ILG.Emit(index<256 ? OpCodes.Ldarga_S : OpCodes.Ldarga, index);
  }
  public void EmitArgSet(int index)
  { if(!MethodBase.IsStatic) index++;
    ILG.Emit(index<256 ? OpCodes.Starg_S : OpCodes.Starg, index);
  }

  public void EmitBool(bool value) { ILG.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); }

  public void EmitCall(ConstructorInfo ci) { ILG.Emit(OpCodes.Call, ci); }
  public void EmitCall(MethodInfo mi)
  { if(mi.IsVirtual && !mi.DeclaringType.IsSealed && !mi.IsFinal) ILG.Emit(OpCodes.Callvirt, mi);
    else ILG.Emit(OpCodes.Call, mi);
  }
  public void EmitCall(Type type, string method) { EmitCall(type.GetMethod(method, SearchAll)); }
  public void EmitCall(Type type, string method, Type[] paramTypes) { EmitCall(type.GetMethod(method, paramTypes)); }

  public void EmitConstant(object value)
  { switch(Convert.GetTypeCode(value))
    { case TypeCode.Byte:   EmitInt((int)(byte)value); break;
      case TypeCode.Char:   EmitInt((int)(char)value); break;
      case TypeCode.Double: ILG.Emit(OpCodes.Ldc_R8, (double)value); break;
      case TypeCode.Empty:  ILG.Emit(OpCodes.Ldnull); break;
      case TypeCode.Int16:  EmitInt((int)(short)value); break;
      case TypeCode.Int32:  EmitInt((int)value); break;
      case TypeCode.Int64:  ILG.Emit(OpCodes.Ldc_I8, (long)value); break;
      case TypeCode.SByte:  EmitInt((int)(sbyte)value); break;
      case TypeCode.Single: ILG.Emit(OpCodes.Ldc_R4, (float)value); break;
      case TypeCode.String: EmitString((string)value); break;
      case TypeCode.UInt16: EmitInt((int)(ushort)value); break;
      case TypeCode.UInt32: EmitInt((int)(uint)value); break;
      case TypeCode.UInt64: ILG.Emit(OpCodes.Ldc_I8, (long)(ulong)value); break;
      default: throw new NotImplementedException("constant: "+value.GetType());
    }
  }

  public void EmitConstantObject(object value)
  { if(value==null) ILG.Emit(OpCodes.Ldnull);
    else if(value is bool) EmitFieldGet(typeof(Ops), (bool)value ? "TRUE" : "FALSE");
    else
    { string s = value as string;
      if(s!=null) EmitString(s);
      else TypeGenerator.GetConstant(value).EmitGet(this);
    }
  }

  public void EmitDouble(double value) { ILG.Emit(OpCodes.Ldc_R8, value); }

  public void EmitExpression(Node e)
  { if(e==null) ILG.Emit(OpCodes.Ldnull);
    else e.Emit(this);
  }

  public void EmitFieldGet(Type type, string name) { EmitFieldGet(type.GetField(name, SearchAll)); }
  public void EmitFieldGet(FieldInfo field)
  { if(field.IsLiteral) EmitConstant(field.GetValue(null));
    else ILG.Emit(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);
  }
  public void EmitFieldGetAddr(Type type, string name) { EmitFieldGetAddr(type.GetField(name, SearchAll)); }
  public void EmitFieldGetAddr(FieldInfo field)
  { if(field.IsLiteral) throw new ArgumentException("Cannot get the address of a literal field");
    ILG.Emit(field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, field);
  }
  public void EmitFieldSet(Type type, string name) { EmitFieldSet(type.GetField(name, SearchAll)); }
  public void EmitFieldSet(FieldInfo field)
  { if(field.IsLiteral) throw new ArgumentException("Cannot set a literal field");
    ILG.Emit(field.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, field);
  }

  public void EmitGet(Name name) { Namespace.GetSlot(name).EmitGet(this); }
  public void EmitSet(Name name) { Namespace.GetSlot(name).EmitSet(this); }

  public void EmitPropGet(Type type, string name) { EmitPropGet(type.GetProperty(name, SearchAll)); }
  public void EmitPropGet(PropertyInfo pi) { EmitCall(pi.GetGetMethod()); }
  public void EmitPropSet(Type type, string name) { EmitPropSet(type.GetProperty(name, SearchAll)); }
  public void EmitPropSet(PropertyInfo pi) { EmitCall(pi.GetSetMethod()); }

  public void EmitInt(int value)
  { OpCode op;
		switch(value)
		{ case -1: op=OpCodes.Ldc_I4_M1; break;
			case  0: op=OpCodes.Ldc_I4_0; break;
			case  1: op=OpCodes.Ldc_I4_1; break;
			case  2: op=OpCodes.Ldc_I4_2; break;
			case  3: op=OpCodes.Ldc_I4_3; break;
			case  4: op=OpCodes.Ldc_I4_4; break;
			case  5: op=OpCodes.Ldc_I4_5; break;
			case  6: op=OpCodes.Ldc_I4_6; break;
			case  7: op=OpCodes.Ldc_I4_7; break;
			case  8: op=OpCodes.Ldc_I4_8; break;
			default:
				if(value>=-128 && value<=127) ILG.Emit(OpCodes.Ldc_I4_S, (byte)value);
				else ILG.Emit(OpCodes.Ldc_I4, value);
				return;
		}
		ILG.Emit(op);
  }
  
  public void EmitIsFalse()
  { EmitIsTrue();
    EmitInt(0);
    ILG.Emit(OpCodes.Ceq);
  }

  public void EmitIsFalse(Node e) { e.Emit(this); EmitIsFalse(); }
  public void EmitIsTrue() { EmitCall(typeof(Ops), "IsTrue"); }
  public void EmitIsTrue(Node e) { e.Emit(this); EmitIsTrue(); }

  public void EmitLine(int line)
  { if(TypeGenerator.Assembly.Symbols!=null)
      ILG.MarkSequencePoint(TypeGenerator.Assembly.Symbols, line, 0, line+1, 0);
  }

  public void EmitNew(Type type) { ILG.Emit(OpCodes.Newobj, type.GetConstructor(Type.EmptyTypes)); }
  public void EmitNew(Type type, Type[] paramTypes)
  { ILG.Emit(OpCodes.Newobj, type.GetConstructor(SearchAll, null, paramTypes, null));
  }
  public void EmitNew(ConstructorInfo ci) { ILG.Emit(OpCodes.Newobj, ci); }

  public void EmitNewArray(Type type, int length)
  { EmitInt(length);
    ILG.Emit(OpCodes.Newarr, type);
  }

  public void EmitObjectArray(Node[] exprs)
  { if(exprs.Length==0) EmitFieldGet(typeof(Ops), "EmptyArray");
    else
    { EmitNewArray(typeof(object), exprs.Length);
      for(int i=0; i<exprs.Length; i++)
      { ILG.Emit(OpCodes.Dup);
        EmitInt(i);
        exprs[i].Emit(this);
        ILG.Emit(OpCodes.Stelem_Ref);
      }
    }
  }

  public void EmitObjectArray(object[] objs)
  { if(objs.Length==0) EmitFieldGet(typeof(Ops), "EmptyArray");
    else
    { EmitNewArray(typeof(object), objs.Length);
      for(int i=0; i<objs.Length; i++)
      { ILG.Emit(OpCodes.Dup);
        EmitInt(i);
        EmitConstantObject(objs[i]);
        ILG.Emit(OpCodes.Stelem_Ref);
      }
    }
  }

  // TODO: make this use actual spans
  public void EmitPosition(Node node)
  { throw new NotImplementedException();
  }

  public void EmitReturn() { ILG.Emit(OpCodes.Ret); }
  public void EmitReturn(Node expr)
  { EmitExpression(expr);
    ILG.Emit(OpCodes.Ret);
  }
  
  public void EmitString(string value)
  { if(value==null) ILG.Emit(OpCodes.Ldnull);
    else ILG.Emit(OpCodes.Ldstr, value);
  }

  public void EmitStringArray(string[] strings)
  { EmitNewArray(typeof(string), strings.Length);
    for(int i=0; i<strings.Length; i++)
    { ILG.Emit(OpCodes.Dup);
      EmitInt(i);
      EmitString(strings[i]);
      ILG.Emit(OpCodes.Stelem_Ref);
    }
  }

  public void EmitThis()
  { if(MethodBase.IsStatic) throw new InvalidOperationException("no 'this' for a static method");
    ILG.Emit(OpCodes.Ldarg_0);
  }
  
  public void EmitTypeOf(Type type)
  { if(type.IsByRef) // TODO: see if there's a better way to do this. this might not even be safe for types in other assemblies. maybe optimize it by caching values?
    { EmitString(type.FullName+"&");
      EmitCall(typeof(Type), "GetType", new Type[] { typeof(string) });
    }
    else
    { ILG.Emit(OpCodes.Ldtoken, type);
      EmitCall(typeof(Type), "GetTypeFromHandle");
    }
  }

  public void Finish()
  {
  }

  public void FreeLocalTemp(Slot slot) { localTemps.Add(slot); }

  public Namespace Namespace;
  public bool IsGenerator;

  public readonly TypeGenerator TypeGenerator;
  public readonly MethodBase MethodBase;
  public readonly ILGenerator   ILG;

  const BindingFlags SearchAll = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;

  ArrayList localTemps;
}

} // namespace NetLisp.Backend
