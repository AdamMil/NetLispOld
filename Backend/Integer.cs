using System;
using System.Runtime.InteropServices;

namespace NetLisp.Backend
{

public struct Integer : IConvertible, IComparable, ICloneable
{ 
  #if LINUX
  const string ImportDll = "glibc.so"; // TODO: figure out what this should really be
  #else
  const string ImportDll = "msvcrt.dll";
  #endif

  #region Constructors
  public Integer(int i)
  { if(i>0)
    { sign   = 1;
      data   = i==1 ? One.data : new uint[1] { (uint)i };
      length = 1;
    }
    else if(i<0)
    { sign   = -1;
      data   = i==-1 ? MinusOne.data : new uint[1] { (uint)-i };
      length = 1;
    }
    else this=Zero;
  }

  public Integer(uint i)
  { if(i==0) this=Zero;
    else
    { sign = 1;
      data = i==1 ? One.data : new uint[1] { i };
      length = 1;
    }
  }

  public Integer(long i)
  { if(i==0) this=Zero;
    else
    { ulong v;
      if(i>0)
      { sign = 1;
        v = (ulong)i;
      }
      else
      { sign = -1;
        v = (ulong)-i;
      }
      data = new uint[2] { (uint)v, (uint)(v>>32) };
      length = (ushort)calcLength(data);
    }
  }

  public Integer(ulong i)
  { if(i==0) { this=Zero; }
    else
    { sign   = 1;
      data   = new uint[2] { (uint)i, (uint)(i>>32) };
      length = (ushort)calcLength(data);
    }
  }

  public Integer(string s) : this(s, 10) { }
  public Integer(string s, int radix)
  { if(s==null) throw Ops.ValueError("Cannot convert null to Integer");
    if(radix<2 || radix>36) throw Ops.ValueError("radix must be from 2 to 36");
    string charSet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".Substring(radix);

    Integer val = Zero;
    int i=0;
    char c;
    bool neg=false;
    while(i<s.Length && char.IsWhiteSpace(s[i])) i++;
    if(i<s.Length)
    { if(s[i]=='-') { neg=true; i++; }
      else if(s[i]=='+') i++;
    }
    while(i<s.Length && char.IsWhiteSpace(s[i])) i++;
    
    if(i==s.Length || charSet.IndexOf(c=char.ToUpper(s[i]))==-1) throw Ops.ValueError("String does not contain a valid integer");
    while(true)
    { val = val*radix + (c>'9' ? c-'A'+10 : c-'0');
      if(++i==s.Length || charSet.IndexOf(c=char.ToUpper(c))==-1) break;
    }

    sign=neg ? (short)-val.sign : val.sign; data=val.data; length=val.length;
  }

  public Integer(double d)
  { if(double.IsInfinity(d)) throw new OverflowException("cannot convert float infinity to long");
    double frac;
    int expo;

    frac = frexp(d, out expo);
    if(expo<=0) { sign=0; data=Zero.data; length=0; return; }

    length = (ushort)(((expo-1)>>5)+1);
    data = new uint[length];
    frac = ldexp(frac, ((expo-1)&31)+1);

    if(length==1) data[0] = (uint)frac;
    else
    { uint bits = (uint)frac;
      data[1] = bits;
      data[0] = (uint)ldexp(frac-bits, 32);
    }
    
    sign = (short)(d<0 ? -1 : 1);
  }

  internal Integer(short sign, params uint[] data)
  { int length = calcLength(data);
    if(length>ushort.MaxValue) throw new NotImplementedException("Integer values larger than 2097120 bits");
    this.sign=length==0 ? (short)0 : sign; this.data=data; this.length=(ushort)length;
  }
  #endregion

  public Integer Abs { get { return sign==-1 ? -this : this; } }
  public int Sign { get { return sign; } }

  public override bool Equals(object obj) { return obj is Integer ? CompareTo((Integer)obj)==0 : false; }

  public override int GetHashCode()
  { uint hash=0;
    for(int i=0; i<length; i++) hash ^= data[i];
    return (int)hash;
  }
  
  #region ToString
  public override string ToString() { return ToString(10); }
  public string ToString(int radix)
  { if(radix<2 || radix>36) throw new ArgumentOutOfRangeException("radix", radix, "radix must be from 2 to 36");
    if(this==0) return "0";
    if(this==1) return sign==-1 ? "-1" : "1";

    System.Text.StringBuilder sb = new System.Text.StringBuilder();
    const string charSet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    uint[] data = (uint[])this.data.Clone();
    int    len  = this.length-1;
    while(true)
    { sb.Append(charSet[divideInPlace(data, len+1, (uint)radix)]);
      while(data[len]==0) if(len--==0) goto done;
    }
    done:
    if(sign==-1) sb.Append('-');
    char[] chars = new char[sb.Length];
    len=sb.Length-1;
    for(int i=0; i<=len; i++) chars[i]=sb[len-i];
    return new string(chars);
  }
  #endregion

  public static Integer Parse(string s) { return new Integer(s, 10); }
  public static Integer Parse(string s, int radix) { return new Integer(s, radix); }

  public static readonly Integer MinusOne = new Integer(-1, new uint[1]{1});
  public static readonly Integer One  = new Integer(1, new uint[1]{1});
  public static readonly Integer Zero = new Integer(0, new uint[0]);
  
  #region Comparison operators
  public static bool operator==(Integer a, Integer b) { return a.CompareTo(b)==0; }
  public static bool operator==(Integer a, int b)     { return a.CompareTo(b)==0; }
  public static bool operator==(Integer a, long b)    { return a.CompareTo(b)==0; }
  public static bool operator==(Integer a, uint b)    { return a.CompareTo(b)==0; }
  public static bool operator==(Integer a, ulong b)   { return a.CompareTo(b)==0; }
  public static bool operator==(int a, Integer b)     { return b.CompareTo(a)==0; }
  public static bool operator==(long a, Integer b)    { return b.CompareTo(a)==0; }
  public static bool operator==(uint a, Integer b)    { return b.CompareTo(a)==0; }
  public static bool operator==(ulong a, Integer b)   { return b.CompareTo(a)==0; }

