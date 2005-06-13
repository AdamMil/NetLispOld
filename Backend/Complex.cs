using System;

namespace NetLisp.Backend
{

public struct Complex
{ public Complex(double real) { this.real=real; imag=0; }
  public Complex(double real, double imag) { this.real=real; this.imag=imag; }

  public Complex Conjugate { get { return new Complex(real, -imag); } }

  public override bool Equals(object obj)
  { if(!(obj is Complex)) return false;
    Complex c = (Complex)obj;
    return c.real==real && c.imag==imag;
  }

  public override int GetHashCode() { return real.GetHashCode() + imag.GetHashCode()*1000003; }

  public double abs() { return Math.Sqrt(real*real+imag*imag); }
  public Complex conjugate() { return new Complex(real, -imag); }

  public override string ToString() { return ToString("G"); }
  public string ToString(string s)
  { System.Text.StringBuilder sb = new System.Text.StringBuilder();
    sb.Append('(');
    sb.Append(real.ToString(s));
    if(imag>=0) sb.Append('+');
    sb.Append(imag.ToString(s));
    sb.Append("j)");
    return sb.ToString();
  }

  public double real, imag;

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
  
  internal Complex Pow(Complex power)
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
  
  internal Complex Pow(int power)
  { if(power>100 || power<-100) return Pow(new Complex(power));
    else if(power>0) return powu(power);
	  else return new Complex(1, 0) / powu(-power);
  }

  internal Complex Pow(double power)
  { int p = (int)power;
    return p==power ? Pow(p) : Pow(new Complex(power));
  }

  internal static Complex Pow(double a, Complex b) { return new Complex(a).Pow(b); }

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