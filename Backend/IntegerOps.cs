using System;
using System.Collections;
using System.Globalization;

namespace NetLisp.Backend
{

public sealed class IntegerOps
{ IntegerOps() { }

  public static object Add(Integer a, object b)
  { Integer ret;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: ret = (bool)b ? a+1 : a; break;
      case TypeCode.Byte: ret = a + (byte)b; break;
      case TypeCode.Char: ret = a + (int)(char)b; break; // TODO: see whether this should return int or char
      case TypeCode.Double: return a.ToDouble() + (double)b;
      case TypeCode.Decimal: return a.ToDecimal() + (Decimal)b;
      case TypeCode.Int16: ret = a + (short)b; break;
      case TypeCode.Int32: ret = a + (int)b; break;
      case TypeCode.Int64: ret = a + (long)b; break;
      case TypeCode.Object:
        if(b is Integer) { ret = a + (Integer)b; break; }
        else if(b is Complex) return a.ToDouble() + (Complex)b;
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          return a + ic.ToInt64(NumberFormatInfo.InvariantInfo);
        }
      case TypeCode.SByte: ret = a + (sbyte)b; break;
      case TypeCode.Single: return a.ToSingle() + (float)b;
      case TypeCode.UInt16: ret = a + (ushort)b; break;
      case TypeCode.UInt32: ret = a + (uint)b; break;
      case TypeCode.UInt64: ret = a + (ulong)b; break;
      default: throw Ops.TypeError("invalid operand types for +: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    return Reduce(ret);
  }

  public static object BitwiseAnd(Integer a, object b)
  { Integer ret;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? (a&1).ToInt32() : 0;
      case TypeCode.Byte: ret = a & (byte)b; break;
      case TypeCode.Int16: ret = a & (short)b; break;
      case TypeCode.Int32: ret = a & (int)b; break;
      case TypeCode.Int64: ret = a & (long)b; break;
      case TypeCode.Object:
        if(b is Integer) { ret = a & (Integer)b; break; }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          return a & ic.ToInt64(NumberFormatInfo.InvariantInfo);
        }
      case TypeCode.SByte: ret = a & (sbyte)b; break;
      case TypeCode.UInt16: ret = a & (ushort)b; break;
      case TypeCode.UInt32: ret = a & (uint)b; break;
      case TypeCode.UInt64: ret = a & (ulong)b; break;
      default: throw Ops.TypeError("invalid operand types for &: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    return Reduce(ret);
  }