  public static bool operator!=(Integer a, Integer b) { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, int b)     { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, long b)    { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, uint b)    { return a.CompareTo(b)!=0; }
  public static bool operator!=(Integer a, ulong b)   { return a.CompareTo(b)!=0; }
  public static bool operator!=(int a, Integer b)     { return b.CompareTo(a)!=0; }
  public static bool operator!=(long a, Integer b)    { return b.CompareTo(a)!=0; }
  public static bool operator!=(uint a, Integer b)    { return b.CompareTo(a)!=0; }
  public static bool operator!=(ulong a, Integer b)   { return b.CompareTo(a)!=0; }

  public static bool operator<(Integer a, Integer b) { return a.CompareTo(b)<0; }
  public static bool operator<(Integer a, int b)     { return a.CompareTo(b)<0; }
  public static bool operator<(Integer a, long b)    { return a.CompareTo(b)<0; }
  public static bool operator<(Integer a, uint b)    { return a.CompareTo(b)<0; }
  public static bool operator<(Integer a, ulong b)   { return a.CompareTo(b)<0; }
  public static bool operator<(int a, Integer b)     { return b.CompareTo(a)>0; }
  public static bool operator<(long a, Integer b)    { return b.CompareTo(a)>0; }
  public static bool operator<(uint a, Integer b)    { return b.CompareTo(a)>0; }
  public static bool operator<(ulong a, Integer b)   { return b.CompareTo(a)>0; }

  public static bool operator<=(Integer a, Integer b) { return a.CompareTo(b)<=0; }
  public static bool operator<=(Integer a, int b)     { return a.CompareTo(b)<=0; }
  public static bool operator<=(Integer a, long b)    { return a.CompareTo(b)<=0; }
  public static bool operator<=(Integer a, uint b)    { return a.CompareTo(b)<=0; }
  public static bool operator<=(Integer a, ulong b)   { return a.CompareTo(b)<=0; }
  public static bool operator<=(int a, Integer b)     { return b.CompareTo(a)>=0; }
  public static bool operator<=(long a, Integer b)    { return b.CompareTo(a)>=0; }
  public static bool operator<=(uint a, Integer b)    { return b.CompareTo(a)>=0; }
  public static bool operator<=(ulong a, Integer b)   { return b.CompareTo(a)>=0; }

  public static bool operator>(Integer a, Integer b) { return a.CompareTo(b)>0; }
  public static bool operator>(Integer a, int b)     { return a.CompareTo(b)>0; }
  public static bool operator>(Integer a, long b)    { return a.CompareTo(b)>0; }
  public static bool operator>(Integer a, uint b)    { return a.CompareTo(b)>0; }
  public static bool operator>(Integer a, ulong b)   { return a.CompareTo(b)>0; }
  public static bool operator>(int a, Integer b)     { return b.CompareTo(a)<0; }
  public static bool operator>(long a, Integer b)    { return b.CompareTo(a)<0; }
  public static bool operator>(uint a, Integer b)    { return b.CompareTo(a)<0; }
  public static bool operator>(ulong a, Integer b)   { return b.CompareTo(a)<0; }

  public static bool operator>=(Integer a, Integer b) { return a.CompareTo(b)>=0; }
  public static bool operator>=(Integer a, int b)     { return a.CompareTo(b)>=0; }
  public static bool operator>=(Integer a, long b)    { return a.CompareTo(b)>=0; }
  public static bool operator>=(Integer a, uint b)    { return a.CompareTo(b)>=0; }
  public static bool operator>=(Integer a, ulong b)   { return a.CompareTo(b)>=0; }
  public static bool operator>=(int a, Integer b)     { return b.CompareTo(a)<=0; }
  public static bool operator>=(long a, Integer b)    { return b.CompareTo(a)<=0; }
  public static bool operator>=(uint a, Integer b)    { return b.CompareTo(a)<=0; }
  public static bool operator>=(ulong a, Integer b)   { return b.CompareTo(a)<=0; }
  #endregion
  
  #region Arithmetic and bitwise operators
  #region Addition
  public static Integer operator+(Integer a, Integer b)
  { int c = a.absCompareTo(b);
    if(a.sign==b.sign) // addition
    { if(c>=0) return new Integer(a.sign, add(a.data, a.length, b.data, b.length));
      else return new Integer(b.sign, add(b.data, b.length, a.data, a.length));
    }
    else // subtraction
    { if(c>0) return new Integer(a.sign, sub(a.data, a.length, b.data, b.length));
      else if(c==0) return Zero;
      else return new Integer(b.sign, sub(b.data, b.length, a.data, a.length));
    }
  }
  public static Integer operator+(Integer a, int b)
  { uint ub=intToUint(b);
    short bsign=(short)Math.Sign(b);
    if(a.sign==bsign) return new Integer((short)bsign, add(a.data, a.length, ub));
    else
    { int c=a.absCompareTo(ub);
      if(c>0) return new Integer(a.sign, sub(a.data, a.length, ub));
      else if(c==0) return Zero;
      else return new Integer(bsign, sub(ub, a.data, a.length));
    }
  }
  public static Integer operator+(Integer a, uint b)
  { if(a.sign==-1) 
    { int c=a.absCompareTo(b);
      if(c>0) return new Integer(a.sign, sub(a.data, a.length, b));
      else if(c==0) return Zero;
      else return new Integer(1, sub(b, a.data, a.length));
    }
    else return new Integer((short)1, add(a.data, a.length, b));
  }
  public static Integer operator+(Integer a, long b)  { return a + new Integer(b); }
  public static Integer operator+(Integer a, ulong b) { return a + new Integer(b); }
  public static Integer operator+(int a, Integer b)   { return b+a; }
  public static Integer operator+(uint a, Integer b)  { return b+a; }
  public static Integer operator+(long a, Integer b)  { return new Integer(a) + b; }
  public static Integer operator+(ulong a, Integer b) { return new Integer(a) + b; }
  #endregion
  
