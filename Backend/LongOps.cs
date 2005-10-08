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
using System.Globalization;

namespace NetLisp.Backend
{

public sealed class LongOps
{ LongOps() { }

  public static object Add(long a, object b)
  { try
    { switch(Convert.GetTypeCode(b))
      { case TypeCode.Byte: return checked(a + (byte)b);
        case TypeCode.Char: return a + (int)(char)b; // TODO: see whether this should return int or char
        case TypeCode.Decimal: return a + (Decimal)b;
        case TypeCode.Double: return a + (double)b;
        case TypeCode.Int16: return checked(a + (short)b);
        case TypeCode.Int32: return checked(a + (int)b);
        case TypeCode.Int64: return checked(a + (long)b);
        case TypeCode.Object:
          if(b is Integer) return IntegerOps.Reduce(a + (Integer)b);
          if(b is Complex) return a + (Complex)b;
          IConvertible ic = b as IConvertible;
          if(ic!=null) return a + ic.ToDouble(NumberFormatInfo.InvariantInfo);
          goto default;
        case TypeCode.SByte: return checked(a + (sbyte)b);
        case TypeCode.Single: return a + (float)b;
        case TypeCode.UInt16: return checked(a + (ushort)b);
        case TypeCode.UInt32: return checked(a + (uint)b);
        case TypeCode.UInt64:
        { ulong bv = (ulong)b;
          if(bv>long.MaxValue) return new Integer(a)+bv;
          return checked(a + (long)b);
        }
        default: throw Ops.TypeError("invalid operand types for +: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
      }
    }
    catch(OverflowException) { return IntegerOps.Add(new Integer(a), b); }
  }

  public static bool AreEqual(long a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a==(byte)b;
      case TypeCode.Char: return a==(int)(char)b;
      case TypeCode.Decimal: return new Decimal(a)==(Decimal)b;
      case TypeCode.Double: return a==(double)b;
      case TypeCode.Int16: return a==(short)b;
      case TypeCode.Int32: return a==(int)b;
      case TypeCode.Int64: return a==(long)b;
      case TypeCode.Object:
        if(b is Integer) return a==(Integer)b;
        if(b is Complex)
        { Complex c = (Complex)b;
          if(c.imag==0) return a==c.real;
        }
        IConvertible ic = b as IConvertible;
        if(ic!=null) return a==ic.ToDouble(NumberFormatInfo.InvariantInfo);
        break;
      case TypeCode.SByte: return a==(sbyte)b;
      case TypeCode.Single: return a==(float)b;
      case TypeCode.UInt16: return a==(ushort)b;
      case TypeCode.UInt32: return a==(uint)b;
      case TypeCode.UInt64: return a>=0 && (uint)a==(ulong)b;
    }
    return false;
  }

