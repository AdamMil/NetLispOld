using System;
using NetLisp.Backend;

namespace NetLisp.Frontend
{

public class App
{ static void Main()
  { Frame.Current = new Frame(new System.Collections.Specialized.HybridDictionary(),
                              new System.Collections.Hashtable(ReflectedType.FromType(typeof(NetLisp.Backend.Modules.Builtins)).Dict));
    try
    { Console.WriteLine(Ops.Repr(Ops.CompileRaw(Parser.FromString(syntax).Parse()).Run()));
      NetLisp.Backend.Modules.Builtins.eval(Parser.FromString(stdlib).Parse());
      
      string line;
      while((line=Console.ReadLine()) != null) Console.WriteLine(Ops.Repr(NetLisp.Backend.Modules.Builtins.eval(Parser.FromString(line).Parse())));
    }
    catch(InvalidProgramException) { SnippetMaker.DumpAssembly(); }
  }
  
  static string syntax = @"
(install-expander 'quasiquote
  (lambda (x e)
    ((lambda (_quasi _combine _isconst?)
       (set! _quasi 
         (lambda (skel)
           (if (null? skel) 'nil
               (if (symbol? skel) (list 'quote skel)
                   (if (not (pair? skel)) skel
                       (if (eq? (car skel) 'unquote) (cadr skel)
                           (if (eq? (car skel) 'quasiquote)
                               (_quasi (_quasi (cadr skel)))
                               (if (if (pair? (car skel))
                                       (eq? (caar skel) 'unquote-splicing) #f)
                                   (list 'append (cadar skel)
                                         (_quasi (cdr skel)))
                                   (_combine (_quasi (car skel))
                                                    (if (null? (cdr skel)) '()
                                                        (_quasi (cdr skel)))
                                                    skel)))))))))
       (set! _combine
         (lambda (lft rgt skel)
           (if (if (_isconst? lft) (_isconst? rgt) #f) (list 'quote skel)
               (if (null? rgt) (list 'list lft)
                   (if (if (pair? rgt) (eq? (car rgt) 'list) #f)
                       (cons 'list (cons lft (cdr rgt)))
                       (list 'cons lft rgt))))))
       (e (_quasi (cdr x)) e))
     nil nil (lambda (obj) (if (pair? obj) (eq? (car obj) 'quote) #f)))))
";

  static string stdlib = @"
  ";
}

} // namespace NetLisp.Frontend