  #region Subtraction
  public static Integer operator-(Integer a, Integer b)
  { int c = a.absCompareTo(b);
    if(a.sign==b.sign) // subtraction
    { if(c>0) return new Integer(a.sign, sub(a.data, a.length, b.data, b.length));
      else if(c==0) return Zero;
      else return new Integer((short)-b.sign, sub(b.data, b.length, a.data, a.length));
    }
    else // addition
    { if(c>0) return new Integer(a.sign, add(a.data, a.length, b.data, b.length));
      else return new Integer((short)-b.sign, add(b.data, b.length, a.data, a.length));
    }
  }
  public static Integer operator-(Integer a, int b)
  { uint ub=intToUint(b);
    int   c=a.absCompareTo(ub);
    short bsign=(short)Math.Sign(b);
    if(a.sign==bsign)
    { if(c>0) return new Integer(a.sign, sub(a.data, a.length, ub));
      else if(c==0) return Zero;
      else return new Integer((short)-bsign, sub(ub, a.data, a.length));
    }
    else return new Integer(c>0 ? a.sign : (short)-bsign, add(a.data, a.length, ub));
  }
  public static Integer operator-(Integer a, uint b)
  { int c=a.absCompareTo(b);
    if(a.sign==-1) return new Integer(c>0 ? a.sign : (short)-1, add(a.data, a.length, b));
    else
    { if(c>0) return new Integer(a.sign, sub(a.data, a.length, b));
      else if(c==0) return Zero;
      else return new Integer((short)-1, sub(b, a.data, a.length));
    }
  }
  public static Integer operator-(Integer a, long b)  { return a - new Integer(b); }
  public static Integer operator-(Integer a, ulong b) { return a - new Integer(b); }
  public static Integer operator-(int a, Integer b)
  { uint ua=intToUint(a);
    int   c=-b.absCompareTo(ua);
    short asign=(short)Math.Sign(a);
    if(asign==b.sign) // subtraction
    { if(c>0) return new Integer(asign, sub(ua, b.data, b.length));
      else if(c==0) return Zero;
      else return new Integer((short)-b.sign, sub(b.data, b.length, ua));
    }
    else return new Integer((short)(c>0 ? asign : -b.sign), add(b.data, b.length, ua)); // addition
  }
  public static Integer operator-(uint a, Integer b)
  { if(b.sign==-1) return new Integer((short)1, add(b.data, b.length, a)); // addition
    else
    { int c=-b.absCompareTo(a);
      if(c>0) return new Integer((short)1, sub(a, b.data, b.length));
      else if(c==0) return Zero;
      else return new Integer((short)-b.sign, sub(b.data, b.length, a));
    }
  }
  public static Integer operator-(long a, Integer b)  { return new Integer(a) - b; }
  public static Integer operator-(ulong a, Integer b) { return new Integer(a) - b; }
  #endregion
  
  #region Multiplication
  public static Integer operator*(Integer a, Integer b)
  { int nsign = a.sign*b.sign;
    return nsign==0 ? Zero : new Integer((short)nsign, multiply(a.data, a.length, b.data, b.length));
  }
  public static Integer operator*(Integer a, int b)
  { int nsign = a.sign * Math.Sign(b);
    return nsign==0 ? Zero : new Integer((short)nsign, multiply(a.data, a.length, intToUint(b)));
  }
  public static Integer operator*(Integer a, uint b)
  { return b==0 || a.sign==0 ? Zero : new Integer(a.sign, multiply(a.data, a.length, b));
  }
  public static Integer operator*(Integer a, long b)  { return a * new Integer(b); }
  public static Integer operator*(Integer a, ulong b) { return a * new Integer(b); }
  public static Integer operator*(int a, Integer b)
  { int nsign = Math.Sign(a) * b.sign;
    return nsign==0 ? Zero : new Integer((short)nsign, multiply(b.data, b.length, intToUint(a)));
  }
  public static Integer operator*(uint a, Integer b)
  { return a==0 || b.sign==0 ? Zero : new Integer(b.sign, multiply(b.data, b.length, a));
  }
  public static Integer operator*(long a, Integer b)  { return new Integer(a) * b; }
  public static Integer operator*(ulong a, Integer b) { return new Integer(a) * b; }
  #endregion

  #region Division
  public static Integer operator/(Integer a, Integer b)
  { if(b.sign==0) throw new DivideByZeroException("long division by zero");
    if(a.sign==0) return Zero;

    int c = a.absCompareTo(b);
    if(c==0) return a.sign==b.sign ? One : MinusOne;

    uint[] dummy;
    return new Integer((short)(a.sign*b.sign), c>0 ? divide(a.data, a.length, b.data, b.length, out dummy)
                                                   : divide(b.data, b.length, a.data, a.length, out dummy));
  }
  public static Integer operator/(Integer a, int b)
  { if(b==0) throw new DivideByZeroException("long division by zero");
    if(a.sign==0) return Zero;
    uint dummy;
    return new Integer((short)(a.sign*Math.Sign(b)), divide(a.data, a.length, intToUint(b), out dummy));
  }
  public static Integer operator/(Integer a, uint b)
  { if(b==0) throw new DivideByZeroException("long division by zero");
    if(a.sign==0) return Zero;
    uint dummy;
    return new Integer(a.sign, divide(a.data, a.length, b, out dummy));
  }
  public static Integer operator/(Integer a, long b)  { return a / new Integer(b); }
  public static Integer operator/(Integer a, ulong b) { return a / new Integer(b); }
  public static Integer operator/(int a, Integer b)   { return new Integer(a) / b; }
  public static Integer operator/(uint a, Integer b)  { return new Integer(a) / b; }
  public static Integer operator/(long a, Integer b)  { return new Integer(a) / b; }
  public static Integer operator/(ulong a, Integer b) { return new Integer(a) / b; }
  #endregion
  
