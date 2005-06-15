using System;
using System.Collections;
using System.Globalization;

namespace NetLisp.Backend
{

public sealed class IntOps
{ IntOps() { }

  public static object Add(int a, object b)
  { try
    { if(b is int) return checked(a+(int)b);
      switch(Convert.GetTypeCode(b))
      { case TypeCode.Boolean: return (bool)b ? checked(a+1) : a;
        case TypeCode.Byte: return checked(a + (byte)b);
        case TypeCode.Char: return a + (char)b; // TODO: see whether this should return int or char
        case TypeCode.Decimal: return a + (Decimal)b;
        case TypeCode.Double: return a + (double)b;
        case TypeCode.Int16: return checked(a + (short)b);
        case TypeCode.Int32: return checked(a + (int)b); // TODO: add these elsewhere
        case TypeCode.Int64: return checked(a + (long)b);
        case TypeCode.Object:
          if(b is Integer) return IntegerOps.Reduce(a + (Integer)b);
          if(b is Complex) return a + (Complex)b;
          IConvertible ic = b as IConvertible;
          if(ic!=null) return checked(a + ic.ToInt32(NumberFormatInfo.InvariantInfo));
          break;
        case TypeCode.SByte: return checked(a + (sbyte)b);
        case TypeCode.Single: return a + (float)b;
        case TypeCode.UInt16: return checked(a + (ushort)b);
        case TypeCode.UInt32: return checked(a + (uint)b);
        case TypeCode.UInt64:
        { ulong bv = (ulong)b;
          if(bv>int.MaxValue) return LongOps.Add(a, b);
          return checked(a + (long)b);
        }
      }
      throw Ops.TypeError("invalid operand types for +: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    catch(OverflowException) { return LongOps.Add(a, b); }
  }

  public static object BitwiseAnd(int a, object b)
  { if(b is int) return a&(int)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a&1 : 0;
      case TypeCode.Byte: return a & (byte)b;
      case TypeCode.Int16: return a & (short)b;
      case TypeCode.Int32: return a & (int)b;
      case TypeCode.Int64: return a & (long)b;
      case TypeCode.Object:
        if(b is Integer) return IntegerOps.Reduce((Integer)b & a);
        IConvertible ic = b as IConvertible;
        if(ic==null) break;
        long lv = ic.ToInt64(NumberFormatInfo.InvariantInfo);
        return lv>int.MaxValue || lv<int.MinValue ? (object)(lv&(long)a) : (object)((int)lv&a);
      case TypeCode.SByte: return a & (sbyte)b;
      case TypeCode.UInt16: return a & (ushort)b;
      case TypeCode.UInt32: return a & (uint)b;
      case TypeCode.UInt64: return (ulong)a & (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for &: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object BitwiseOr(int a, object b)
  { if(b is int) return a|(int)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a|1 : a;
      case TypeCode.Byte: return a | (byte)b;
      case TypeCode.Int16: return a | (ushort)(short)b;
      case TypeCode.Int32: return a | (int)b;
      case TypeCode.Int64: return (uint)a | (long)b;
      case TypeCode.Object:
        if(b is Integer) return (Integer)b | a;
        IConvertible ic = b as IConvertible;
        if(ic==null) break;
        long lv = ic.ToInt64(NumberFormatInfo.InvariantInfo);
        return lv>int.MaxValue || lv<int.MinValue ? (object)(lv|(uint)a) : (object)((int)lv|a);
      case TypeCode.SByte: return a | (byte)(sbyte)b;
      case TypeCode.UInt16: return a | (ushort)b;
      case TypeCode.UInt32: return (uint)a | (uint)b;
      case TypeCode.UInt64: return (ulong)(uint)a | (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for |: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static object BitwiseXor(int a, object b)
  { if(b is int) return a^(int)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a^1 : a;
      case TypeCode.Byte: return a ^ (byte)b;
      case TypeCode.Int16: return a ^ (short)b;
      case TypeCode.Int32: return a ^ (int)b;
      case TypeCode.Int64: return a ^ (long)b;
      case TypeCode.Object:
        if(b is Integer) return (Integer)b ^ a;
        IConvertible ic = b as IConvertible;
        if(ic==null) break;
        long lv = ic.ToInt64(NumberFormatInfo.InvariantInfo);
        return lv>int.MaxValue || lv<int.MinValue ? (object)(lv^(long)a) : (object)((int)lv^a);
      case TypeCode.SByte: return a ^ (sbyte)b;
      case TypeCode.UInt16: return a ^ (ushort)b;
      case TypeCode.UInt32: return a ^ (uint)b;
      case TypeCode.UInt64: return (ulong)a ^ (ulong)b;
    }
    throw Ops.TypeError("invalid operand types for ^: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
  }

  public static int Compare(int a, object b)
  { if(b is int) return a-(int)b;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a-1 : a;
      case TypeCode.Byte: return a - (byte)b;
      case TypeCode.Char: case TypeCode.String: return -1;
      case TypeCode.Decimal:
      { Decimal v = (Decimal)b;
        return a<v ? -1 : a>v ? 1 : 0;
      }
      case TypeCode.Double:
      { double av=a, bv = (double)b;
        return av<bv ? -1 : av>bv ? 1 : 0;
      }
      case TypeCode.Empty: return 1;
      case TypeCode.Int16: return a - (short)b;
      case TypeCode.Int32: return a - (int)b;
      case TypeCode.Int64: return (int)((a - (long)b)>>32);
      case TypeCode.Object:
        if(b is Integer) return -((Integer)b).CompareTo(a);
        IConvertible ic = b as IConvertible;
        if(ic!=null) return (int)(a - ic.ToInt64(NumberFormatInfo.InvariantInfo));
        break;
      case TypeCode.SByte: return a - (sbyte)b;
      case TypeCode.Single:
      { float av=a, bv=(float)b;
        return av<bv ? -1 : av>bv ? 1 : 0;
      }
      case TypeCode.UInt16: return a - (ushort)b;
      case TypeCode.UInt32: return a<0 ? -1 : (int)((uint)a - (uint)b);
      case TypeCode.UInt64: return a<0 ? -1 : (int)((ulong)a - (ulong)b);
    }
    return string.Compare("fixnum32", Ops.TypeName(b));
  }

  public static object Divide(int a, object b) { return Divide(a, b, false); }
  public static object Divide(int a, object b, bool floor)
  { int bv;
    if(b is int) bv = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv = (bool)b ? 1 : 0; break;
      case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Double:
      { double dv = a/(double)b;
        return floor ? Math.Floor(dv) : dv;
      }
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int64: return LongOps.Divide(a, b, floor);
      case TypeCode.Object:
      { if(b is Integer)
        { if(floor)
          { Integer iv = (Integer)b;
            return IntegerOps.Reduce(a<0 ? (a-iv+iv.Sign)/iv : a/iv);
          }
          else return IntegerOps.Divide(new Integer(a), b);
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          long lv = ic.ToInt64(NumberFormatInfo.InvariantInfo);
          if(lv>int.MaxValue || lv<int.MinValue) return LongOps.Divide(a, b, floor);
          bv = (int)lv;
        }
        break;
      }
      case TypeCode.SByte: bv=(sbyte)b; break;
      case TypeCode.Single:
      { double dv = a/(float)b;
        return floor ? Math.Floor(dv) : dv;
      }
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32:
      { uint v = (uint)b;
        if(v>int.MaxValue) return LongOps.Divide(a, b, floor);
        else { bv = (int)v; break; }
      }
      case TypeCode.UInt64:
        return floor ? IntegerOps.FloorDivide(new Integer(a), b) : IntegerOps.Divide(new Integer(a), b);
      default: throw Ops.TypeError("invalid operand types for //: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    if(bv==0) throw new DivideByZeroException("floor division by zero");
    if(floor) return (a<0 ? (a-bv+Math.Sign(bv)) : a) / bv;
    else
    { int rem, ret=Math.DivRem(a, bv, out rem);
      return rem==0 ? (object)ret : (double)a/bv;
    }
  }

  public static object FloorDivide(int a, object b) { return Divide(a, b, true); }

  public static object LeftShift(int a, object b)
  { int shift;
    if(b is int) shift = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: if((bool)b) shift=1; else return a; break;
      case TypeCode.Byte: shift = (byte)b; break;
      case TypeCode.Int16: shift = (short)b; break;
      case TypeCode.Int32: shift = (int)b; break;
      case TypeCode.Int64:
      { long lv = (long)b;
        if(lv>int.MaxValue || lv<int.MinValue) throw new OverflowException("long int too large to convert to int");
        shift = (int)lv;
        break;
      }
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        shift = ic.ToInt32(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: shift = (sbyte)b; break;
      case TypeCode.UInt16: shift = (ushort)b; break;
      case TypeCode.UInt32:
      { uint ui = (uint)b;
        if(ui>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        shift = (int)ui;
        break;
      }
      case TypeCode.UInt64:
      { ulong ul = (uint)b;
        if(ul>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        shift = (int)ul;
        break;
      }
      default: throw Ops.TypeError("invalid operand types for <<: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    
    if(shift<0) throw Ops.ValueError("negative shift count");
    if(shift>31) return LongOps.LeftShift(a, shift);
    int res = a << shift;
    return res<a || (a&0x80000000) != (res&0x80000000) ? LongOps.LeftShift(a, shift) : res;
  }

  public static object Modulus(int a, object b)
  { int bv;
    if(b is int) bv = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: bv = (bool)b ? 1 : 0; break;
      case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Decimal: return Math.IEEERemainder(a, ((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo));
      case TypeCode.Double: return Math.IEEERemainder(a, (double)b);
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int32: bv = (int)b; break;
      case TypeCode.Int64: int64:
      { long lv = (long)b;
        if(lv<0)
        { if(a<0 && lv<a) return a;
          else if(a>=0 && lv<=-a) return lv+a;
        }
        else if(a>=0 && lv>a) return a;
        else if(a<0 && lv>=-(long)a) return lv+a;
        bv = (int)lv;
        break;
      }
      case TypeCode.Object:
        if(b is Integer)
        { Integer iv = (Integer)b;
          if(iv.Sign<0)
          { if(a<0 && iv<a) return a;
            else if(a>=0 && iv<=-a) return iv+a;
          }
          else if(a>=0 && iv>a) return a;
          else if(a<0 && iv>=-(long)a) return iv+a;
          bv = iv.ToInt32();
          break;
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          b = ic.ToInt64(NumberFormatInfo.InvariantInfo);
          goto int64;
        }
      case TypeCode.SByte: bv = (sbyte)b; break;
      case TypeCode.Single: return Math.IEEERemainder(a, (float)b);
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32:
      { uint ui = (uint)b;
        if(a>=0 && ui>(uint)a) return a;
        else if(a<0 && ui>=-(long)a) return ui+a;
        else bv = (int)ui;
        break;
      }
      case TypeCode.UInt64:
      { ulong ul = (ulong)b;
        if(a>=0 && ul>(uint)a) return a;
        else if(a<0 && ul>=(ulong)-(long)a) return ul+(ulong)(long)a;
        else bv = (int)ul;
        break;
      }
      default:
        throw Ops.TypeError("invalid operand types for %: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    if(bv==0) throw new DivideByZeroException("modulus by zero");
    return a%bv;
  }
  
  public static object Multiply(int a, object b)
  { try
    { if(b is int) return checked(a*(int)b);
      switch(Convert.GetTypeCode(b))
      { case TypeCode.Boolean: return (bool)b ? checked(a*1) : a;
        case TypeCode.Byte: return checked(a * (byte)b);
        case TypeCode.Decimal: return a * (Decimal)b;
        case TypeCode.Double: return a * (double)b;
        case TypeCode.Int16: return checked(a * (short)b);
        case TypeCode.Int64: return checked(a * (long)b);
        case TypeCode.Object:
          if(b is Integer) return a * (Integer)b;
          if(b is Complex) return a * (Complex)b;
          IConvertible ic = b as IConvertible;
          if(ic!=null) return checked(a * ic.ToInt32(NumberFormatInfo.InvariantInfo));
          goto default;
        case TypeCode.SByte: return checked(a * (sbyte)b);
        case TypeCode.Single: return a * (float)b;
        case TypeCode.String: return StringOps.Multiply((string)b, a);
        case TypeCode.UInt16: return checked(a * (ushort)b);
        case TypeCode.UInt32: return checked(a * (uint)b);
        case TypeCode.UInt64:
        { if(a==0) return 0;
          ulong bv = (ulong)b;
          if(bv>int.MaxValue) return LongOps.Multiply(a, b);
          return checked(a * (long)b);
        }
        default: throw Ops.TypeError("invalid operand types for *: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
      }
    }
    catch(OverflowException) { return LongOps.Multiply(a, b); }
  }

  public static object Power(int a, object b)
  { int bv;
    if(b is int) bv = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: return (bool)b ? a : a<0 ? -1 : 1;
      case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Decimal: return Math.Pow(a, ((IConvertible)b).ToDouble(NumberFormatInfo.InvariantInfo));
      case TypeCode.Double: return Math.Pow(a, (double)b);
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int32: bv = (int)b; break;
      case TypeCode.Int64: int64:
      { long v = (long)b;
        if(v<0 || v>int.MaxValue) return IntegerOps.Power(new Integer(a), b);
        bv = (int)v;
        break;
      }
      case TypeCode.Object:
        if(b is Integer)
        { Integer iv = (Integer)b;
          if(iv.Sign<0 || iv>int.MaxValue) return new Integer(a).Pow(iv);
          bv = iv.ToInt32();
          break;
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic==null) goto default;
          b = ic.ToInt64(NumberFormatInfo.InvariantInfo);
          goto int64;
        }
      case TypeCode.SByte: bv = (sbyte)b; break;
      case TypeCode.Single: return Math.Pow(a, (float)b);
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32:
      { uint v = (uint)b;
        if(v>(uint)int.MaxValue) return IntegerOps.Power(new Integer(a), b);
        bv = (int)v;
        break;
      }
      case TypeCode.UInt64:
      { ulong v = (ulong)b;
        if(v>(uint)int.MaxValue) return IntegerOps.Power(new Integer(a), b);
        bv = (int)v;
        break;
      }
      default:
        throw Ops.TypeError("invalid operand types for **: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }

    if(bv<0) return Math.Pow(a, bv);
    try
    { int ix=1;
	    while(bv > 0)
	    { if((bv&1)!=0)
	      { if(a==0) break;
	        checked { ix = ix*a; }
		    }
	 	    bv >>= 1;
	      if(bv==0) break;
	 	    checked { a *= a;	}
		  }
		  return ix;
		}
		catch(OverflowException) { return IntegerOps.Power(new Integer(a), b); }
  }

  // FIXME: this shouldn't be so inefficient. the modulus should be built into the power operation
  public static object PowerMod(int a, object b, object c)
  { int mod = Ops.ToInt(c);
    if(mod==0) throw new DivideByZeroException("ternary pow(): modulus by zero");

    object pow = Power(a, b);
    if(pow is int) return (int)pow % mod;
    if(pow is Integer) return IntegerOps.Reduce((Integer)pow % mod);
    throw Ops.TypeError("ternary pow() requires that the base and exponent be integers");
  }

  public static object Subtract(int a, object b)
  { try
    { if(b is int) return checked(a-(int)b); // TODO: make sure this is worthwhile
      switch(Convert.GetTypeCode(b))
      { case TypeCode.Boolean: return (bool)b ? checked(a-1) : a;
        case TypeCode.Byte: return checked(a - (byte)b);
        case TypeCode.Char: return a - (char)b; // TODO: see whether this should return int or char
        case TypeCode.Decimal: return a - (Decimal)b;
        case TypeCode.Double: return a - (double)b;
        case TypeCode.Int16: return checked(a - (short)b);
        case TypeCode.Int64: return checked(a - (long)b);
        case TypeCode.Object:
          if(b is Integer) return IntegerOps.Reduce(a - (Integer)b);
          if(b is Complex) return a - (Complex)b;
          IConvertible ic = b as IConvertible;
          if(ic!=null) return checked(a - ic.ToInt32(NumberFormatInfo.InvariantInfo));
          goto default;
        case TypeCode.SByte: return checked(a - (sbyte)b);
        case TypeCode.Single: return a - (float)b;
        case TypeCode.UInt16: return checked(a - (ushort)b);
        case TypeCode.UInt32: return checked(a - (uint)b);
        case TypeCode.UInt64:
        { ulong bv = (ulong)b;
          if(bv>int.MaxValue) return LongOps.Subtract(a, b);
          return checked(a - (long)b);
        }
        default: throw Ops.TypeError("invalid operand types for -: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
      }
    }
    catch(OverflowException) { return LongOps.Subtract(a, b); }
  }

  public static object RightShift(int a, object b)
  { int shift;
    if(b is int) shift = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Boolean: if((bool)b) shift=1; else return a; break;
      case TypeCode.Byte: shift = (byte)b; break;
      case TypeCode.Int16: shift = (short)b; break;
      case TypeCode.Int64:
      { long lv = (long)b;
        if(lv>int.MaxValue || lv<int.MinValue) throw new OverflowException("long int too large to convert to int");
        shift = (int)lv;
        break;
      }
      case TypeCode.Object:
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        shift = ic.ToInt32(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: shift = (sbyte)b; break;
      case TypeCode.UInt16: shift = (ushort)b; break;
      case TypeCode.UInt32:
      { uint ui = (uint)b;
        if(ui>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        shift = (int)ui;
        break;
      }
      case TypeCode.UInt64:
      { ulong ul = (uint)b;
        if(ul>int.MaxValue) throw new OverflowException("long int too large to convert to int");
        shift = (int)ul;
        break;
      }
      default: throw Ops.TypeError("invalid operand types for >>: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }

    if(shift<0) throw Ops.ValueError("negative shift count");
    return shift>31 ? 0 : a>>shift;
  }
}

} // namespace NetLisp.Backend