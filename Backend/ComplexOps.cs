using System;

namespace NetLisp.Backend
{

public sealed class ComplexOps
{ ComplexOps() { }

  public static object Add(Complex a, object b)
  { if(b is Complex) return a + (Complex)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a + (byte)b;
      case TypeCode.Decimal: return a + Decimal.ToDouble((Decimal)b);
      case TypeCode.Double: return a + (double)b;
      case TypeCode.Int16: return a + (short)b;
      case TypeCode.Int32: return a + (int)b;
      case TypeCode.Int64: return a + (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a + ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a + (sbyte)b;
      case TypeCode.Single: return a + (float)b;
      case TypeCode.UInt16: return a + (ushort)b;
      case TypeCode.UInt32: return a + (uint)b;
      case TypeCode.UInt64: return a + (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for +: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static bool AreEqual(Complex a, object b)
  { if(b is Complex) return a==(Complex)b;
    if(a.imag!=0) return false;

    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a.real==(byte)b;
      case TypeCode.Decimal: return a.real==Decimal.ToDouble((Decimal)b);
      case TypeCode.Double: return a.real==(double)b;
      case TypeCode.Int16: return a.real==(short)b;
      case TypeCode.Int32: return a.real==(int)b;
      case TypeCode.Int64: return a.real==(long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a.real==ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a.real==(sbyte)b;
      case TypeCode.Single: return a.real==(float)b;
      case TypeCode.UInt16: return a.real==(ushort)b;
      case TypeCode.UInt32: return a.real==(uint)b;
      case TypeCode.UInt64: return a.real==(ulong)b;
    }
    return false;
  }

  public static object Divide(Complex a, object b)
  { if(b is Complex) return a / (Complex)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a / (byte)b;
      case TypeCode.Decimal: return a / Decimal.ToDouble((Decimal)b);
      case TypeCode.Double: return a / (double)b;
      case TypeCode.Int16: return a / (short)b;
      case TypeCode.Int32: return a / (int)b;
      case TypeCode.Int64: return a / (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a / ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a / (sbyte)b;
      case TypeCode.Single: return a / (float)b;
      case TypeCode.UInt16: return a / (ushort)b;
      case TypeCode.UInt32: return a / (uint)b;
      case TypeCode.UInt64: return a / (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for /: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object Multiply(Complex a, object b)
  { if(b is Complex) return a * (Complex)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a * (byte)b;
      case TypeCode.Decimal: return a * Decimal.ToDouble((Decimal)b);
      case TypeCode.Double: return a * (double)b;
      case TypeCode.Int16: return a * (short)b;
      case TypeCode.Int32: return a * (int)b;
      case TypeCode.Int64: return a * (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a * ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a * (sbyte)b;
      case TypeCode.Single: return a * (float)b;
      case TypeCode.UInt16: return a * (ushort)b;
      case TypeCode.UInt32: return a * (uint)b;
      case TypeCode.UInt64: return a * (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for *: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static bool NonZero(Complex a) { return a.real!=0 && a.imag!=0; }

  public static object Power(Complex a, object b)
  { if(b is Complex) return a.Pow((Complex)b);

    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a.Pow((byte)b);
      case TypeCode.Decimal: return a.Pow(Decimal.ToDouble((Decimal)b));
      case TypeCode.Double: return a.Pow((double)b);
      case TypeCode.Int16: return a.Pow((short)b);
      case TypeCode.Int32: return a.Pow((int)b);
      case TypeCode.Int64: return a.Pow((long)b);
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a.Pow(ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo));
        break;
      case TypeCode.SByte: return a.Pow((sbyte)b);
      case TypeCode.Single: return a.Pow((float)b);
      case TypeCode.UInt16: return a.Pow((ushort)b);
      case TypeCode.UInt32: return a.Pow((uint)b);
      case TypeCode.UInt64: return a.Pow((ulong)b);
    }
    throw Ops.TypeError("invalid operand types for **: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object PowerMod(Complex a, object b, object c)
  { throw Ops.TypeError("complex modulus not supported");
  }

  public static object Subtract(Complex a, object b)
  { if(b is Complex) return a - (Complex)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a - (byte)b;
      case TypeCode.Decimal: return a - Decimal.ToDouble((Decimal)b);
      case TypeCode.Double: return a - (double)b;
      case TypeCode.Int16: return a - (short)b;
      case TypeCode.Int32: return a - (int)b;
      case TypeCode.Int64: return a - (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a - ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
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