  #region Modulus
  public static Integer operator%(Integer a, Integer b)
  { if(b.sign==0) throw new DivideByZeroException("long modulus by zero");
    if(a.sign==0) return b;
    int c = a.absCompareTo(b);
    if(c==0) return Zero;

    uint[] remainder;
    if(c>0) divide(a.data, a.length, b.data, b.length, out remainder);
    else divide(a.data, a.length, b.data, b.length, out remainder);
    return new Integer(b.sign, remainder);
  }
  public static Integer operator%(Integer a, int b)
  { if(b==0) throw new DivideByZeroException("long modulus by zero");
    if(a.sign==0) return new Integer(b);

    uint remainder;
    divide(a.data, a.length, intToUint(b), out remainder);
    Integer ret = new Integer(remainder);
    if(b<0) ret.sign = -1;
    return ret;
  }
  public static Integer operator%(Integer a, uint b)
  { if(b==0) throw new DivideByZeroException("long modulus by zero");
    if(a.sign==0) return new Integer(b);

    uint remainder;
    divide(a.data, a.length, b, out remainder);
    return new Integer(remainder);
  }
  public static Integer operator%(Integer a, long b)  { return a % new Integer(b); }
  public static Integer operator%(Integer a, ulong b) { return a % new Integer(b); }
  public static Integer operator%(int a, Integer b)   { return new Integer(a) % b; }
  public static Integer operator%(uint a, Integer b)  { return new Integer(a) % b; }
  public static Integer operator%(long a, Integer b)  { return new Integer(a) % b; }
  public static Integer operator%(ulong a, Integer b) { return new Integer(a) % b; }
  #endregion
  
  #region Unary
  public static Integer operator-(Integer i) { return new Integer((short)-i.sign, i.data); }
  public static Integer operator~(Integer i) { return -(i+One); }
  #endregion
  
  #region Bitwise And
  public static Integer operator&(Integer a, Integer b)
  { bool nega=a.sign==-1, negb=b.sign==-1;
    if(!nega && !negb) return new Integer((short)1, bitand(a.data, a.length, b.data, b.length));
    uint[] data = bitand(a.data, a.length, nega, b.data, b.length, negb);
    return nega && negb ? new Integer((short)-1, twosComplement(data)) : new Integer((short)1, data);
  }
  public static Integer operator&(Integer a, int b)   { return a & new Integer(b); }
  public static Integer operator&(Integer a, uint b)  { return a & new Integer(b); }
  public static Integer operator&(Integer a, long b)  { return a & new Integer(b); }
  public static Integer operator&(Integer a, ulong b) { return a & new Integer(b); }
  public static Integer operator&(int a, Integer b)   { return new Integer(a) & b; }
  public static Integer operator&(uint a, Integer b)  { return new Integer(a) & b; }
  public static Integer operator&(long a, Integer b)  { return new Integer(a) & b; }
  public static Integer operator&(ulong a, Integer b) { return new Integer(a) & b; }
  #endregion

  #region Bitwise Or
  public static Integer operator|(Integer a, Integer b)
  { bool nega=a.sign==-1, negb=b.sign==-1;
    if(!nega && !negb) return new Integer((short)1, bitor(a.data, a.length, b.data, b.length));
    return new Integer((short)-1, twosComplement(bitor(a.data, a.length, nega, b.data, b.length, negb)));
  }
  public static Integer operator|(Integer a, int b)   { return a | new Integer(b); }
  public static Integer operator|(Integer a, uint b)  { return a | new Integer(b); }
  public static Integer operator|(Integer a, long b)  { return a | new Integer(b); }
  public static Integer operator|(Integer a, ulong b) { return a | new Integer(b); }
  public static Integer operator|(int a, Integer b)   { return new Integer(a) | b; }
  public static Integer operator|(uint a, Integer b)  { return new Integer(a) | b; }
  public static Integer operator|(long a, Integer b)  { return new Integer(a) | b; }
  public static Integer operator|(ulong a, Integer b) { return new Integer(a) | b; }
  #endregion
  
  #region Bitwise Xor
  public static Integer operator^(Integer a, Integer b)
  { bool nega=a.sign==-1, negb=b.sign==-1;
    if(!nega && !negb) return new Integer((short)1, bitxor(a.data, a.length, b.data, b.length));
    uint[] data = bitxor(a.data, a.length, nega, b.data, b.length, negb);
    return nega&&negb || !nega && !negb ? new Integer((short)1, data) : new Integer((short)-1, twosComplement(data));
  }
  public static Integer operator^(Integer a, int b)
  { return a.sign==-1 || b<0 ? a ^ new Integer(b) : new Integer((short)1, bitxor(a.data, a.length, (uint)b));
  }
  public static Integer operator^(Integer a, uint b)
  { return a.sign==-1 ? a ^ new Integer(b) : new Integer((short)1, bitxor(a.data, a.length, b));
  }
  public static Integer operator^(Integer a, long b)
  { return a.sign==-1 || b<0 ? a ^ new Integer(b) : new Integer((short)1, bitxor(a.data, a.length, (ulong)b));
  }
  public static Integer operator^(Integer a, ulong b)
  { return a.sign==-1 ? a ^ new Integer(b) : new Integer((short)1, bitxor(a.data, a.length, b));
  }
  public static Integer operator^(int a, Integer b)
  { return a<0 || b.sign==-1 ? new Integer(a) ^ b : new Integer((short)1, bitxor(b.data, b.length, (uint)a));
  }
  public static Integer operator^(uint a, Integer b)
  { return b.sign==-1 ? new Integer(a) ^ b : new Integer((short)1, bitxor(b.data, b.length, a));
  }
  public static Integer operator^(long a, Integer b)
  { return a<0 || b.sign==-1 ? new Integer(a) ^ b : new Integer((short)1, bitxor(b.data, b.length, (ulong)a));
  }
  public static Integer operator^(ulong a, Integer b)
  { return b.sign==-1 ? new Integer(a) ^ b : new Integer((short)1, bitxor(b.data, b.length, a));
  }
  #endregion

  #region Shifting
  public static Integer operator<<(Integer a, int shift)
  { if(a.sign==0 || shift==0) return a;
    return new Integer(a.sign, shift<0 ? rshift(a.data, a.length, -shift) : lshift(a.data, a.length, shift));
  }
  public static Integer operator>>(Integer a, int shift)
  { if(a.sign==0 || shift==0) return a;
    return new Integer(a.sign, shift<0 ? lshift(a.data, a.length, -shift) : rshift(a.data, a.length, shift));
  }
  #endregion
  #endregion