  public static object BitwiseOr(Integer a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a|1 : a;
      case TypeCode.Byte: return a | (byte)b;
      case TypeCode.Int16: return a | (short)b;
      case TypeCode.Int32: return a | (int)b;
      case TypeCode.Int64: return a | (long)b;
      case TypeCode.Object:
        if(b is Integer) return a | (Integer)b;
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a | ic.ToInt64(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a | (sbyte)b;
      case TypeCode.UInt16: return a | (ushort)b;
      case TypeCode.UInt32: return a | (uint)b;
      case TypeCode.UInt64: return a | (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for |: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object BitwiseXor(Integer a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a^1 : a;
      case TypeCode.Byte: return a ^ (byte)b;
      case TypeCode.Int16: return a ^ (short)b;
      case TypeCode.Int32: return a ^ (int)b;
      case TypeCode.Int64: return a ^ (long)b;
      case TypeCode.Object:
        if(b is Integer) return a ^ (Integer)b;
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a ^ ic.ToInt64(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a ^ (sbyte)b;
      case TypeCode.UInt16: return a ^ (ushort)b;
      case TypeCode.UInt32: return a ^ (uint)b;
      case TypeCode.UInt64: return a ^ (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for ^: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static int Compare(Integer a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return a.CompareTo((bool)b ? 1 : 0);
      case TypeCode.Byte: return a.CompareTo((uint)(byte)b);
      case TypeCode.Char: case TypeCode.String: return -1;
      case TypeCode.Empty: return 1;
      case TypeCode.Int16: return a.CompareTo((int)(short)b);
      case TypeCode.Int32: return a.CompareTo((int)b);
      case TypeCode.Int64: return a.CompareTo((long)b);
      case TypeCode.Object:
        if(b is Integer) return a.CompareTo((Integer)b);
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a.CompareTo(ic.ToInt64(NumberFormatInfo.InvariantInfo));
        break;
      case TypeCode.SByte: return a.CompareTo((int)(sbyte)b);
      case TypeCode.UInt16: return a.CompareTo((uint)(ushort)b);
      case TypeCode.UInt32: return a.CompareTo((uint)b);
      case TypeCode.UInt64: return a.CompareTo((ulong)b);
    }
    throw Ops.TypeError("can't compare '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object Divide(Integer a, object b)
  { Integer bv;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv = new Integer((bool)b ? 1 : 0); break;
      case TypeCode.Byte: bv = new Integer((byte)b); break;
      case TypeCode.Double: case TypeCode.Single: case TypeCode.Decimal: return FloatOps.Divide(a.ToDouble(), b);
      case TypeCode.Int16: bv = new Integer((short)b); break;
      case TypeCode.Int32: bv = new Integer((int)b); break;
      case TypeCode.Int64: bv = new Integer((long)b); break;
      case TypeCode.Object:
        if(b is Integer) bv = (Integer)b;
        else if(b is Complex) return a.ToDouble() / (Complex)b;
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          bv = new Integer(ic.ToInt64(NumberFormatInfo.InvariantInfo));
        }
        break;
      case TypeCode.SByte: bv = new Integer((sbyte)b); break;
      case TypeCode.UInt16: bv = new Integer((ushort)b); break;
      case TypeCode.UInt32: bv = new Integer((uint)b); break;
      case TypeCode.UInt64: bv = new Integer((ulong)b); break;
      default: throw Ops.TypeError("invalid operand types for /: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    if(bv.Sign==0) throw new DivideByZeroException("long division by zero");

    Integer res = a/bv;
    return res*bv==a ? Reduce(res) : (object)(a.ToDouble()/bv.ToDouble());
  }

  public static object FloorDivide(Integer a, object b)
  { Integer bv;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv = new Integer((bool)b ? 1 : 0); break;
      case TypeCode.Byte: bv = new Integer((byte)b); break;
      case TypeCode.Double: case TypeCode.Single: case TypeCode.Decimal: return FloatOps.Divide(a.ToDouble(), b);
      case TypeCode.Int16: bv = new Integer((short)b); break;
      case TypeCode.Int32: bv = new Integer((int)b); break;
      case TypeCode.Int64: bv = new Integer((long)b); break;
      case TypeCode.Object:
        if(b is Integer) bv = (Integer)b;
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          bv = new Integer(ic.ToInt64(NumberFormatInfo.InvariantInfo));
        }
        break;
      case TypeCode.SByte: bv = new Integer((sbyte)b); break;
      case TypeCode.UInt16: bv = new Integer((ushort)b); break;
      case TypeCode.UInt32: bv = new Integer((uint)b); break;
      case TypeCode.UInt64: bv = new Integer((ulong)b); break;
      default: throw Ops.TypeError("invalid operand types for /: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    if(bv.Sign==0) throw new DivideByZeroException("long floor division by zero");
    return Reduce((a.Sign==-1 ? (a-bv+bv.Sign) : a) / bv);
  }

  public static object LeftShift(Integer a, object b)
  { if(a.Sign==0) return a;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a<<1 : a;
      case TypeCode.Byte: return a << (byte)b;
      case TypeCode.Int16: return a << (short)b;
      case TypeCode.Int32: return a << (int)b;
      case TypeCode.Int64:
      { long lv = (long)b;
        if(lv>int.MaxValue || lv<int.MinValue) throw new OverflowException("long int too large to convert to int");
        return a << (int)lv;
      }
      case TypeCode.Object:
        if(b is Integer) return a << ((Integer)b).ToInt32();
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a << ic.ToInt32(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a << (sbyte)b;
      case TypeCode.UInt16: return a << (ushort)b;
      case TypeCode.UInt32:
      { uint ui = (uint)b;
        if(ui>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        return a << (int)ui;
      }
      case TypeCode.UInt64:
      { ulong ul = (uint)b;
        if(ul>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        return a << (int)ul;
      }
    }
    throw Ops.TypeError("invalid operand types for <<: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object Modulus(Integer a, object b)
  { Integer ret;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: ret = a % ((bool)b ? 1 : 0); break;
      case TypeCode.Byte: ret = a % (byte)b; break;
      case TypeCode.Decimal:
        return Math.IEEERemainder(a.ToDouble(), ((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo));
      case TypeCode.Double: return Math.IEEERemainder(a.ToDouble(), (double)b);
      case TypeCode.Int16: ret = a % (short)b; break;
      case TypeCode.Int32: ret = a % (int)b; break;
      case TypeCode.Int64: ret = a % (long)b; break;
      case TypeCode.Object:
        if(b is Integer) { ret = a % (Integer)b; break; }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          return a % ic.ToInt64(NumberFormatInfo.InvariantInfo);
        }
      case TypeCode.SByte: ret = a % (sbyte)b; break;
      case TypeCode.Single: return Math.IEEERemainder(a.ToDouble(), (float)b);
      case TypeCode.UInt16: ret = a % (ushort)b; break;
      case TypeCode.UInt32: ret = a % (uint)b; break;
      case TypeCode.UInt64: ret = a % (ulong)b; break;
      default: throw Ops.TypeError("invalid operand types for %: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    return Reduce(ret);
  }

  public static object Multiply(Integer a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a : (object)0;
      case TypeCode.Byte: return a * (byte)b;
      case TypeCode.Int16: return a * (short)b;
      case TypeCode.Int32: return a * (int)b;
      case TypeCode.Int64: return a * (long)b;
      case TypeCode.Object:
        if(b is Integer) return a * (Integer)b;
        if(b is Complex) return a.ToDouble() * (Complex)b;
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a * ic.ToInt64(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a * (sbyte)b;
      case TypeCode.String: return StringOps.Multiply((string)b, a);
      case TypeCode.UInt16: return a * (ushort)b;
      case TypeCode.UInt32: return a * (uint)b;
      case TypeCode.UInt64: return a * (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for *: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object Power(Integer a, object b)
  { long bv;
    if(b is int) bv = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? (object)a : a<0 ? -1 : 1;
      case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Decimal:
      { double d = ((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo);
        bv = (long)d;
        if(bv!=d) return Math.Pow(a.ToDouble(), d);
        break;
      }
      case TypeCode.Double:
      { double d = (double)b;
        bv = (long)d;
        if(bv!=d) return Math.Pow(a.ToDouble(), d);
        break;
      }
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int32: bv = (int)b; break;
      case TypeCode.Int64: bv = (long)b; break;
      case TypeCode.Object:
        if(b is Integer)
        { Integer bi = (Integer)b;
          if(bi.Sign<0) return Math.Pow(a.ToDouble(), bi.ToDouble());
          if(bi>uint.MaxValue) throw Ops.ValueError("exponent too big to fit into uint");
          bv = bi.ToUInt32();
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          bv = ic.ToInt64(NumberFormatInfo.InvariantInfo);
        }
        break;
      case TypeCode.SByte: bv = (sbyte)b; break;
      case TypeCode.Single:
      { float f = (float)b;
        bv = (long)f;
        if(bv!=f) return Math.Pow(a.ToDouble(), f);
        break;
      }
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32: bv = (uint)b; break;
      case TypeCode.UInt64:
      { ulong v = (ulong)b;
        if(v>uint.MaxValue) throw Ops.ValueError("exponent too big to fit into uint");
        bv = (long)v;
        break;
      }
      default:
        throw Ops.TypeError("invalid operand types for **: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }

    if(bv<0) return Math.Pow(a.ToDouble(), bv);
    if(bv>uint.MaxValue) throw Ops.ValueError("exponent too big to fit into uint");
    return a.Pow((uint)bv);
  }

  public static object PowerMod(Integer a, object b, object c)
  { long bv;
    if(b is int) bv = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? (object)a : a<0 ? -1 : 1;
      case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int32: bv = (int)b; break;
      case TypeCode.Int64: bv = (long)b; break;
      case TypeCode.Object:
        if(b is Integer)
        { Integer bi = (Integer)b;
          if(bi.Sign<0) throw Ops.ValueError("the exponent cannot be negative for ternary pow");
          if(bi>uint.MaxValue) throw Ops.ValueError("exponent too big to fit into uint");
          bv = bi.ToUInt32();
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          bv = ic.ToInt64(NumberFormatInfo.InvariantInfo);
        }
        break;
      case TypeCode.SByte: bv = (sbyte)b; break;
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32: bv = (uint)b; break;
      case TypeCode.UInt64:
      { ulong v = (ulong)b;
        if(v>uint.MaxValue) throw Ops.ValueError("exponent too big to fit into uint");
        bv = (long)v;
        break;
      }
      default: throw Ops.TypeError("invalid operand types for ternary pow: '{0}', '{1}', and '{2}'",
                                   Ops.TypeName(a), Ops.TypeName(b), Ops.TypeName(c));
    }

    if(bv<0) throw Ops.ValueError("the exponent cannot be negative for ternary pow");
    if(bv>uint.MaxValue) throw Ops.ValueError("exponent too big to fit into uint");
    return Reduce(a.Pow((uint)bv, c));
  }

  public static object Subtract(Integer a, object b)
  { Integer ret;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: ret = (bool)b ? a-1 : a; break;
      case TypeCode.Byte: ret = a - (byte)b; break;
      case TypeCode.Char: ret = a - (int)(char)b; break; // TODO: see whether this should return int or char
      case TypeCode.Double: return a.ToDouble() - (double)b;
      case TypeCode.Decimal: return a.ToDecimal() - (Decimal)b;
      case TypeCode.Int16: ret = a - (short)b; break;
      case TypeCode.Int32: ret = a - (int)b; break;
      case TypeCode.Int64: ret = a - (long)b; break;
      case TypeCode.Object:
        if(b is Integer) { ret = a - (Integer)b; break; }
        else if(b is Complex) return a.ToDouble() - (Complex)b;
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          return a - ic.ToInt64(NumberFormatInfo.InvariantInfo);
        }
      case TypeCode.SByte: ret = a - (sbyte)b; break;
      case TypeCode.Single: return a.ToSingle() - (float)b;
      case TypeCode.UInt16: ret = a - (ushort)b; break;
      case TypeCode.UInt32: ret = a - (uint)b; break;
      case TypeCode.UInt64: ret = a - (ulong)b; break;
      default: throw Ops.TypeError("invalid operand types for -: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    return Reduce(ret);
  }

  public static object RightShift(Integer a, object b)
  { Integer ret;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: ret = (bool)b ? a>>1 : a; break;
      case TypeCode.Byte: ret = a >> (byte)b; break;
      case TypeCode.Int16: ret = a >> (short)b; break;
      case TypeCode.Int32: ret = a >> (int)b; break;
      case TypeCode.Int64:
      { long lv = (long)b;
        if(lv>int.MaxValue || lv<int.MinValue) throw new OverflowException("long int too large to convert to int");
        ret = a >> (int)lv;
        break;
      }
      case TypeCode.Object:
        if(b is Integer) { ret = a >> ((Integer)b).ToInt32(); break; }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          return a >> ic.ToInt32(NumberFormatInfo.InvariantInfo);
        }
      case TypeCode.SByte: ret = a >> (sbyte)b; break;
      case TypeCode.UInt16: ret = a >> (ushort)b; break;
      case TypeCode.UInt32:
      { uint ui = (uint)b;
        if(ui>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        ret = a >> (int)ui;
        break;
      }
      case TypeCode.UInt64:
      { ulong ul = (uint)b;
        if(ul>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        ret = a >> (int)ul;
        break;
      }
      default: throw Ops.TypeError("invalid operand types for >>: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    return Reduce(ret);
  }
  
  internal static object Reduce(Integer ret)
  { return ret.length>1 ? ret : ret.length==0 ? 0 : (object)ret.ToInt64();
  }
}

} // namespace NetLisp.Backend