using System;
using System.Collections;
using NetLisp.Backend;

namespace NetLisp.Frontend
{

public class App
{ static void Main()
  { Options.Debug = true;

    TopLevel.Current = new TopLevel();
    Builtins.Bind(TopLevel.Current);

    Options.AllowInternal = true;
    Eval(stdlib);
    Options.AllowInternal = false;

    while(true)
    { string code = null;
      int  parens = 0;
      do
      { Console.Write(code==null ? ">>> " : "... ");
        string line = Console.ReadLine();
        if(line==null) goto done;
        for(int i=0; i<line.Length; i++)
          if(line[i]=='(') parens++;
          else if(line[i]==')') parens--;
        code += line;
      } while(parens>0);

      try { Eval(code); }
      catch(Exception e) { Console.WriteLine("ERROR: "+e.ToString()); }
    }
    done:
    SnippetMaker.DumpAssembly();
  }

  static void Eval(string code)
  { Parser p = Parser.FromString(code);
    while(true)
    { object obj = p.ParseOne();
      if(obj==Parser.EOF) return;
      Console.WriteLine(Ops.Repr(Builtins.eval(obj)));
    }
  }

  static string stdlib = @"
(define caar (lambda (x) (car (car x))))
(define cadr (lambda (x) (car (cdr x))))
(define cdar (lambda (x) (cdr (car x))))
(define cddr (lambda (x) (cdr (cdr x))))

(define caaar (lambda (x) (car (car (car x)))))
(define caadr (lambda (x) (car (car (cdr x)))))
(define cadar (lambda (x) (car (cdr (car x)))))
(define caddr (lambda (x) (car (cdr (cdr x)))))
(define cdaar (lambda (x) (cdr (car (car x)))))
(define cdadr (lambda (x) (cdr (car (cdr x)))))
(define cddar (lambda (x) (cdr (cdr (car x)))))
(define cdddr (lambda (x) (cdr (cdr (cdr x)))))

(define caaaar (lambda (x) (car (car (car (car x))))))
(define caaadr (lambda (x) (car (car (car (cdr x))))))
(define caadar (lambda (x) (car (car (cdr (car x))))))
(define caaddr (lambda (x) (car (car (cdr (cdr x))))))
(define cadaar (lambda (x) (car (cdr (car (car x))))))
(define cadadr (lambda (x) (car (cdr (car (cdr x))))))
(define caddar (lambda (x) (car (cdr (cdr (car x))))))
(define cadddr (lambda (x) (car (cdr (cdr (cdr x))))))
(define cdaaar (lambda (x) (cdr (car (car (car x))))))
(define cdaadr (lambda (x) (cdr (car (car (cdr x))))))
(define cdadar (lambda (x) (cdr (car (cdr (car x))))))
(define cdaddr (lambda (x) (cdr (car (cdr (cdr x))))))
(define cddaar (lambda (x) (cdr (cdr (car (car x))))))
(define cddadr (lambda (x) (cdr (cdr (car (cdr x))))))
(define cdddar (lambda (x) (cdr (cdr (cdr (car x))))))
(define cddddr (lambda (x) (cdr (cdr (cdr (cdr x))))))

(define list (lambda elems elems))

(define initial-expander
  (lambda (x e)
    (if (not (pair? x)) x
        (if (expander? (car x)) ((expander-function (car x)) x e)
            (map (lambda (x) (e x e)) x)))))

(define expand (lambda (x) (initial-expander x initial-expander)))
(define expand-once (lambda (x) (initial-expander x (lambda (x e) x))))

(install-expander 'quote (lambda (x e) x))

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
                               (if (if (pair? (car skel)) (eq? (caar skel) 'unquote-splicing) #f)
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
       (e (_quasi (cadr x)) e))
     nil nil (lambda (obj) (if (pair? obj) (eq? (car obj) 'quote) #f)))))

(install-expander 'define
  (lambda (x e)
    (if (pair? (cadr x))
        (e `(define ,(caadr x) (lambda ,(cdadr x) ,@(cddr x))) e)
           `(define ,@(map (lambda (x) (e x e)) (cdr x))))))

(define (#_body-expander x e)
  (set! x (e x e))
  (if (if (pair? (car x)) (eq? (caar x) 'define) #f)
      (let ((dar (cdar x)))
        (let ((name (car dar)))
          `((let ((,name nil))
              (set! ,name ,(cadr dar))
              ,@(cdr x)))))
      x))

(install-expander 'let
  (lambda (x e)
    (let ((bindings (cadr x)))
      (if (symbol? bindings) ; named let
          (let ((name bindings))
            (set! bindings (map (lambda (init) (if (pair? init)
                                                   init
                                                   (list init nil)))
                                (caddr x)))
            (e `(letrec ((,name (lambda ,(map car bindings) ,@(cdddr x))))
                  (,name ,@(map cadr bindings))) e))
          `(let ,bindings ,@(#_body-expander (cddr x) e))))))

(install-expander 'if
  (lambda (x e) `(if ,@(map (lambda (x) (e x e)) (cdr x)))))

(install-expander 'set!
  (lambda (x e) `(set! ,(cadr x) ,(e (caddr x) e))))

(install-expander 'lambda
  (lambda (x e) `(lambda ,(cadr x) ,@(#_body-expander (cddr x) e))))

(install-expander 'defmacro
  (lambda (x e)
    (define (make-macro pattern body)
      (define (destructure pattern access bindings)
        (if (null? pattern) bindings
            (if (symbol? pattern) (cons `(,pattern ,access) bindings)
                (if (pair? pattern)
                    (destructure (car pattern) `(car ,access)
                                 (destructure (cdr pattern) `(cdr ,access) bindings))))))
      (let ((x (gensym ""x""))  (e (gensym ""e"")))
        `(lambda (,x ,e)
          (,e (let ,(destructure pattern `(cdr ,x) nil) ,body) ,e))))
    (let ((keyword (cadr x))  (pattern (caddr x))  (body (cadddr x)))
      (e `(install-expander ',keyword ,(make-macro pattern body)) e))))

(defmacro cond (item . rest)
  `(if ,(if (eq? (car item) 'else) #t (car item))
       ,@(cdr item)
       ,(if (null? rest) nil `(cond ,@rest))))

(defmacro case (key . cases)
  (let ((keyvar (gensym ""key"")))
    `(let ((,keyvar ,key))
       (cond ,@(map (lambda (case)
                      `(,(cond ((pair? (car case)) `(memv ,keyvar ',(car case)))
                               ((eq? (car case) 'else) #t)
                               ((symbol? (car case)) `(eq? ,keyvar ',(car case)))
                               (#t `(eqv? ,keyvar ',(car case))))
                        ,@(cdr case)))
                    cases)))))

(defmacro and items
  (if (null? items) #t
      (if (null? (cdr items)) (car items)
          `(if ,(car items) (and ,@(cdr items))))))

(defmacro letrec (bindings . body)
  `(let ,(map (lambda (init) (if (pair? init) (car init) init)) bindings)
     ,@(apply append (map (lambda (init) (if (pair? init) `((set! ,(car init) ,(cadr init))))) bindings))
     ,@body))

(defmacro let* (bindings . body)
  (let rec ((bindings bindings))
    (if (null? bindings) `(begin ,@body)
        `(let (,(if (pair? (car bindings)) `(,(caar bindings) ,(cdar bindings))
                    (car bindings)))
           ,(rec (cdr bindings))))))

(defmacro or items
  (if (null? items) #f
      (letrec ((tmp (gensym ""tmp""))
               (rec (lambda (items)
                      (if (null? items) #f
                          `(if (set! ,tmp ,(car items)) ,tmp
                               ,(rec (cdr items)))))))
        `(let (,tmp) ,(rec items)))))

(defmacro do (inits test . body)
  (let ((loop (gensym ""loop"")))
    `(let ,loop ,(map (lambda (init) (list (car init) (cadr init))) inits)
       (if ,(car test)
           ,(if (null? (cdr test)) nil `(begin ,@(cdr test)))
           (begin ,@body
                  (,loop ,@(map (lambda (init)
                                  (if (cddr init) (caddr init) (car init)))
                                inits)))))))

; (1+ obj)
; (1- obj)
(define (1+ o) (+ o 1))
(define (1- o) (- o 1))

; (memq   obj list)
; (memv   obj list)
; (member obj list)

(define (memq obj list)
  (if (null? list) #f
      (if (not (pair? list))
          (error ""2nd arg to memq not a list: "" list)
          (if (eq? obj (car list)) list
              (memq obj (cdr list))))))

(define (memv obj list)
  (if (null? list) #f
      (if (not (pair? list))
          (error ""2nd arg to memv not a list: "" list)
          (if (eqv? obj (car list)) list
              (memv obj (cdr list))))))

(define (member obj list)
  (if (null? list) #f
      (if (not (pair? list))
          (error ""2nd arg to member not a list: "" list)
          (if (equal? obj (car list)) list
              (member obj (cdr list))))))

; (assq  obj alist)
; (assv  obj alist)
; (assoc obj alist)

(define (assq obj alist)
  (if (null? alist) #f
      (if (not (pair? alist))
          (error ""2nd argument to assq not a list: "" alist)
          (if (eq? (caar alist) obj) (car alist)
              (assq obj (cdr alist))))))

(define (assv obj alist)
  (if (null? alist) #f
      (if (not (pair? alist))
          (error ""2nd argument to assv not a list: "" alist)
          (if (eqv? (caar alist) obj) (car alist)
              (assv obj (cdr alist))))))

(define (assoc obj alist)
  (if (null? alist) #f
      (if (not (pair? alist))
          (error ""2nd argument to assoc not a list: "" alist)
          (if (equal? (caar alist) obj) (car alist)
              (assoc obj (cdr alist))))))";
/*
; (list? obj)

(define (list? x)
  (cond ((null? x) #t)
        ((not (pair? x)) #f)
        ((null? (cdr x)) #t)
        ((not (pair? (cdr x))) #f)
        (else (let loop ((fast (cddr x)) (slow (cdr x)))
		(cond ((null? fast) #t)
		      ((or (not (pair? fast)) (eq? fast slow)) #f)
		      ((null? (cdr fast)) #t)
		      (else (loop (cddr fast) (cdr slow))))))))

*/
}

} // namespace NetLisp.Frontend