  #region CompareTo
  public int CompareTo(Integer o)
  { if(sign!=o.sign) return sign-o.sign;
    int len=length, olen=o.length;
    if(len!=olen) return len-olen;
    for(int i=len-1; i>=0; i--) if(data[i]!=o.data[i]) return (int)(data[i]-o.data[i]);
    return 0;
  }

  public int CompareTo(int i)
  { int osign = i>0 ? 1 : i<0 ? -1 : 0;
    if(sign!=osign) return sign-osign;
    switch(length)
    { case 1: return uintCompare(data[0], intToUint(i));
      case 0: return 0; // 'i' can't be nonzero here because the 'sign!=osign' check above would have caught it
      default: return sign;
    }
  }

  public int CompareTo(uint i)
  { int osign = i==0 ? 0 : 1;
    if(sign!=osign) return sign-osign;
    return length==1 ? uintCompare(data[0], i) : length;
  }

  public int CompareTo(long i)
  { int osign = i>0 ? 1 : i<0 ? -1 : 0;
    if(sign!=osign) return sign-osign;
    switch(length)
    { case 2: return ulongCompare(((ulong)data[1]<<32) | data[0], longToUlong(i));
      case 1: return ((ulong)i>>32)==0 ? uintCompare(data[0], (uint)(ulong)i) : -sign;
      case 0: return 0;
      default: return sign;
    }
  }

  public int CompareTo(ulong i)
  { int osign = i==0 ? 0 : 1;
    if(sign!=osign) return sign-osign;
    switch(length)
    { case 2: return ulongCompare(((ulong)data[1]<<32) | data[0], i);
      case 1: return (i>>32)==0 ? uintCompare(data[0], (uint)i) : -sign;
      case 0: return 0;
      default: return sign;
    }
  }
  #endregion

  #region IConvertible Members
  public ulong ToUInt64()
  { if(sign==-1 || length>2) throw new OverflowException("Integer won't fit into a ulong");
    if(sign==0) return 0;
    return data.Length==1 ? data[0] : (ulong)data[1]<<32 | data[0];
  }
  public ulong ToUInt64(IFormatProvider provider) { return ToUInt64(); }

  public sbyte ToSByte()
  { if(length>1 || (sign>0 && this>sbyte.MaxValue) || (sign==-1 && this<sbyte.MinValue))
      throw new OverflowException("Integer won't fit into an sbyte");
    return length==0 ? (sbyte)0 : (sbyte)((int)data[0] * sign);
  }
  public sbyte ToSByte(IFormatProvider provider) { return ToSByte(); }

  public double ToDouble()
  { if(length==0) return 0.0;
    int    len = length-1;
    double ret = data[len];
    if(len>0)
    { ret = ret*4294967296.0 + data[--len];
      // can't happen because 'len' is a ushort. if(len>int.MaxValue/32) throw new OverflowException("long int too large to convert to float");
      if(len>0)
      { ret = ldexp(ret, len*32);
        if(double.IsPositiveInfinity(ret)) throw new OverflowException("long int too large to convert to float");
      }
    }
    if(sign==-1) ret = -ret;
    return ret;
  }
  public double ToDouble(IFormatProvider provider) { return ToDouble(); }

  public DateTime ToDateTime(IFormatProvider provider) { throw new InvalidCastException(); }
  public float ToSingle() { return (float)ToDouble(); }
  public float ToSingle(IFormatProvider provider) { return (float)ToDouble(); }
  public bool ToBoolean() { return sign!=0; }
  public bool ToBoolean(IFormatProvider provider) { return sign!=0; }

  public int ToInt32()
  { if(length==0) return 0;
    if(length>1 || (sign>0 && data[0]>(uint)int.MaxValue) || (sign==-1 && data[0]>0x80000000))
      throw new OverflowException("Integer won't fit into an int");
    return (int)data[0]*sign;
  }
  public int ToInt32(IFormatProvider provider) { return ToInt32(); }

  public ushort ToUInt16()
  { if(length==0) return 0;
    if(length>1 || data[0]>ushort.MaxValue) throw new OverflowException("Integer won't fit into a ushort");
    return (ushort)data[0];
  }
  public ushort ToUInt16(IFormatProvider provider) { return ToUInt16(); }

  public short ToInt16()
  { if(length==0) return 0;
    if(length>1 || (sign>0 && data[0]>(uint)short.MaxValue) || (sign==-1 && data[0]>(uint)-short.MinValue))
      throw new OverflowException("Integer won't fit into an int");
    return (short)((int)data[0]*sign);
  }
  public short ToInt16(IFormatProvider provider) { return ToInt16(); }

  public string ToString(IFormatProvider provider) { return ToString(); }

  public byte ToByte()
  { if(sign==-1 || length>1 || (sign>0 && this>byte.MaxValue))
      throw new OverflowException("Integer won't fit into a byte");
    return length==0 ? (byte)0 : (byte)data[0];
  }
  public byte ToByte(IFormatProvider provider) { return ToByte(); }

  public char ToChar()
  { if(length==0) return '\0';
    if(length>1 || data[0]>char.MaxValue) throw new OverflowException("Integer won't fit into a char");
    return (char)data[0];
  }
  public char ToChar(IFormatProvider provider) { return ToChar(); }

  public long ToInt64()
  { if(sign==-1 || length>2) throw new OverflowException("Integer won't fit into a long");
    if(sign==0) return 0;
    if(data.Length==1) return sign*(int)data[0];
    if((data[1]&0x80000000)!=0) throw new OverflowException("Integer won't fit into a long");
    return (long)((ulong)data[1]<<32 | data[0]);
  }
  public long ToInt64(IFormatProvider provider) { return ToInt64(); }

  public System.TypeCode GetTypeCode() { return TypeCode.Object; }

  public decimal ToDecimal()
  { throw new NotImplementedException();
  }
  public decimal ToDecimal(IFormatProvider provider) { return ToDecimal(); }