  public static object BitwiseAnd(long a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a & (byte)b;
      case TypeCode.Int16: return a & (short)b;
      case TypeCode.Int32: return a & (int)b;
      case TypeCode.Int64: return a & (long)b;
      case TypeCode.Object:
        if(b is Integer) return IntegerOps.Reduce((Integer)b & a);
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        return a & ic.ToInt64(NumberFormatInfo.InvariantInfo);
      case TypeCode.SByte: return a & (sbyte)b;
      case TypeCode.UInt16: return a & (ushort)b;
      case TypeCode.UInt32: return a & (uint)b;
      case TypeCode.UInt64: return (ulong)a & (ulong)b;
      default: throw Ops.TypeError("invalid operand types for &: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
  }

  public static object BitwiseOr(long a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a | (byte)b;
      case TypeCode.Int16: return a | (ushort)(short)b;
      case TypeCode.Int32: return a | (uint)(int)b;
      case TypeCode.Int64: return a | (long)b;
      case TypeCode.Object:
        if(b is Integer) return (Integer)b | a;
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        return a | ic.ToInt64(NumberFormatInfo.InvariantInfo);
      case TypeCode.SByte: return a | (byte)(sbyte)b;
      case TypeCode.UInt16: return a | (ushort)b;
      case TypeCode.UInt32: return a | (uint)b;
      case TypeCode.UInt64: return (ulong)a | (ulong)b;
      default: throw Ops.TypeError("invalid operand types for |: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
  }

  public static object BitwiseXor(long a, object b)
  { switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: return a ^ (byte)b;
      case TypeCode.Int16: return a ^ (short)b;
      case TypeCode.Int32: return a ^ (int)b;
      case TypeCode.Int64: return a ^ (long)b;
      case TypeCode.Object:
        if(b is Integer) return (Integer)b ^ a;
        IConvertible ic = b as IConvertible;
        if(ic==null) goto default;
        return a ^ ic.ToInt64(NumberFormatInfo.InvariantInfo);
      case TypeCode.SByte: return a ^ (sbyte)b;
      case TypeCode.UInt16: return a ^ (ushort)b;
      case TypeCode.UInt32: return a ^ (uint)b;
      case TypeCode.UInt64: return (ulong)a ^ (ulong)b;
      default: throw Ops.TypeError("invalid operand types for ^: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
  }

  public static int Compare(long a, object b)
  { long cv;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: cv = a-(byte)b; break;
      case TypeCode.Decimal:
      { Decimal v = (Decimal)b;
        return a<v ? -1 : a>v ? 1 : 0;
      }
      case TypeCode.Double:
      { double av=a, bv = (double)b;
        return av<bv ? -1 : av>bv ? 1 : 0;
      }
      case TypeCode.Int16: cv = a-(short)b; break;
      case TypeCode.Int32: cv = a-(int)b; break;
      case TypeCode.Int64: cv = a-(long)b; break;
      case TypeCode.Object:
        if(b is Integer) return -((Integer)b).CompareTo(a);
        if(b is Complex)
        { Complex c = (Complex)b;
          if(c.imag==0) return a<c.real ? -1 : a>c.real ? 1 : 0;
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic!=null)
          { double dv = ic.ToDouble(NumberFormatInfo.InvariantInfo);
            return a<dv ? -1 : a>dv ? 1 : 0;
          }
        }
        goto default;
      case TypeCode.SByte: cv = a-(sbyte)b; break;
      case TypeCode.Single:
      { float av=a, bv=(float)b;
        return av<bv ? -1 : av>bv ? 1 : 0;
      }
      case TypeCode.UInt16: cv = a-(ushort)b; break;
      case TypeCode.UInt32: cv = a-(uint)b; break;
      case TypeCode.UInt64:
        if(a<0) return -1;
        cv = (long)((ulong)a - (ulong)b);
        break;
      default: throw Ops.TypeError("can't compare types: {0} and {1}", Ops.TypeName(a), Ops.TypeName(b));
    }
    return cv<0 ? -1 : cv>0 ? 1 : 0;
  }

//  public static object Divide(long a, object b) { return Divide(a, b, false); }
  public static object Divide(long a, object b, bool floor)
  { long bv;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Double:
      { double dv = a/(double)b;
        return floor ? Math.Floor(dv) : dv;
      }
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int32: bv = (int)b; break;
      case TypeCode.Int64: bv = (long)b; break;
      case TypeCode.Object:
        if(b is Integer)
        { if(floor)
          { Integer iv = (Integer)b;
            return IntegerOps.Reduce(a<0 ? (a-iv+iv.Sign)/iv : a/iv);
          }
          else return IntegerOps.Divide(new Integer(a), b);
        }
        else if(b is Complex)
        { Complex c = (Complex)b;
          if(c.imag==0)
          { double dv = a/c.real;
            return floor ? Math.Floor(dv) : dv;
          }
          goto default;
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic!=null) goto default;
          double dv = a/ic.ToDouble(NumberFormatInfo.InvariantInfo);
          return floor ? Math.Floor(dv) : dv;
        }
      case TypeCode.SByte: bv=(sbyte)b; break;
      case TypeCode.Single:
      { double dv = a/(float)b;
        return floor ? Math.Floor(dv) : dv;
      }
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32: bv = (uint)b; break;
      case TypeCode.UInt64:
      { ulong v = (ulong)b;
        if(v>long.MaxValue)
          return floor ? IntegerOps.FloorDivide(new Integer(a), b) : IntegerOps.Divide(new Integer(a), b);
        else { bv = (long)v; break; }
      }
      default: throw Ops.TypeError("invalid operand types for /: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }

    if(bv==0) throw new DivideByZeroException("floor division by zero");
    if(floor) return (a<0 ? (a-bv+Math.Sign(bv)) : a) / bv;
    else
    { long rem, ret=Math.DivRem(a, bv, out rem);
      return rem==0 ? (object)ret : (double)a/bv;
    }
  }

  public static object FloorDivide(long a, object b) { return Divide(a, b, true); }

  public static object LeftShift(long a, object b)
  { int shift;
    if(b is int) shift = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: shift = (byte)b; break;
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
    if(shift>63) return new Integer(a)<<shift;
    long res = a << shift;
    if(res<a || ((ulong)a&0x8000000000000000) != ((ulong)res&0x8000000000000000)) return new Integer(a)<<shift;
    else return res;
  }

  public static object Modulus(long a, object b)
  { long bv;
    switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Decimal: return Math.IEEERemainder(a, Decimal.ToDouble((Decimal)b));
      case TypeCode.Double: return Math.IEEERemainder(a, (double)b);
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int32: bv = (int)b; break;
      case TypeCode.Int64: bv = (long)b; break;
      case TypeCode.Object:
        if(b is Integer)
        { Integer iv = (Integer)b;
          if(iv.Sign<0)
          { if(a<0 && iv<a) return a;
            else if(a>=0 && iv<=-a) return iv+a;
          }
          else if(a>=0 && iv>a) return a;
          else if(a<0 && -iv<=a) return iv+a;
          bv = iv.ToInt64();
          break;
        }
        else if(b is Complex)
        { Complex c = (Complex)b;
          if(c.imag==0) return Math.IEEERemainder(a, c.real);
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic!=null) return Math.IEEERemainder(a, ic.ToDouble(NumberFormatInfo.InvariantInfo));
        }
        goto default;
      case TypeCode.SByte: bv = (sbyte)b; break;
      case TypeCode.Single: return Math.IEEERemainder(a, (float)b);
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32: bv = (uint)b; break;
      case TypeCode.UInt64:
      { ulong ul = (ulong)b;
        if(a>=0 && ul>(ulong)a) return a;
        else if(a<0 && ul>=(a==long.MinValue ? unchecked((ulong)-long.MinValue) : (ulong)-a)) return ul+(ulong)a;
        else bv = (long)ul;
        break;
      }
      default: throw Ops.TypeError("invalid operand types for %: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }
    if(bv==0) throw new DivideByZeroException("modulus by zero");
    return Reduce(a%bv);
  }

  public static object Multiply(long a, object b)
  { try
    { switch(Convert.GetTypeCode(b))
      { case TypeCode.Byte: return checked(a * (byte)b);
        case TypeCode.Decimal: return a * (Decimal)b;
        case TypeCode.Double: return a * (double)b;
        case TypeCode.Int16: return checked(a * (short)b);
        case TypeCode.Int32: return checked(a * (int)b);
        case TypeCode.Int64: return checked(a * (long)b);
        case TypeCode.Object:
          if(b is Integer) return a * (Integer)b;
          if(b is Complex) return a * (Complex)b;
          IConvertible ic = b as IConvertible;
          if(ic!=null) return a * ic.ToDouble(NumberFormatInfo.InvariantInfo);
          goto default;
        case TypeCode.SByte: return checked(a * (sbyte)b);
        case TypeCode.Single: return a * (float)b;
        case TypeCode.String: return StringOps.Multiply((string)b, a);
        case TypeCode.UInt16: return checked(a * (ushort)b);
        case TypeCode.UInt32: return checked(a * (uint)b);
        case TypeCode.UInt64:
        { ulong ul = (ulong)b;
          if(ul>(ulong)int.MaxValue) return new Integer(a)*ul;
          return checked(a * (long)ul);
        }
        default: throw Ops.TypeError("invalid operand types for *: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
      }
    }
    catch(OverflowException) { return IntegerOps.Multiply(new Integer(a), b); }
  }

  public static object Power(long a, object b)
  { long bv;
    if(b is int) bv = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: bv = (byte)b; break;
      case TypeCode.Decimal: return Math.Pow(a, Decimal.ToDouble((Decimal)b));
      case TypeCode.Double: return Math.Pow(a, (double)b);
      case TypeCode.Int16: bv = (short)b; break;
      case TypeCode.Int32: bv = (int)b; break;
      case TypeCode.Int64: bv = (long)b; break;
      case TypeCode.Object:
        if(b is Integer)
        { Integer iv = (Integer)b;
          if(iv<0 || iv>long.MaxValue) return new Integer(a).Pow(iv);
          bv = iv.ToInt64(null);
          break;
        }
        else if(b is Complex)
        { Complex c = (Complex)b;
          if(c.imag==0) return Math.Pow(a, c.real);
        }
        else
        { IConvertible ic = b as IConvertible;
          if(ic!=null) return Math.Pow(a, ic.ToDouble(NumberFormatInfo.InvariantInfo));
        }
        goto default;
      case TypeCode.SByte: bv = (sbyte)b; break;
      case TypeCode.Single: return Math.Pow(a, (float)b);
      case TypeCode.UInt16: bv = (ushort)b; break;
      case TypeCode.UInt32: bv = (uint)b; break;
      case TypeCode.UInt64:
      { ulong v = (ulong)b;
        if(v>(ulong)long.MaxValue) return IntegerOps.Power(new Integer(a), b);
        bv = (long)v;
        break;
      }
      default:
        throw Ops.TypeError("invalid operand types for **: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }

    if(bv<0) return Math.Pow(a, bv);
    try // TODO: check if Math.Pow() is faster than this
    { long ix=1;
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
  public static object PowerMod(long a, object b, object o)
  { long mod = Ops.ToLong(o);
    if(mod==0) throw new DivideByZeroException("ternary pow(): modulus by zero");

    object pow = Power(a, b);
    if(pow is long) return Reduce((long)pow % mod);
    if(pow is Integer) return IntegerOps.Reduce((Integer)pow % mod);
    throw Ops.TypeError("ternary pow() requires that the base and exponent be integers");
  }

  public static object Subtract(long a, object b)
  { try
    { long ret;
      switch(Convert.GetTypeCode(b))
      { case TypeCode.Byte: ret = checked(a - (byte)b); break;
        case TypeCode.Char: ret = a - (char)b; break; // TODO: see whether this should return int or char
        case TypeCode.Decimal: return a - (Decimal)b;
        case TypeCode.Double: return a - (double)b;
        case TypeCode.Int16: ret = checked(a - (short)b); break;
        case TypeCode.Int32: ret = checked(a - (int)b); break;
        case TypeCode.Int64: ret = checked(a - (long)b); break;
        case TypeCode.Object:
          if(b is Integer) return IntegerOps.Reduce(a - (Integer)b);
          if(b is Complex) return a - (Complex)b;
          IConvertible ic = b as IConvertible;
          if(ic!=null) return a - ic.ToDouble(NumberFormatInfo.InvariantInfo);
          goto default;
        case TypeCode.SByte: ret = checked(a - (sbyte)b); break;
        case TypeCode.Single: return a - (float)b;
        case TypeCode.UInt16: ret = checked(a - (ushort)b); break;
        case TypeCode.UInt32: ret = checked(a - (uint)b); break;
        case TypeCode.UInt64:
        { ulong ul = (ulong)b;
          if(ul>(ulong)long.MaxValue) return IntegerOps.Reduce(new Integer(a)-ul);
          ret = checked(a - (long)ul); break;
        }
        default: throw Ops.TypeError("invalid operand types for -: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
      }
      return Reduce(ret);
    }
    catch(OverflowException) { return IntegerOps.Subtract(new Integer(a), b); }
  }

  public static object RightShift(long a, object b)
  { int shift;
    if(b is int) shift = (int)b;
    else switch(Convert.GetTypeCode(b))
    { case TypeCode.Byte: shift = (byte)b; break;
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
      default: throw Ops.TypeError("invalid operand types for >>return shift>31 ? 0 : a>>shift;: '{0}' and '{1}'", Ops.TypeName(a), Ops.TypeName(b));
    }

    if(shift<0) throw Ops.ValueError("negative shift count");
    return shift>63 ? 0 : Reduce(a>>shift);
  }

  internal static object Reduce(long value) { return ((ulong)value>>32)==0 ? (int)(uint)value : (object)value; }
}

} // namespace NetLisp.Backend