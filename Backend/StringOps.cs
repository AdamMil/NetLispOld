using System;
using System.Text;

namespace NetLisp.Backend
{

public sealed class StringOps
{ StringOps() { }

  public static string Multiply(string str, object times)
  { int n = Ops.ToInt(times);
    StringBuilder sb = new StringBuilder(str.Length*n);
    while(n-->0) sb.Append(str);
    return sb.ToString();
  }
}

} // namespace NetLisp.Backend