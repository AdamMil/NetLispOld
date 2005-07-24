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

namespace NetLisp.Backend
{

public struct Complex
{ public Complex(double real) { this.real=real; imag=0; }
  public Complex(double real, double imag) { this.real=real; this.imag=imag; }

  public double Angle { get { return Math.Atan2(imag, real); } }
  public Complex Conjugate { get { return new Complex(real, -imag); } }
  public double Magnitude { get { return Math.Sqrt(real*real + imag*imag); } }

  public override bool Equals(object obj)
  { if(!(obj is Complex)) return false;
    Complex c = (Complex)obj;
    return c.real==real && c.imag==imag;
  }

  public override int GetHashCode() { return real.GetHashCode()^imag.GetHashCode(); }

  public override string ToString() { return ToString("G"); }
  public string ToString(string s)
  { System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.Append(real);
    if(imag>=0) sb.Append('+');
    sb.Append(imag);
    sb.Append('i');
    return sb.ToString();
  }

  public Complex Pow(Complex power)
  { double r, i;
	  if(power.real==0 && power.imag==0) { r=1; i=0; }
	  else if(real==0 && imag==0)
	  { if(power.imag!=0 || power.real<0) throw new DivideByZeroException("Complex Pow(): division by zero");
	    r=i=0;
	  }
	  else
	  { double vabs=Math.Sqrt(real*real+imag*imag), len=Math.Pow(vabs, power.real), at=Math.Atan2(imag, real),
	           phase=at*power.real;
		  if(power.imag!=0)
		  { len /= Math.Exp(at*power.imag);
			  phase += power.imag*Math.Log(vabs);
			}
  		r = len*Math.Cos(phase);
	  	i = len*Math.Sin(phase);
	  }
	  return new Complex(r, i);
  }
  
  public Complex Pow(int power)
  { if(power>100 || power<-100) return Pow(new Complex(power));
    else if(power>0) return powu(power);
	  else return new Complex(1, 0) / powu(-power);
  }

  public Complex Pow(double power)
  { int p = (int)power;
    return p==power ? Pow(p) : Pow(new Complex(power));
  }

  public double real, imag;

  public static Complex Acos(Complex z) { return Math.PI/2 - Asin(z); }

  // TODO: i suspect that these naive implementations have problems with certain inputs
  public static Complex Asin(Complex z)
  { Complex iz=new Complex(-z.imag, z.real);
    z = Log(iz + Sqrt(1 - z*z));
    return new Complex(z.imag, -z.real);
  }

  public static Complex Atan(Complex z)
  { Complex iz=new Complex(-z.imag, z.real);
    z = Log(1+iz) - Log(1-iz);
    return new Complex(z.imag/2, -z.real/2);
  }

  public static Complex Log(Complex c) { return new Complex(Math.Log(c.Magnitude), c.Angle); }
  public static Complex Log10(Complex c) { return new Complex(Math.Log10(c.Magnitude), c.Angle); }

  public static Complex Pow(double a, Complex b) { return new Complex(a).Pow(b); }

  public static Complex Sqrt(Complex c)
  { if(c.imag==0) return new Complex(Math.Sqrt(c.real), 0);
    double r=c.Magnitude, y=Math.Sqrt((r-c.real)/2), x=c.imag/(2*y);
    return x<0 ? new Complex(-x, -y) : new Complex(x, y);
  }

  public static Complex operator+(Complex a, Complex b) { return new Complex(a.real+b.real, a.imag+b.imag); }
  public static Complex operator+(Complex a, double  b) { return new Complex(a.real+b, a.imag); }
  public static Complex operator+(double  a, Complex b) { return new Complex(a+b.real, b.imag); }

  public static Complex operator-(Complex a, Complex b) { return new Complex(a.real-b.real, a.imag-b.imag); }
  public static Complex operator-(Complex a, double  b) { return new Complex(a.real-b, a.imag); }
  public static Complex operator-(double  a, Complex b) { return new Complex(a-b.real, -b.imag); }

  public static Complex operator*(Complex a, Complex b)
  { return new Complex(a.real*b.real - a.imag*b.imag, a.real*b.imag + a.imag*b.real);
  }
  public static Complex operator*(Complex a, double b)  { return new Complex(a.real*b, a.imag*b); }
  public static Complex operator*(double  a, Complex b) { return new Complex(a*b.real, a*b.imag); }

  public static Complex operator/(Complex a, Complex b)
  { double abs_breal = b.real < 0 ? -b.real : b.real;
	  double abs_bimag = b.imag < 0 ? -b.imag : b.imag;
	  double real, imag;

  	if(abs_breal >= abs_bimag)
  	{ if(abs_breal == 0.0) throw new DivideByZeroException("attempted complex division by zero");
	 	  else
	 	  { double ratio = b.imag / b.real;
	 		  double denom = b.real + b.imag * ratio;
	 		  real = (a.real + a.imag * ratio) / denom;
	 		  imag = (a.imag - a.real * ratio) / denom;
	 	  }
  	}
	  else
	  { double ratio = b.real / b.imag;
		  double denom = b.real * ratio + b.imag;
		  real = (a.real * ratio + a.imag) / denom;
		  imag = (a.imag * ratio - a.real) / denom;
	  }
	  return new Complex(real, imag);
  }
  public static Complex operator/(Complex a, double b) { return new Complex(a.real/b, a.imag/b); }
  public static Complex operator/(double a, Complex b) { return new Complex(a)/b; }

  public static Complex operator-(Complex a) { return new Complex(-a.real, -a.imag); }

  public static bool operator==(Complex a, Complex b) { return a.real==b.real && a.imag==b.imag; }
  public static bool operator==(Complex a, double b)  { return a.real==b && a.imag==0; }
  public static bool operator==(double a, Complex b)  { return a==b.real && b.imag==0; }

  public static bool operator!=(Complex a, Complex b) { return a.real!=b.real || a.imag!=b.imag; }
  public static bool operator!=(Complex a, double b)  { return a.real!=b || a.imag!=0; }
  public static bool operator!=(double a, Complex b)  { return a!=b.real || b.imag!=0; }
  
  public static readonly Complex Zero = new Complex(0);
  public static readonly Complex I = new Complex(0, 1);

  Complex powu(int power)
  { Complex r = new Complex(1, 0);
	  int mask = 1;
	  while(mask>0 && power>=mask)
	  { if((power&mask)!=0) r *= this;
		  mask <<= 1;
		  this *= this;
	  }
	  return r;
  }
}

} // namespace NetLisp.Backend