using System;

namespace NetLisp.Runtime
{

public sealed class Ops
{ Ops() { }

  public static object InexactToExact(object number) { throw new NotImplementedException("inexact->exact"); }
  public static string Repr(object obj) { return obj.ToString(); throw new NotImplementedException("repr"); }
}

} // namespace NetLisp.Runtime