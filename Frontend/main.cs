using System;
using NetLisp.Backend;

namespace NetLisp.Frontend
{

/*
(set! add (lambda (a b) (+ a b)))
(set! sum (add 4 5))
(set! code '(add 1 2))
(set! sum (eval code))
*/

public class App
{ static void Main()
  { Pair prog = Parser.FromString(@"
(let ((a 5) (b 4))
  (let ((b 6)) a))
").Parse();
    Console.WriteLine(prog.ToString());
    Console.WriteLine(Ops.Eval(prog));
  }
}

} // namespace NetLisp.Frontend