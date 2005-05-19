using System;
using NetLisp.AST;

namespace NetLisp.Frontend
{

public class App
{ static void Main()
  { Node[] prog = Parser.FromString(@"
(define blah (a b)
  (display (* a b 2)))
(define 1+ (a) (+ a 1))
(blah (1+ 5) 7)").Parse();

    Console.WriteLine(prog.ToCode());
  }
}

} // namespace NetLisp.Frontend