  public object ToType(Type conversionType, IFormatProvider provider)
  { if(conversionType==typeof(int)) return ToInt32(provider);
    if(conversionType==typeof(double)) return ToDouble(provider);
    if(conversionType==typeof(long)) return ToInt64(provider);
    if(conversionType==typeof(bool)) return ToBoolean(provider);
    if(conversionType==typeof(string)) return ToString(provider);
    if(conversionType==typeof(uint)) return ToUInt32(provider);
    if(conversionType==typeof(ulong)) return ToUInt64(provider);
    if(conversionType==typeof(float)) return ToSingle(provider);
    if(conversionType==typeof(short)) return ToInt16(provider);
    if(conversionType==typeof(ushort)) return ToUInt16(provider);
    if(conversionType==typeof(byte)) return ToByte(provider);
    if(conversionType==typeof(sbyte)) return ToSByte(provider);
    if(conversionType==typeof(decimal)) return ToDecimal(provider);
    if(conversionType==typeof(char)) return ToChar(provider);
    throw new InvalidCastException();
  }

  public uint ToUInt32()
  { if(length>1) throw new OverflowException("Integer won't fit into a uint");
    return length==0 ? 0 : data[0];
  }
  public uint ToUInt32(IFormatProvider provider) { return ToUInt32(); }
  #endregion

  #region IComparable Members
  public int CompareTo(object obj)
  { if(obj is Integer) return CompareTo((Integer)obj);
    throw new ArgumentException();
  }
  #endregion
  
  #region ICloneable Members
  public object Clone() { return new Integer(sign, (uint[])data.Clone()); }
  #endregion

  #region Pow
  internal Integer Pow(uint power) // TODO: this can be optimized better
  { if(power==0) return One;
    if(power==2) return squared();
    if(power<0) throw new ArgumentOutOfRangeException("power", power, "power must be >= 0");

    Integer factor = this;
    Integer result = One;
    while(power!=0)
    { if((power&1)!=0) result *= factor;
      factor = factor.squared();
      power >>= 1;
    }
    return result; 
  }

  internal Integer Pow(uint power, object mod) // TODO: this can be optimized better
  { throw new NotImplementedException();
  }

  internal Integer Pow(Integer power) { return Pow(power.ToUInt32()); }
  internal Integer Pow(Integer power, object mod) { return Pow(power.ToUInt32(), mod); }
  #endregion

  int absCompareTo(Integer o)
  { int len=length, olen=o.length;
    if(len!=olen) return len-olen;
    for(int i=len-1; i>=0; i--) if(data[i]!=o.data[i]) return (int)(data[i]-o.data[i]);
    return 0;
  }

  int absCompareTo(uint i)
  { switch(length)
    { case 1: return uintCompare(data[0], i);
      case 0: return i==0 ? 0 : -1;
      default: return 1;
    }
  }

  Integer squared() { return this*this; } // TODO: this can be optimized much better

  internal uint[] data;
  internal ushort length;
  short sign;

  static unsafe uint[] add(uint[] a, int alen, uint b)
  { uint[] d = new uint[alen+1];
    fixed(uint* ab=a, db=d)
    { ulong sum=b;
      int i=0;
      for(; i<alen && (uint)sum!=0; i++)
      { sum += ab[i];
        db[i] = (uint)sum;
        sum >>= 32;
      }
      if((uint)sum!=0) db[alen] = (uint)sum;
      else for(; i<alen; i++) db[i]=ab[i];
    }
    return d;
  }

  static unsafe uint[] add(uint[] a, int alen, uint[] b, int blen) // assumes alen >= blen
  { uint[] d = new uint[alen+1];
    fixed(uint* ab=a, bb=b, db=d)
    { ulong sum=0;
      int i=0;
      for(; i<blen; i++)
      { sum  += ab[i] + bb[i];
        db[i] = (uint)sum;
        sum >>= 32;
      }
      for(; i<alen && (uint)sum!=0; i++)
      { sum  += ab[i];
        db[i] = (uint)sum;
        sum >>= 32;
      }
      if((uint)sum!=0) db[alen] = (uint)sum;
      else for(; i<alen; i++) db[i]=ab[i];
    }
    return d;
  }

  static uint[] bitand(uint[] a, int alen, uint b)
  { return alen==0 ? Zero.data : new uint[1] { a[0]&b };
  }

  static uint[] bitand(uint[] a, int alen, ulong b)
  { uint[] ret;
    if(alen>1)
    { ret = new uint[2];
      ret[0] = a[0]&(uint)b;
      ret[1] = a[1]&(uint)(b>>32);
    }
    else if(alen!=0)
    { ret = new uint[1];
      ret[0] = a[0]&(uint)b;
    }
    else ret=Zero.data;
    return ret;
  }

  static unsafe uint[] bitand(uint[] a, int alen, uint[] b, int blen)
  { int dlen = Math.Min(alen, blen);
    uint[] d = new uint[dlen];
    fixed(uint* ab=a, bb=b, db=d)
      for(int i=0; i<dlen; i++) db[i]=ab[i]&db[i];
    return d;
  }

  static unsafe uint[] bitand(uint[] a, int alen, bool aneg, uint[] b, int blen, bool bneg)
  { if(alen<blen)
    { uint[] t=a; a=b; b=t;
      int it=alen; alen=blen; blen=it;
      bool bt=aneg; aneg=bneg; bneg=bt;
    }

    uint[] d = new uint[blen];
    fixed(uint* ab=a, bb=b, db=d)
    { int i=0;
      bool nza=false, nzb=false;
      for(; i<blen; i++) db[i] = getOne(aneg, ab[i], ref nza) & getOne(bneg, bb[i], ref nzb);
      if(bneg) for(; i<alen; i++) db[i] = getOne(aneg, ab[i], ref nza);
    }
    return d;
  }

  static uint[] bitor(uint[] a, int alen, uint b)
  { uint[] d = (uint[])a.Clone();
    if(alen!=0) d[0] |= b;
    return d;
  }

