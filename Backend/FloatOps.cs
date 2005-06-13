using System;
using System.Globalization;

namespace NetLisp.Backend
{

public sealed class FloatOps
{ FloatOps() { }

  public static object Add(double a, object b)
  { if(b is double) return a + (double)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a+1 : a;
      case TypeCode.Byte: return a + (byte)b;
      case TypeCode.Decimal: return new Decimal(a) + (Decimal)b;
      case TypeCode.Int16: return a + (short)b;
      case TypeCode.Int32: return a + (int)b;
      case TypeCode.Int64: return a + (long)b;
      case TypeCode.Object:
        if(b is Integer) return a + ((Integer)b).ToDouble();
        if(b is Complex) return a + (Complex)b;
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a + ic.ToDouble(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a + (sbyte)b;
      case TypeCode.Single: return a + (float)b;
      case TypeCode.UInt16: return a + (ushort)b;
      case TypeCode.UInt32: return a + (uint)b;
      case TypeCode.UInt64: return a + (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for +: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static int Compare(double a, object b)
  { double bv;
    if(b is double) bv = (double)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv=(bool)b ? 1 : 0; break;
      case TypeCode.Byte: bv=(byte)b; break;
      case TypeCode.Char: case TypeCode.String: return -1;
      case TypeCode.Decimal:
        bv=((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo); break;
      case TypeCode.Empty: return 1;
      case TypeCode.Int16: bv=(short)b; break;
      case TypeCode.Int32: bv=(int)b; break;
      case TypeCode.Int64: bv=(long)b; break;
      case TypeCode.Object:
        if(b is Integer) bv = ((Integer)b).ToDouble();
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          bv = ic.ToDouble(NumberFormatInfo.InvariantInfo);
        }
        break;
      case TypeCode.SByte: bv=(sbyte)b; break;
      case TypeCode.Single: bv=(float)b; break;
      case TypeCode.UInt16: bv=(ushort)b; break;
      case TypeCode.UInt32: bv=(uint)b; break;
      case TypeCode.UInt64: bv=(ulong)b; break;
      default: throw Ops.TypeError("cannot compare '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    return a<bv ? -1 : a>bv ? 1 : 0;
  }

  public static object Divide(double a, object b) { return Divide(a, b, false); }
  public static object Divide(double a, object b, bool floor)
  { double bv;
    if(b is double) bv = (double)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv=(bool)b ? 1 : 0; break;
      case TypeCode.Byte: bv=(byte)b; break;
      case TypeCode.Decimal:
        bv=((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo); break;
      case TypeCode.Int16: bv=(short)b; break;
      case TypeCode.Int32: bv=(int)b; break;
      case TypeCode.Int64: bv=(long)b; break;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        bv = ic.ToDouble(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: bv=(sbyte)b; break;
      case TypeCode.Single: bv=(float)b; break;
      case TypeCode.UInt16: bv=(ushort)b; break;
      case TypeCode.UInt32: bv=(uint)b; break;
      case TypeCode.UInt64: bv=(ulong)b; break;
      default:
        throw Ops.TypeError("invalid operand types for /: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    if(bv==0) throw new DivideByZeroException("float division by zero");

    return floor ? Math.Floor(a/bv) : a/bv;
  }

  public static object FloorDivide(double a, object b) { return Divide(a, b, true); }

  public static object Modulus(double a, object b)
  { double bv;
    if(b is double) bv = (double)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv=(bool)b ? 1 : 0; break;
      case TypeCode.Byte: bv=(byte)b; break;
      case TypeCode.Decimal: bv=((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo); break;
      case TypeCode.Int16: bv=(short)b; break;
      case TypeCode.Int32: bv=(int)b; break;
      case TypeCode.Int64: bv=(long)b; break;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        bv = ic.ToDouble(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: bv=(sbyte)b; break;
      case TypeCode.Single: bv=(float)b; break;
      case TypeCode.UInt16: bv=(ushort)b; break;
      case TypeCode.UInt32: bv=(uint)b; break;
      case TypeCode.UInt64: bv=(ulong)b; break;
      default: throw Ops.TypeError("invalid operand types for /: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    if(bv==0) throw new DivideByZeroException("float modulus by zero");
    return Math.IEEERemainder(a, bv);
  }

  public static object Multiply(double a, object b)
  { if(b is double) return a * (double)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a*1 : b;
      case TypeCode.Byte: return a * (byte)b;
      case TypeCode.Decimal: return new Decimal(a) * (Decimal)b;
      case TypeCode.Int16: return a * (short)b;
      case TypeCode.Int32: return a * (int)b;
      case TypeCode.Int64: return a * (long)b;
      case TypeCode.Object:
        if(b is Integer) return a * ((Integer)b).ToDouble();
        if(b is Complex) return a * (Complex)b;
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a * ic.ToDouble(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a * (sbyte)b;
      case TypeCode.Single: return a * (float)b;
      case TypeCode.UInt16: return a * (ushort)b;
      case TypeCode.UInt32: return a * (uint)b;
      case TypeCode.UInt64: return a * (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for *: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object Power(double a, object b)
  { double bv;
    if(b is double) bv = (double)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv=(bool)b ? 1 : 0; break;
      case TypeCode.Byte: bv=(byte)b; break;
      case TypeCode.Decimal:
        bv=((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo); break;
      case TypeCode.Int16: bv=(short)b; break;
      case TypeCode.Int32: bv=(int)b; break;
      case TypeCode.Int64: bv=(long)b; break;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        bv = ic.ToDouble(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: bv=(sbyte)b; break;
      case TypeCode.Single: bv=(float)b; break;
      case TypeCode.UInt16: bv=(ushort)b; break;
      case TypeCode.UInt32: bv=(uint)b; break;
      case TypeCode.UInt64: bv=(ulong)b; break;
      default:
        throw Ops.TypeError("invalid operand types for **: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }

    return Math.Pow(a, bv);
  }

  public static object Subtract(double a, object b)
  { if(b is double) return a - (double)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a-1 : a;
      case TypeCode.Byte: return a - (byte)b;
      case TypeCode.Decimal: return new Decimal(a) - (Decimal)b;
      case TypeCode.Int16: return a - (short)b;
      case TypeCode.Int32: return a - (int)b;
      case TypeCode.Int64: return a - (long)b;
      case TypeCode.Object:
        if(b is Integer) return a - ((Integer)b).ToDouble();
        if(b is Complex) return a - (Complex)b;
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a - ic.ToDouble(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a - (sbyte)b;
      case TypeCode.Single: return a - (float)b;
      case TypeCode.UInt16: return a - (ushort)b;
      case TypeCode.UInt32: return a - (uint)b;
      case TypeCode.UInt64: return a - (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for -: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }
}

} // namespace NetLisp.Backend