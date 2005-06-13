using System;

namespace NetLisp.Backend
{

public sealed class ComplexOps
{ ComplexOps() { }

  public static object Add(Complex a, object b)
  { if(b is Complex) return a + (Complex)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a+1 : a;
      case TypeCode.Byte: return a + (byte)b;
      case TypeCode.Decimal:
        return a + ((IConvertible)b).ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
      case TypeCode.Double: return a + (double)b;
      case TypeCode.Int16: return a + (short)b;
      case TypeCode.Int32: return a + (int)b;
      case TypeCode.Int64: return a + (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return (object)(a + ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo));
        break;
      case TypeCode.SByte: return a + (sbyte)b;
      case TypeCode.Single: return a + (float)b;
      case TypeCode.UInt16: return a + (ushort)b;
      case TypeCode.UInt32: return a + (uint)b;
      case TypeCode.UInt64: return a + (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for +: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static int Compare(Complex a, object b)
  { throw Ops.TypeError("cannot compare complex numbers except for equality/inequality");
  }

  public static object Divide(Complex a, object b)
  { if(b is Complex) return a / (Complex)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return a / ((bool)b ? 1 : 0);
      case TypeCode.Byte: return a / (byte)b;
      case TypeCode.Decimal:
        return a / ((IConvertible)b).ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
      case TypeCode.Double: return a / (double)b;
      case TypeCode.Int16: return a / (short)b;
      case TypeCode.Int32: return a / (int)b;
      case TypeCode.Int64: return a / (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return (object)(a / ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo));
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
    { case TypeCode.Boolean: return (bool)b ? a : new Complex(0);
      case TypeCode.Byte: return a * (byte)b;
      case TypeCode.Decimal:
        return a * ((IConvertible)b).ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
      case TypeCode.Double: return a * (double)b;
      case TypeCode.Int16: return a * (short)b;
      case TypeCode.Int32: return a * (int)b;
      case TypeCode.Int64: return a * (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return (object)(a * ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo));
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
    { case TypeCode.Boolean: return a.Pow((bool)b ? 1 : 0);
      case TypeCode.Byte: return a.Pow((byte)b);
      case TypeCode.Decimal:
        return a.Pow(((IConvertible)b).ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo));
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
    { case TypeCode.Boolean: return (bool)b ? a-1 : a;
      case TypeCode.Byte: return a - (byte)b;
      case TypeCode.Decimal:
        return a - ((IConvertible)b).ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo);
      case TypeCode.Double: return a - (double)b;
      case TypeCode.Int16: return a - (short)b;
      case TypeCode.Int32: return a - (int)b;
      case TypeCode.Int64: return a - (long)b;
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic!=null) return (object)(a - ic.ToDouble(System.Globalization.NumberFormatInfo.InvariantInfo));
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