  static uint[] bitor(uint[] a, int alen, ulong b)
  { uint[] d = (uint[])a.Clone();
    if(alen>1)
    { d[0] |= (uint)b;
      d[1] |= (uint)(b>>32);
    }
    else if(alen!=0) d[0] |= (uint)b;
    return d;
  }

  static unsafe uint[] bitor(uint[] a, int alen, uint[] b, int blen)
  { if(alen<blen)
    { uint[] t=a; a=b; b=t;
      int it=alen; alen=blen; blen=it;
    }

    uint[] d = new uint[alen];
    fixed(uint* ab=a, bb=b, db=d)
    { int i=0;
      for(; i<blen; i++) db[i]=ab[i]|db[i];
      for(; i<alen; i++) db[i]=ab[i];
    }
    return d;
  }

  static unsafe uint[] bitor(uint[] a, int alen, bool aneg, uint[] b, int blen, bool bneg)
  { if(alen<blen)
    { uint[] t=a; a=b; b=t;
      int it=alen; alen=blen; blen=it;
      bool bt=aneg; aneg=bneg; bneg=bt;
    }

    uint[] d = new uint[alen];
    fixed(uint* ab=a, bb=b, db=d)
    { bool nza=false, nzb=false;
      int i=0;
      for(; i<blen; i++) db[i] = getOne(aneg, ab[i], ref nza) | getOne(bneg, bb[i], ref nzb);
      if(bneg) for(; i<alen; i++) db[i] = uint.MaxValue;
      else for(; i<alen; i++) db[i] = getOne(aneg, ab[i], ref nza);
    }
    return d;
  }

  static uint[] bitxor(uint[] a, int alen, uint b)
  { uint[] d = (uint[])a.Clone();
    if(alen!=0) d[0] ^= b;
    return d;
  }

  static uint[] bitxor(uint[] a, int alen, ulong b)
  { uint[] d = (uint[])a.Clone();
    if(alen>1)
    { d[0] ^= (uint)b;
      d[1] ^= (uint)(b>>32);
    }
    else if(alen!=0) d[0] ^= (uint)b;
    return d;
  }

  static unsafe uint[] bitxor(uint[] a, int alen, uint[] b, int blen)
  { if(alen<blen)
    { uint[] t=a; a=b; b=t;
      int it=alen; alen=blen; blen=it;
    }

    uint[] d = new uint[alen];
    fixed(uint* ab=a, bb=b, db=d)
    { int i=0;
      for(; i<blen; i++) db[i]=ab[i]^db[i];
      for(; i<alen; i++) db[i]=ab[i];
    }
    return d;
  }

  static unsafe uint[] bitxor(uint[] a, int alen, bool aneg, uint[] b, int blen, bool bneg)
  { if(alen<blen)
    { uint[] t=a; a=b; b=t;
      int it=alen; alen=blen; blen=it;
      bool bt=aneg; aneg=bneg; bneg=bt;
    }

    uint[] d = new uint[alen];
    fixed(uint* ab=a, bb=b, db=d)
    { bool nza=false, nzb=false;
      int i=0;
      for(; i<blen; i++) db[i] = getOne(aneg, ab[i], ref nza) ^ getOne(bneg, bb[i], ref nzb);
      if(bneg) for(; i<alen; i++) db[i] = ~getOne(aneg, ab[i], ref nza);
      else for(; i<alen; i++) db[i] = getOne(aneg, ab[i], ref nza);
    }
    return d;
  }

  static int calcLength(uint[] data)
  { int len = data.Length-1; 
    while(len>=0 && data[len]==0) len--;
    return len+1;
  }

  static unsafe uint[] divide(uint[] a, int alen, uint b, out uint remainder) // assumes b>0
  { uint[] d = new uint[alen];

    fixed(uint* ab=a, db=d)
    { ulong rem=0;
      while(alen-- != 0)
      { rem = (rem<<32) | ab[alen];
        d[alen] = (uint)(rem/b); // it'd be nice to combine rem/b and rem%b into one operation,
        rem %= b;                // but Math.DivRem() doesn't support unsigned longs
      }
      remainder = (uint)rem;
    }
    return d;
  }

  // assumes avalue > bvalue and blen!=0
  // this algorithm was shamelessly copied from Mono.Math
  static unsafe uint[] divide(uint[] a, int alen, uint[] b, int blen, out uint[] remainder)
  { int rlen=alen+1, dlen=blen+1;
    fixed(uint* ab=a)
    { uint[] d, rem;
      int shift=0, rpos=alen-blen;
      { uint mask=0x80000000, val=b[blen-1];
        while(mask!=0 && (val&mask)==0) { shift++; mask>>=1; }

        d=new uint[rpos+1];
        if(shift==0) rem = (uint[])a.Clone();
        else
        { rem  = lshift(a, alen, shift);
          b    = lshift(b, blen, shift);
          blen = calcLength(b);
        }
      }

      fixed(uint* bb=b, db=d, rb=rem)
      { int j=rlen-blen, pos=rlen-1;
        uint  firstdb  = bb[blen-1];
        ulong seconddb = bb[blen-2];
        
        while(j!=0)
        { ulong dividend=((ulong)rb[pos]<<32) + rb[pos-1], qhat=dividend/firstdb, rhat=dividend%firstdb;
          mloop:
          if(qhat==0x100000000 || qhat*seconddb>(rhat<<32)+rb[pos-2])
          { qhat--;
            rhat += firstdb;
            if(rhat<0x100000000) goto mloop;
          }

          uint uiqhat=(uint)qhat;
          int  dpos=0, npos=pos-dlen+1;

          rhat=0; // commandeering this variable
          do
          { rhat += (ulong)bb[dpos] * uiqhat;
            uint t = rb[npos];
            rb[npos] -= (uint)rhat;
            rhat >>= 32;
            if(rb[npos] > t) rhat++;
            npos++;
          } while(++dpos<dlen);

          npos = pos-dlen+1;
          dpos = 0;

          if(rhat != 0)
          { uiqhat--; rhat=0;
            do
            { rhat += (ulong)rb[npos] + bb[dpos];
              rb[npos] = (uint)rhat;
              rhat >>= 32;
              npos++;
            } while(++dpos<dlen);
          }

          db[rpos--] = uiqhat;
          pos--; j--;
        }
      }
      
      remainder = shift==0 ? rem : rshift(rem, rlen, shift);
      return d;
    }
  }

