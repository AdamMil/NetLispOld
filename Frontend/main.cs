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

  const string stdlib = @"
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

; fluid-let (needs dynamic-wind)

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

const string srfi1 = @"
(define (xcons d a) (cons a d))

;;; Make a list of length LEN. Elt i is (PROC i) for 0 <= i < LEN.
(define (list-tabulate len proc)
  (do ((i (- len 1) (- i 1))
       (ans '() (cons (proc i) ans)))
      ((< i 0) ans)))

(define (circular-list val1 . vals)
  (let ((ans (cons val1 vals)))
    (set-cdr! (last-pair ans) ans)
    ans))

;;; <proper-list> ::= ()                        ; Empty proper list
;;;                  |   (cons <x> <proper-list>)        ; Proper-list pair
;;; Note that this definition rules out circular lists -- and this
;;; function is required to detect this case and return false.
(define (proper-list? x) (or (null? x) (list? x)))

;;; A dotted list is a finite list (possibly of length 0) terminated
;;; by a non-nil value. Any non-cons, non-nil value (e.g., ""foo"" or 5)
;;; is a dotted list of length 0.
;;;
;;; <dotted-list> ::= <non-nil,non-pair>        ; Empty dotted list
;;;               |   (cons <x> <dotted-list>)        ; Proper-list pair
(define (dotted-list? x)
  (let lp ((x x) (lag x))
    (if (pair? x)
        (let ((x (cdr x)))
          (if (pair? x)
              (let ((x   (cdr x))
                    (lag (cdr lag)))
                (and (not (eq? x lag)) (lp x lag)))
              (not (null? x))))
        (not (null? x)))))

(define (circular-list? x)
  (let lp ((x x) (lag x))
    (and (pair? x)
         (let ((x (cdr x)))
           (and (pair? x)
                (let ((x   (cdr x))
                      (lag (cdr lag)))
                  (or (eq? x lag) (lp x lag))))))))

(define (not-pair? x) (not (pair? x)))        ; Inline me.

;;; This is a legal definition which is fast and sloppy:
;;;     (define null-list? not-pair?)
;;; but we'll provide a more careful one:
(define (null-list? l)
  (cond ((pair? l) #f)
        ((null? l) #t)
        (else (error ""null-list?: argument out of domain"" l))))
           
(define (list= = . lists)
  (or (null? lists) ; special case

      (let lp1 ((list-a (car lists)) (others (cdr lists)))
        (or (null? others)
            (let ((list-b (car others))
                  (others (cdr others)))
              (if (eq? list-a list-b)        ; EQ? => LIST=
                  (lp1 list-b others)
                  (let lp2 ((list-a list-a) (list-b list-b))
                    (if (null-list? list-a)
                        (and (null-list? list-b)
                             (lp1 list-b others))
                        (and (not (null-list? list-b))
                             (= (car list-a) (car list-b))
                             (lp2 (cdr list-a) (cdr list-b)))))))))))
                        


(define (length+ x)                        ; Returns #f if X is circular.
  (let lp ((x x) (lag x) (len 0))
    (if (pair? x)
        (let ((x (cdr x))
              (len (+ len 1)))
          (if (pair? x)
              (let ((x   (cdr x))
                    (lag (cdr lag))
                    (len (+ len 1)))
                (and (not (eq? x lag)) (lp x lag len)))
              len))
        len)))

(define (zip list1 . more-lists) (apply map list list1 more-lists))

;;; Selectors
;;;;;;;;;;;;;

(define first  car)
(define second cadr)
(define third  caddr)
(define fourth cadddr)
(define (fifth   x) (car    (cddddr x)))
(define (sixth   x) (cadr   (cddddr x)))
(define (seventh x) (caddr  (cddddr x)))
(define (eighth  x) (cadddr (cddddr x)))
(define (ninth   x) (car  (cddddr (cddddr x))))
(define (tenth   x) (cadr (cddddr (cddddr x))))

(define (car+cdr pair) (values (car pair) (cdr pair)))

;;; take & drop

(define take list-head)
(define drop list-tail)

(define (take! lis k)
  (check-arg integer? k take!)
  (if (zero? k) '()
      (begin (set-cdr! (drop lis (- k 1)) '())
             lis)))

;;; TAKE-RIGHT and DROP-RIGHT work by getting two pointers into the list, 
;;; off by K, then chasing down the list until the lead pointer falls off
;;; the end.

(define (take-right lis k)
  (check-arg integer? k take-right)
  (let lp ((lag lis)  (lead (drop lis k)))
    (if (pair? lead)
        (lp (cdr lag) (cdr lead))
        lag)))

(define (drop-right lis k)
  (check-arg integer? k drop-right)
  (let recur ((lag lis) (lead (drop lis k)))
    (if (pair? lead)
        (cons (car lag) (recur (cdr lag) (cdr lead)))
        '())))

;;; In this function, LEAD is actually K+1 ahead of LAG. This lets
;;; us stop LAG one step early, in time to smash its cdr to ().
(define (drop-right! lis k)
  (check-arg integer? k drop-right!)
  (let ((lead (drop lis k)))
    (if (pair? lead)

        (let lp ((lag lis)  (lead (cdr lead)))        ; Standard case
          (if (pair? lead)
              (lp (cdr lag) (cdr lead))
              (begin (set-cdr! lag '())
                     lis)))

        '())))        ; Special case dropping everything -- no cons to side-effect.

(define (split-at x k)
  (check-arg integer? k split-at)
  (let recur ((lis x) (k k))
    (if (zero? k) (values '() lis)
        (receive (prefix suffix) (recur (cdr lis) (- k 1))
          (values (cons (car lis) prefix) suffix)))))

(define (split-at! x k)
  (check-arg integer? k split-at!)
  (if (zero? k) (values '() x)
      (let* ((prev (drop x (- k 1)))
             (suffix (cdr prev)))
        (set-cdr! prev '())
        (values x suffix))))


(define (last lis) (car (last-pair lis)))

;;; Unzippers -- 1 through 5
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

(define (unzip1 lis) (map car lis))

(define (unzip2 lis)
  (let recur ((lis lis))
    (if (null-list? lis) (values lis lis)        ; Use NOT-PAIR? to handle
        (let ((elt (car lis)))                        ; dotted lists.
          (receive (a b) (recur (cdr lis))
            (values (cons (car  elt) a)
                    (cons (cadr elt) b)))))))

(define (unzip3 lis)
  (let recur ((lis lis))
    (if (null-list? lis) (values lis lis lis)
        (let ((elt (car lis)))
          (receive (a b c) (recur (cdr lis))
            (values (cons (car   elt) a)
                    (cons (cadr  elt) b)
                    (cons (caddr elt) c)))))))

(define (unzip4 lis)
  (let recur ((lis lis))
    (if (null-list? lis) (values lis lis lis lis)
        (let ((elt (car lis)))
          (receive (a b c d) (recur (cdr lis))
            (values (cons (car    elt) a)
                    (cons (cadr   elt) b)
                    (cons (caddr  elt) c)
                    (cons (cadddr elt) d)))))))

(define (unzip5 lis)
  (let recur ((lis lis))
    (if (null-list? lis) (values lis lis lis lis lis)
        (let ((elt (car lis)))
          (receive (a b c d e) (recur (cdr lis))
            (values (cons (car     elt) a)
                    (cons (cadr    elt) b)
                    (cons (caddr   elt) c)
                    (cons (cadddr  elt) d)
                    (cons (car (cddddr  elt)) e)))))))

;;; append! append-reverse append-reverse! concatenate concatenate!
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

;;; Hand-inline the FOLD and PAIR-FOLD ops for speed.
(define (append-reverse rev-head tail)
  (let lp ((rev-head rev-head) (tail tail))
    (if (null-list? rev-head) tail
        (lp (cdr rev-head) (cons (car rev-head) tail)))))

(define (append-reverse! rev-head tail)
  (let lp ((rev-head rev-head) (tail tail))
    (if (null-list? rev-head) tail
        (let ((next-rev (cdr rev-head)))
          (set-cdr! rev-head tail)
          (lp next-rev rev-head)))))

(define (concatenate  lists) (reduce-right append  '() lists))
(define (concatenate! lists) (reduce-right append! '() lists))

;;; Fold/map internal utilities
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;; These little internal utilities are used by the general
;;; fold & mapper funs for the n-ary cases . It'd be nice if they got inlined.
;;; One the other hand, the n-ary cases are painfully inefficient as it is.
;;; An aggressive implementation should simply re-write these functions 
;;; for raw efficiency; I have written them for as much clarity, portability,
;;; and simplicity as can be achieved.
;;;
;;; I use the dreaded call/cc to do local aborts. A good compiler could
;;; handle this with extreme efficiency. An implementation that provides
;;; a one-shot, non-persistent continuation grabber could help the compiler
;;; out by using that in place of the call/cc's in these routines.
;;;
;;; These functions have funky definitions that are precisely tuned to
;;; the needs of the fold/map procs -- for example, to minimize the number
;;; of times the argument lists need to be examined.

;;; Return (map cdr lists). 
;;; However, if any element of LISTS is empty, just abort and return '().
(define (%cdrs lists)
  (call-with-current-continuation
    (lambda (abort)
      (let recur ((lists lists))
        (if (pair? lists)
            (let ((lis (car lists)))
              (if (null-list? lis) (abort '())
                  (cons (cdr lis) (recur (cdr lists)))))
            '())))))

(define (%cars+ lists last-elt)        ; (append! (map car lists) (list last-elt))
  (let recur ((lists lists))
    (if (pair? lists) (cons (caar lists) (recur (cdr lists))) (list last-elt))))

;;; LISTS is a (not very long) non-empty list of lists.
;;; Return two lists: the cars & the cdrs of the lists.
;;; However, if any of the lists is empty, just abort and return [() ()].

(define (%cars+cdrs lists)
  (call-with-current-continuation
    (lambda (abort)
      (let recur ((lists lists))
        (if (pair? lists)
            (receive (list other-lists) (car+cdr lists)
              (if (null-list? list) (abort '() '()) ; LIST is empty -- bail out
                  (receive (a d) (car+cdr list)
                    (receive (cars cdrs) (recur other-lists)
                      (values (cons a cars) (cons d cdrs))))))
            (values '() '()))))))

;;; Like %CARS+CDRS, but we pass in a final elt tacked onto the end of the
;;; cars list. What a hack.
(define (%cars+cdrs+ lists cars-final)
  (call-with-current-continuation
    (lambda (abort)
      (let recur ((lists lists))
        (if (pair? lists)
            (receive (list other-lists) (car+cdr lists)
              (if (null-list? list) (abort '() '()) ; LIST is empty -- bail out
                  (receive (a d) (car+cdr list)
                    (receive (cars cdrs) (recur other-lists)
                      (values (cons a cars) (cons d cdrs))))))
            (values (list cars-final) '()))))))

;;; Like %CARS+CDRS, but blow up if any list is empty.
(define (%cars+cdrs/no-test lists)
  (let recur ((lists lists))
    (if (pair? lists)
        (receive (list other-lists) (car+cdr lists)
          (receive (a d) (car+cdr list)
            (receive (cars cdrs) (recur other-lists)
              (values (cons a cars) (cons d cdrs)))))
        (values '() '()))))


;;; count
;;;;;;;;;
(define (count pred list1 . lists)
  (check-arg procedure? pred count)
  (if (pair? lists)

      ;; N-ary case
      (let lp ((list1 list1) (lists lists) (i 0))
        (if (null-list? list1) i
            (receive (as ds) (%cars+cdrs lists)
              (if (null? as) i
                  (lp (cdr list1) ds
                      (if (apply pred (car list1) as) (+ i 1) i))))))

      ;; Fast path
      (let lp ((lis list1) (i 0))
        (if (null-list? lis) i
            (lp (cdr lis) (if (pred (car lis)) (+ i 1) i))))))


;;; fold/unfold
;;;;;;;;;;;;;;;

(define (unfold-right p f g seed . maybe-tail)
  (check-arg procedure? p unfold-right)
  (check-arg procedure? f unfold-right)
  (check-arg procedure? g unfold-right)
  (let lp ((seed seed) (ans (:optional maybe-tail '())))
    (if (p seed) ans
        (lp (g seed)
            (cons (f seed) ans)))))


(define (unfold p f g seed . maybe-tail-gen)
  (check-arg procedure? p unfold)
  (check-arg procedure? f unfold)
  (check-arg procedure? g unfold)
  (if (pair? maybe-tail-gen)

      (let ((tail-gen (car maybe-tail-gen)))
        (if (pair? (cdr maybe-tail-gen))
            (apply error ""Too many arguments"" unfold p f g seed maybe-tail-gen)

            (let recur ((seed seed))
              (if (p seed) (tail-gen seed)
                  (cons (f seed) (recur (g seed)))))))

      (let recur ((seed seed))
        (if (p seed) '()
            (cons (f seed) (recur (g seed)))))))
      

(define (fold kons knil lis1 . lists)
  (check-arg procedure? kons fold)
  (if (pair? lists)
      (let lp ((lists (cons lis1 lists)) (ans knil))        ; N-ary case
        (receive (cars+ans cdrs) (%cars+cdrs+ lists ans)
          (if (null? cars+ans) ans ; Done.
              (lp cdrs (apply kons cars+ans)))))
            
      (let lp ((lis lis1) (ans knil))                        ; Fast path
        (if (null-list? lis) ans
            (lp (cdr lis) (kons (car lis) ans))))))


(define (fold-right kons knil lis1 . lists)
  (check-arg procedure? kons fold-right)
  (if (pair? lists)
      (let recur ((lists (cons lis1 lists)))                ; N-ary case
        (let ((cdrs (%cdrs lists)))
          (if (null? cdrs) knil
              (apply kons (%cars+ lists (recur cdrs))))))

      (let recur ((lis lis1))                                ; Fast path
        (if (null-list? lis) knil
            (let ((head (car lis)))
              (kons head (recur (cdr lis))))))))


(define (pair-fold-right f zero lis1 . lists)
  (check-arg procedure? f pair-fold-right)
  (if (pair? lists)
      (let recur ((lists (cons lis1 lists)))                ; N-ary case
        (let ((cdrs (%cdrs lists)))
          (if (null? cdrs) zero
              (apply f (append! lists (list (recur cdrs)))))))

      (let recur ((lis lis1))                                ; Fast path
        (if (null-list? lis) zero (f lis (recur (cdr lis)))))))

(define (pair-fold f zero lis1 . lists)
  (check-arg procedure? f pair-fold)
  (if (pair? lists)
      (let lp ((lists (cons lis1 lists)) (ans zero))        ; N-ary case
        (let ((tails (%cdrs lists)))
          (if (null? tails) ans
              (lp tails (apply f (append! lists (list ans)))))))

      (let lp ((lis lis1) (ans zero))
        (if (null-list? lis) ans
            (let ((tail (cdr lis)))                ; Grab the cdr now,
              (lp tail (f lis ans)))))))        ; in case F SET-CDR!s LIS.
      

;;; REDUCE and REDUCE-RIGHT only use RIDENTITY in the empty-list case.
;;; These cannot meaningfully be n-ary.

(define (reduce f ridentity lis)
  (check-arg procedure? f reduce)
  (if (null-list? lis) ridentity
      (fold f (car lis) (cdr lis))))

(define (reduce-right f ridentity lis)
  (check-arg procedure? f reduce-right)
  (if (null-list? lis) ridentity
      (let recur ((head (car lis)) (lis (cdr lis)))
        (if (pair? lis)
            (f head (recur (car lis) (cdr lis)))
            head))))



;;; Mappers: append-map append-map! pair-for-each map! filter-map map-in-order
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

(define (append-map f lis1 . lists)
  (really-append-map append-map  append  f lis1 lists))
(define (append-map! f lis1 . lists) 
  (really-append-map append-map! append! f lis1 lists))

(define (really-append-map who appender f lis1 lists)
  (check-arg procedure? f who)
  (if (pair? lists)
      (receive (cars cdrs) (%cars+cdrs (cons lis1 lists))
        (if (null? cars) '()
            (let recur ((cars cars) (cdrs cdrs))
              (let ((vals (apply f cars)))
                (receive (cars2 cdrs2) (%cars+cdrs cdrs)
                  (if (null? cars2) vals
                      (appender vals (recur cars2 cdrs2))))))))

      ;; Fast path
      (if (null-list? lis1) '()
          (let recur ((elt (car lis1)) (rest (cdr lis1)))
            (let ((vals (f elt)))
              (if (null-list? rest) vals
                  (appender vals (recur (car rest) (cdr rest)))))))))


(define (pair-for-each proc lis1 . lists)
  (check-arg procedure? proc pair-for-each)
  (if (pair? lists)

      (let lp ((lists (cons lis1 lists)))
        (let ((tails (%cdrs lists)))
          (if (pair? tails)
              (begin (apply proc lists)
                     (lp tails)))))

      ;; Fast path.
      (let lp ((lis lis1))
        (if (not (null-list? lis))
            (let ((tail (cdr lis)))        ; Grab the cdr now,
              (proc lis)                ; in case PROC SET-CDR!s LIS.
              (lp tail))))))

;;; We stop when LIS1 runs out, not when any list runs out.
(define (map! f lis1 . lists)
  (check-arg procedure? f map!)
  (if (pair? lists)
      (let lp ((lis1 lis1) (lists lists))
        (if (not (null-list? lis1))
            (receive (heads tails) (%cars+cdrs/no-test lists)
              (set-car! lis1 (apply f (car lis1) heads))
              (lp (cdr lis1) tails))))

      ;; Fast path.
      (pair-for-each (lambda (pair) (set-car! pair (f (car pair)))) lis1))
  lis1)


;;; Map F across L, and save up all the non-false results.
(define (filter-map f lis1 . lists)
  (check-arg procedure? f filter-map)
  (if (pair? lists)
      (let recur ((lists (cons lis1 lists)))
        (receive (cars cdrs) (%cars+cdrs lists)
          (if (pair? cars)
              (cond ((apply f cars) => (lambda (x) (cons x (recur cdrs))))
                    (else (recur cdrs))) ; Tail call in this arm.
              '())))
            
      ;; Fast path.
      (let recur ((lis lis1))
        (if (null-list? lis) lis
            (let ((tail (recur (cdr lis))))
              (cond ((f (car lis)) => (lambda (x) (cons x tail)))
                    (else tail)))))))


;;; Map F across lists, guaranteeing to go left-to-right.
;;; NOTE: Some implementations of R5RS MAP are compliant with this spec;
;;; in which case this procedure may simply be defined as a synonym for MAP.

(define (map-in-order f lis1 . lists)
  (check-arg procedure? f map-in-order)
  (if (pair? lists)
      (let recur ((lists (cons lis1 lists)))
        (receive (cars cdrs) (%cars+cdrs lists)
          (if (pair? cars)
              (let ((x (apply f cars)))                ; Do head first,
                (cons x (recur cdrs)))                ; then tail.
              '())))
            
      ;; Fast path.
      (let recur ((lis lis1))
        (if (null-list? lis) lis
            (let ((tail (cdr lis))
                  (x (f (car lis))))                ; Do head first,
              (cons x (recur tail)))))))        ; then tail.


;;; We extend MAP to handle arguments of unequal length.
(define map map-in-order)        


;;; filter, remove, partition
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;; FILTER, REMOVE, PARTITION and their destructive counterparts do not
;;; disorder the elements of their argument.

;; This FILTER shares the longest tail of L that has no deleted elements.
;; If Scheme had multi-continuation calls, they could be made more efficient.

(define (filter pred lis)                        ; Sleazing with EQ? makes this
  (check-arg procedure? pred filter)                ; one faster.
  (let recur ((lis lis))                
    (if (null-list? lis) lis                        ; Use NOT-PAIR? to handle dotted lists.
        (let ((head (car lis))
              (tail (cdr lis)))
          (if (pred head)
              (let ((new-tail (recur tail)))        ; Replicate the RECUR call so
                (if (eq? tail new-tail) lis
                    (cons head new-tail)))
              (recur tail))))))                        ; this one can be a tail call.


;;; Another version that shares longest tail.
;(define (filter pred lis)
;  (receive (ans no-del?)
;      ;; (recur l) returns L with (pred x) values filtered.
;      ;; It also returns a flag NO-DEL? if the returned value
;      ;; is EQ? to L, i.e. if it didn't have to delete anything.
;      (let recur ((l l))
;        (if (null-list? l) (values l #t)
;            (let ((x  (car l))
;                  (tl (cdr l)))
;              (if (pred x)
;                  (receive (ans no-del?) (recur tl)
;                    (if no-del?
;                        (values l #t)
;                        (values (cons x ans) #f)))
;                  (receive (ans no-del?) (recur tl) ; Delete X.
;                    (values ans #f))))))
;    ans))



;(define (filter! pred lis)                        ; Things are much simpler
;  (let recur ((lis lis))                        ; if you are willing to
;    (if (pair? lis)                                ; push N stack frames & do N
;        (cond ((pred (car lis))                ; SET-CDR! writes, where N is
;               (set-cdr! lis (recur (cdr lis))); the length of the answer.
;               lis)                                
;              (else (recur (cdr lis))))
;        lis)))


;;; This implementation of FILTER!
;;; - doesn't cons, and uses no stack;
;;; - is careful not to do redundant SET-CDR! writes, as writes to memory are 
;;;   usually expensive on modern machines, and can be extremely expensive on 
;;;   modern Schemes (e.g., ones that have generational GC's).
;;; It just zips down contiguous runs of in and out elts in LIS doing the 
;;; minimal number of SET-CDR!s to splice the tail of one run of ins to the 
;;; beginning of the next.

(define (filter! pred lis)
  (check-arg procedure? pred filter!)
  (let lp ((ans lis))
    (cond ((null-list? ans)       ans)                        ; Scan looking for
          ((not (pred (car ans))) (lp (cdr ans)))        ; first cons of result.

          ;; ANS is the eventual answer.
          ;; SCAN-IN: (CDR PREV) = LIS and (CAR PREV) satisfies PRED.
          ;;          Scan over a contiguous segment of the list that
          ;;          satisfies PRED.
          ;; SCAN-OUT: (CAR PREV) satisfies PRED. Scan over a contiguous
          ;;           segment of the list that *doesn't* satisfy PRED.
          ;;           When the segment ends, patch in a link from PREV
          ;;           to the start of the next good segment, and jump to
          ;;           SCAN-IN.
          (else (letrec ((scan-in (lambda (prev lis)
                                    (if (pair? lis)
                                        (if (pred (car lis))
                                            (scan-in lis (cdr lis))
                                            (scan-out prev (cdr lis))))))
                         (scan-out (lambda (prev lis)
                                     (let lp ((lis lis))
                                       (if (pair? lis)
                                           (if (pred (car lis))
                                               (begin (set-cdr! prev lis)
                                                      (scan-in lis (cdr lis)))
                                               (lp (cdr lis)))
                                           (set-cdr! prev lis))))))
                  (scan-in ans (cdr ans))
                  ans)))))



;;; Answers share common tail with LIS where possible; 
;;; the technique is slightly subtle.

(define (partition pred lis)
  (check-arg procedure? pred partition)
  (let recur ((lis lis))
    (if (null-list? lis) (values lis lis)        ; Use NOT-PAIR? to handle dotted lists.
        (let ((elt (car lis))
              (tail (cdr lis)))
          (receive (in out) (recur tail)
            (if (pred elt)
                (values (if (pair? out) (cons elt in) lis) out)
                (values in (if (pair? in) (cons elt out) lis))))))))



;(define (partition! pred lis)                        ; Things are much simpler
;  (let recur ((lis lis))                        ; if you are willing to
;    (if (null-list? lis) (values lis lis)        ; push N stack frames & do N
;        (let ((elt (car lis)))                        ; SET-CDR! writes, where N is
;          (receive (in out) (recur (cdr lis))        ; the length of LIS.
;            (cond ((pred elt)
;                   (set-cdr! lis in)
;                   (values lis out))
;                  (else (set-cdr! lis out)
;                        (values in lis))))))))


;;; This implementation of PARTITION!
;;; - doesn't cons, and uses no stack;
;;; - is careful not to do redundant SET-CDR! writes, as writes to memory are
;;;   usually expensive on modern machines, and can be extremely expensive on 
;;;   modern Schemes (e.g., ones that have generational GC's).
;;; It just zips down contiguous runs of in and out elts in LIS doing the
;;; minimal number of SET-CDR!s to splice these runs together into the result 
;;; lists.

(define (partition! pred lis)
  (check-arg procedure? pred partition!)
  (if (null-list? lis) (values lis lis)

      ;; This pair of loops zips down contiguous in & out runs of the
      ;; list, splicing the runs together. The invariants are
      ;;   SCAN-IN:  (cdr in-prev)  = LIS.
      ;;   SCAN-OUT: (cdr out-prev) = LIS.
      (letrec ((scan-in (lambda (in-prev out-prev lis)
                          (let lp ((in-prev in-prev) (lis lis))
                            (if (pair? lis)
                                (if (pred (car lis))
                                    (lp lis (cdr lis))
                                    (begin (set-cdr! out-prev lis)
                                           (scan-out in-prev lis (cdr lis))))
                                (set-cdr! out-prev lis))))) ; Done.

               (scan-out (lambda (in-prev out-prev lis)
                           (let lp ((out-prev out-prev) (lis lis))
                             (if (pair? lis)
                                 (if (pred (car lis))
                                     (begin (set-cdr! in-prev lis)
                                            (scan-in lis out-prev (cdr lis)))
                                     (lp lis (cdr lis)))
                                 (set-cdr! in-prev lis)))))) ; Done.

        ;; Crank up the scan&splice loops.
        (if (pred (car lis))
            ;; LIS begins in-list. Search for out-list's first pair.
            (let lp ((prev-l lis) (l (cdr lis)))
              (cond ((not (pair? l)) (values lis l))
                    ((pred (car l)) (lp l (cdr l)))
                    (else (scan-out prev-l l (cdr l))
                          (values lis l))))        ; Done.

            ;; LIS begins out-list. Search for in-list's first pair.
            (let lp ((prev-l lis) (l (cdr lis)))
              (cond ((not (pair? l)) (values l lis))
                    ((pred (car l))
                     (scan-in l prev-l (cdr l))
                     (values l lis))                ; Done.
                    (else (lp l (cdr l)))))))))


;;; Inline us, please.
(define (remove  pred l) (filter  (lambda (x) (not (pred x))) l))
(define (remove! pred l) (filter! (lambda (x) (not (pred x))) l))



;;; Here's the taxonomy for the DELETE/ASSOC/MEMBER functions.
;;; (I don't actually think these are the world's most important
;;; functions -- the procedural FILTER/REMOVE/FIND/FIND-TAIL variants
;;; are far more general.)
;;;
;;; Function                        Action
;;; ---------------------------------------------------------------------------
;;; remove pred lis                Delete by general predicate
;;; delete x lis [=]                Delete by element comparison
;;;                                             
;;; find pred lis                Search by general predicate
;;; find-tail pred lis                Search by general predicate
;;; member x lis [=]                Search by element comparison
;;;
;;; assoc key lis [=]                Search alist by key comparison
;;; alist-delete key alist [=]        Alist-delete by key comparison

(define (delete x lis . maybe-=) 
  (let ((= (:optional maybe-= equal?)))
    (filter (lambda (y) (not (= x y))) lis)))

(define (delete! x lis . maybe-=)
  (let ((= (:optional maybe-= equal?)))
    (filter! (lambda (y) (not (= x y))) lis)))

;;; Extended from R4RS to take an optional comparison argument.
(define (member x lis . maybe-=)
  (let ((= (:optional maybe-= equal?)))
    (find-tail (lambda (y) (= x y)) lis)))

;;; R4RS, hence we don't bother to define.
;;; The MEMBER and then FIND-TAIL call should definitely
;;; be inlined for MEMQ & MEMV.
;(define (memq    x lis) (member x lis eq?))
;(define (memv    x lis) (member x lis eqv?))


;;; right-duplicate deletion
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;
;;; delete-duplicates delete-duplicates!
;;;
;;; Beware -- these are N^2 algorithms. To efficiently remove duplicates
;;; in long lists, sort the list to bring duplicates together, then use a 
;;; linear-time algorithm to kill the dups. Or use an algorithm based on
;;; element-marking. The former gives you O(n lg n), the latter is linear.

(define (delete-duplicates lis . maybe-=)
  (let ((elt= (:optional maybe-= equal?)))
    (check-arg procedure? elt= delete-duplicates)
    (let recur ((lis lis))
      (if (null-list? lis) lis
          (let* ((x (car lis))
                 (tail (cdr lis))
                 (new-tail (recur (delete x tail elt=))))
            (if (eq? tail new-tail) lis (cons x new-tail)))))))

(define (delete-duplicates! lis maybe-=)
  (let ((elt= (:optional maybe-= equal?)))
    (check-arg procedure? elt= delete-duplicates!)
    (let recur ((lis lis))
      (if (null-list? lis) lis
          (let* ((x (car lis))
                 (tail (cdr lis))
                 (new-tail (recur (delete! x tail elt=))))
            (if (eq? tail new-tail) lis (cons x new-tail)))))))


;;; alist stuff
;;;;;;;;;;;;;;;

;;; Extended from R4RS to take an optional comparison argument.
(define (assoc x lis . maybe-=)
  (let ((= (:optional maybe-= equal?)))
    (find (lambda (entry) (= x (car entry))) lis)))

(define (alist-cons key datum alist) (cons (cons key datum) alist))

(define (alist-copy alist)
  (map (lambda (elt) (cons (car elt) (cdr elt)))
       alist))

(define (alist-delete key alist . maybe-=)
  (let ((= (:optional maybe-= equal?)))
    (filter (lambda (elt) (not (= key (car elt)))) alist)))

(define (alist-delete! key alist . maybe-=)
  (let ((= (:optional maybe-= equal?)))
    (filter! (lambda (elt) (not (= key (car elt)))) alist)))


;;; find find-tail take-while drop-while span break any every list-index
;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;

(define (find pred list)
  (cond ((find-tail pred list) => car)
        (else #f)))

(define (find-tail pred list)
  (check-arg procedure? pred find-tail)
  (let lp ((list list))
    (and (not (null-list? list))
         (if (pred (car list)) list
             (lp (cdr list))))))

(define (take-while pred lis)
  (check-arg procedure? pred take-while)
  (let recur ((lis lis))
    (if (null-list? lis) '()
        (let ((x (car lis)))
          (if (pred x)
              (cons x (recur (cdr lis)))
              '())))))

(define (drop-while pred lis)
  (check-arg procedure? pred drop-while)
  (let lp ((lis lis))
    (if (null-list? lis) '()
        (if (pred (car lis))
            (lp (cdr lis))
            lis))))

(define (take-while! pred lis)
  (check-arg procedure? pred take-while!)
  (if (or (null-list? lis) (not (pred (car lis)))) '()
      (begin (let lp ((prev lis) (rest (cdr lis)))
               (if (pair? rest)
                   (let ((x (car rest)))
                     (if (pred x) (lp rest (cdr rest))
                         (set-cdr! prev '())))))
             lis)))

(define (span pred lis)
  (check-arg procedure? pred span)
  (let recur ((lis lis))
    (if (null-list? lis) (values '() '())
        (let ((x (car lis)))
          (if (pred x)
              (receive (prefix suffix) (recur (cdr lis))
                (values (cons x prefix) suffix))
              (values '() lis))))))

(define (span! pred lis)
  (check-arg procedure? pred span!)
  (if (or (null-list? lis) (not (pred (car lis)))) (values '() lis)
      (let ((suffix (let lp ((prev lis) (rest (cdr lis)))
                      (if (null-list? rest) rest
                          (let ((x (car rest)))
                            (if (pred x) (lp rest (cdr rest))
                                (begin (set-cdr! prev '())
                                       rest)))))))
        (values lis suffix))))
  

(define (break  pred lis) (span  (lambda (x) (not (pred x))) lis))
(define (break! pred lis) (span! (lambda (x) (not (pred x))) lis))

(define (any pred lis1 . lists)
  (check-arg procedure? pred any)
  (if (pair? lists)

      ;; N-ary case
      (receive (heads tails) (%cars+cdrs (cons lis1 lists))
        (and (pair? heads)
             (let lp ((heads heads) (tails tails))
               (receive (next-heads next-tails) (%cars+cdrs tails)
                 (if (pair? next-heads)
                     (or (apply pred heads) (lp next-heads next-tails))
                     (apply pred heads)))))) ; Last PRED app is tail call.

      ;; Fast path
      (and (not (null-list? lis1))
           (let lp ((head (car lis1)) (tail (cdr lis1)))
             (if (null-list? tail)
                 (pred head)                ; Last PRED app is tail call.
                 (or (pred head) (lp (car tail) (cdr tail))))))))


;(define (every pred list)              ; Simple definition.
;  (let lp ((list list))                ; Doesn't return the last PRED value.
;    (or (not (pair? list))
;        (and (pred (car list))
;             (lp (cdr list))))))

(define (every pred lis1 . lists)
  (check-arg procedure? pred every)
  (if (pair? lists)

      ;; N-ary case
      (receive (heads tails) (%cars+cdrs (cons lis1 lists))
        (or (not (pair? heads))
            (let lp ((heads heads) (tails tails))
              (receive (next-heads next-tails) (%cars+cdrs tails)
                (if (pair? next-heads)
                    (and (apply pred heads) (lp next-heads next-tails))
                    (apply pred heads)))))) ; Last PRED app is tail call.

      ;; Fast path
      (or (null-list? lis1)
          (let lp ((head (car lis1))  (tail (cdr lis1)))
            (if (null-list? tail)
                (pred head)        ; Last PRED app is tail call.
                (and (pred head) (lp (car tail) (cdr tail))))))))

(define (list-index pred lis1 . lists)
  (check-arg procedure? pred list-index)
  (if (pair? lists)

      ;; N-ary case
      (let lp ((lists (cons lis1 lists)) (n 0))
        (receive (heads tails) (%cars+cdrs lists)
          (and (pair? heads)
               (if (apply pred heads) n
                   (lp tails (+ n 1))))))

      ;; Fast path
      (let lp ((lis lis1) (n 0))
        (and (not (null-list? lis))
             (if (pred (car lis)) n (lp (cdr lis) (+ n 1)))))))

;;; Reverse
;;;;;;;;;;;

;R4RS, so not defined here.
;(define (reverse lis) (fold cons '() lis))
                                      
;(define (reverse! lis)
;  (pair-fold (lambda (pair tail) (set-cdr! pair tail) pair) '() lis))

(define (reverse! lis)
  (let lp ((lis lis) (ans '()))
    (if (null-list? lis) ans
        (let ((tail (cdr lis)))
          (set-cdr! lis ans)
          (lp tail lis)))))

;;; Lists-as-sets
;;;;;;;;;;;;;;;;;

;;; This is carefully tuned code; do not modify casually.
;;; - It is careful to share storage when possible;
;;; - Side-effecting code tries not to perform redundant writes.
;;; - It tries to avoid linear-time scans in special cases where constant-time
;;;   computations can be performed.
;;; - It relies on similar properties from the other list-lib procs it calls.
;;;   For example, it uses the fact that the implementations of MEMBER and
;;;   FILTER in this source code share longest common tails between args
;;;   and results to get structure sharing in the lset procedures.

(define (%lset2<= = lis1 lis2) (every (lambda (x) (member x lis2 =)) lis1))

(define (lset<= = . lists)
  (check-arg procedure? = lset<=)
  (or (not (pair? lists)) ; 0-ary case
      (let lp ((s1 (car lists)) (rest (cdr lists)))
        (or (not (pair? rest))
            (let ((s2 (car rest))  (rest (cdr rest)))
              (and (or (eq? s2 s1)        ; Fast path
                       (%lset2<= = s1 s2)) ; Real test
                   (lp s2 rest)))))))

(define (lset= = . lists)
  (check-arg procedure? = lset=)
  (or (not (pair? lists)) ; 0-ary case
      (let lp ((s1 (car lists)) (rest (cdr lists)))
        (or (not (pair? rest))
            (let ((s2   (car rest))
                  (rest (cdr rest)))
              (and (or (eq? s1 s2)        ; Fast path
                       (and (%lset2<= = s1 s2) (%lset2<= = s2 s1))) ; Real test
                   (lp s2 rest)))))))


(define (lset-adjoin = lis . elts)
  (check-arg procedure? = lset-adjoin)
  (fold (lambda (elt ans) (if (member elt ans =) ans (cons elt ans)))
        lis elts))


(define (lset-union = . lists)
  (check-arg procedure? = lset-union)
  (reduce (lambda (lis ans)                ; Compute ANS + LIS.
            (cond ((null? lis) ans)        ; Don't copy any lists
                  ((null? ans) lis)         ; if we don't have to.
                  ((eq? lis ans) ans)
                  (else
                   (fold (lambda (elt ans) (if (any (lambda (x) (= x elt)) ans)
                                               ans
                                               (cons elt ans)))
                         ans lis))))
          '() lists))

(define (lset-union! = . lists)
  (check-arg procedure? = lset-union!)
  (reduce (lambda (lis ans)                ; Splice new elts of LIS onto the front of ANS.
            (cond ((null? lis) ans)        ; Don't copy any lists
                  ((null? ans) lis)         ; if we don't have to.
                  ((eq? lis ans) ans)
                  (else
                   (pair-fold (lambda (pair ans)
                                (let ((elt (car pair)))
                                  (if (any (lambda (x) (= x elt)) ans)
                                      ans
                                      (begin (set-cdr! pair ans) pair))))
                              ans lis))))
          '() lists))


(define (lset-intersection = lis1 . lists)
  (check-arg procedure? = lset-intersection)
  (let ((lists (delete lis1 lists eq?))) ; Throw out any LIS1 vals.
    (cond ((any null-list? lists) '())                ; Short cut
          ((null? lists)          lis1)                ; Short cut
          (else (filter (lambda (x)
                          (every (lambda (lis) (member x lis =)) lists))
                        lis1)))))

(define (lset-intersection! = lis1 . lists)
  (check-arg procedure? = lset-intersection!)
  (let ((lists (delete lis1 lists eq?))) ; Throw out any LIS1 vals.
    (cond ((any null-list? lists) '())                ; Short cut
          ((null? lists)          lis1)                ; Short cut
          (else (filter! (lambda (x)
                           (every (lambda (lis) (member x lis =)) lists))
                         lis1)))))


(define (lset-difference = lis1 . lists)
  (check-arg procedure? = lset-difference)
  (let ((lists (filter pair? lists)))        ; Throw out empty lists.
    (cond ((null? lists)     lis1)        ; Short cut
          ((memq lis1 lists) '())        ; Short cut
          (else (filter (lambda (x)
                          (every (lambda (lis) (not (member x lis =)))
                                 lists))
                        lis1)))))

(define (lset-difference! = lis1 . lists)
  (check-arg procedure? = lset-difference!)
  (let ((lists (filter pair? lists)))        ; Throw out empty lists.
    (cond ((null? lists)     lis1)        ; Short cut
          ((memq lis1 lists) '())        ; Short cut
          (else (filter! (lambda (x)
                           (every (lambda (lis) (not (member x lis =)))
                                  lists))
                         lis1)))))


(define (lset-xor = . lists)
  (check-arg procedure? = lset-xor)
  (reduce (lambda (b a)                        ; Compute A xor B:
            ;; Note that this code relies on the constant-time
            ;; short-cuts provided by LSET-DIFF+INTERSECTION,
            ;; LSET-DIFFERENCE & APPEND to provide constant-time short
            ;; cuts for the cases A = (), B = (), and A eq? B. It takes
            ;; a careful case analysis to see it, but it's carefully
            ;; built in.

            ;; Compute a-b and a^b, then compute b-(a^b) and
            ;; cons it onto the front of a-b.
            (receive (a-b a-int-b)   (lset-diff+intersection = a b)
              (cond ((null? a-b)     (lset-difference b a =))
                    ((null? a-int-b) (append b a))
                    (else (fold (lambda (xb ans)
                                  (if (member xb a-int-b =) ans (cons xb ans)))
                                a-b
                                b)))))
          '() lists))


(define (lset-xor! = . lists)
  (check-arg procedure? = lset-xor!)
  (reduce (lambda (b a)                        ; Compute A xor B:
            ;; Note that this code relies on the constant-time
            ;; short-cuts provided by LSET-DIFF+INTERSECTION,
            ;; LSET-DIFFERENCE & APPEND to provide constant-time short
            ;; cuts for the cases A = (), B = (), and A eq? B. It takes
            ;; a careful case analysis to see it, but it's carefully
            ;; built in.

            ;; Compute a-b and a^b, then compute b-(a^b) and
            ;; cons it onto the front of a-b.
            (receive (a-b a-int-b)   (lset-diff+intersection! = a b)
              (cond ((null? a-b)     (lset-difference! b a =))
                    ((null? a-int-b) (append! b a))
                    (else (pair-fold (lambda (b-pair ans)
                                       (if (member (car b-pair) a-int-b =) ans
                                           (begin (set-cdr! b-pair ans) b-pair)))
                                     a-b
                                     b)))))
          '() lists))


(define (lset-diff+intersection = lis1 . lists)
  (check-arg procedure? = lset-diff+intersection)
  (cond ((every null-list? lists) (values lis1 '()))        ; Short cut
        ((memq lis1 lists)        (values '() lis1))        ; Short cut
        (else (partition (lambda (elt)
                           (not (any (lambda (lis) (member elt lis =))
                                     lists)))
                         lis1))))

(define (lset-diff+intersection! = lis1 . lists)
  (check-arg procedure? = lset-diff+intersection!)
  (cond ((every null-list? lists) (values lis1 '()))        ; Short cut
        ((memq lis1 lists)        (values '() lis1))        ; Short cut
        (else (partition! (lambda (elt)
                            (not (any (lambda (lis) (member elt lis =))
                                      lists)))
                          lis1))))
";
}

} // namespace NetLisp.Frontend