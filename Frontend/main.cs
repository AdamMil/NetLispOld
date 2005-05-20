using System;
using NetLisp.Backend;

namespace NetLisp.Frontend
{

public class App
{ static void Main()
  { Pair prog = Parser.FromString(@"
(set! ret
  (lambda (a)
    (lambda () a)))
(set! mem (ret 5))
(mem)
").Parse();
    Console.WriteLine(prog.ToString());
    Console.WriteLine(Ops.Eval(prog));
  }
}

} // namespace NetLisp.Frontend