  static unsafe int divideInPlace(uint[] data, int length, uint n) // only used for conversion to string
  { ulong rem=0;
    fixed(uint* db=data)
    { while(length--!=0)
      { rem = (rem<<32) | db[length];
        db[length] = (uint)(rem/n);
        rem %= n;
      }
    }
    return (int)(uint)rem;
  }

  static uint extend(uint value, ref bool snz)
  { if(snz) return ~value;
    else if(value==0) return 0;
    else
    { snz=true;
      return ~value+1;
    }
  }

  static uint getOne(bool neg, uint value, ref bool snz) { return neg ? extend(value, ref snz) : value; }

  static uint intToUint(int i)
  { if(i>=0) return (uint)i;
    else
    { uint ui = (uint)-i;
      return ui==0 ? (uint)2147483648 : ui;
    }
  }

  static ulong longToUlong(long i)
  { if(i>=0) return (ulong)i;
    else
    { ulong ul = (ulong)-i;
      return ul==0 ? (ulong)9223372036854775808 : ul;
    }
  }

  static unsafe uint[] lshift(uint[] a, int alen, int n) // assumes n>0
  { int whole=n>>5, nlen=alen+whole+1;
    uint[] d = new uint[nlen];
    n &= 31;

    fixed(uint* ab=a, db=d)
    { if(n==0) for(int i=0; i<nlen; i++) db[i+whole] = ab[i];
      else
      { uint carry=0;
        int  n32=32-n;
        for(int i=0; i<nlen; i++)
        { uint v = ab[i];
          db[i+whole] = (v<<n) | carry;
          carry = v>>n32;
        }
      }
    }
    return d;
  }

  static unsafe uint[] multiply(uint[] a, int alen, uint b)
  { uint[] d = new uint[alen+1];
    fixed(uint* ab=a, db=d)
    { ulong carry=0;
      int i=0;
      for(; i<alen; i++)
      { carry += (ulong)ab[i] * (ulong)b;
        db[i] = (uint)carry;
        carry >>= 32;
      }
      db[i] = (uint)carry;
    }
    return d;
  }

  // TODO: this is a rather naive algorithm. optimize it.
  static unsafe uint[] multiply(uint[] a, int alen, uint[] b, int blen)
  { uint[] d = new uint[alen+blen];
    fixed(uint* ab=a, bb=b, odb=d)
    { uint* ap=ab, ae=ap+alen, be=bb+blen, db=odb;
      for(; ap<ae; db++,ap++)
      { if(*ap==0) continue;
        ulong carry=0;
        uint* dp=db;
        for(uint *bp=bb; bp<be; dp++,bp++)
        { carry += (ulong)*ap * (ulong)*bp + *dp;
          *dp = (uint)carry;
          carry >>= 32;
        }
        if(carry!=0) *dp=(uint)carry;
      }
    }
    return d;
  }

  static uint[] resize(uint[] array, int length)
  { if(array.Length>=length) return array;
    uint[] narr = new uint[length];
    Array.Copy(array, narr, array.Length);
    return narr;
  }

  static unsafe uint[] rshift(uint[] a, int alen, int n) // assumes n>0
  { int whole=n>>5, nlen=alen-whole;
    uint[] d = new uint[nlen+1];
    n &= 31;

    fixed(uint* ab=a, db=d)
    { if(n==0) while(nlen-- != 0) db[nlen] = ab[nlen+whole];
      else
      { uint carry=0;
        int  n32=32-n;
        while(nlen-- != 0)
        { uint v = ab[nlen+whole];
          db[nlen] = (v>>n) | carry;
          carry = v << n32;
        }
      }
    }
    return d;
  }

  static unsafe uint[] sub(uint a, uint[] b, int blen) { return new uint[1] { a-b[0] }; } // assumes avalue >= bvalue

  static unsafe uint[] sub(uint[] a, int alen, uint b) // assumes avalue >= bvalue
  { uint[] d = (uint[])a.Clone();
    fixed(uint* db=d)
    { bool borrow = b>db[0];
      db[0] -= b;
      if(borrow) for(int i=1; i<alen; i++) if(db[i]--!=0) break;
    }
    return d;
  }

  static unsafe uint[] sub(uint[] a, int alen, uint[] b, int blen) // assumes avalue >= bvalue
  { uint[] d = new uint[alen];
    fixed(uint* ab=a, bb=b, db=d)
    { int  i=0;
      uint ai, bi;
      bool borrow=false;

      for(; i<blen; i++)
      { ai=ab[i]; bi=bb[i];
        if(borrow)
        { if(ai==0) ai=0xffffffff;
          else borrow = bi > --ai;
        }
        else if(bi>ai) borrow = true;
        db[i] = ai-bi;
      }

      if(borrow)
        for(; i<alen; i++)
        { ai = ab[i];
          db[i] = ai-1;
          if(ai!=0) { i++; break; }
        }
      for(; i<alen; i++) db[i] = ab[i];
    }
    return d;
  }

  static unsafe uint[] twosComplement(uint[] d)
  { fixed(uint* db=d)
    { int i=0, length=d.Length;
      uint v=0;
      for(; i<length; i++)
      { db[i] = v = ~db[i]+1;
        if(v!=0) { i++; break; }
      }
      if(v!=0) for(; i<length; i++) db[i] = ~db[i];
      else
      { d = resize(d, length+1);
        d[length] = 1;
      }
    }
    return d;
  }

  static int uintCompare(uint a, uint b) { return a>b ? 1 : a<b ? -1 : 0; }
  static int ulongCompare(ulong a, ulong b) { return a>b ? 1 : a<b ? -1 : 0; }

  [DllImport(ImportDll, CallingConvention=CallingConvention.Cdecl)]
  static extern double frexp(double v, out int e);
  [DllImport(ImportDll, CallingConvention=CallingConvention.Cdecl)]
  static extern double ldexp(double m, int e);
}

} // namespace NetLisp.Backend
