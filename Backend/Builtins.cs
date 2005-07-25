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
using System.Collections;
using System.IO;
using System.Reflection;

namespace NetLisp.Backend
{

#region Lisp code
[LispCode(@"
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
        (let ((first (car x)))
          (if (expander? first) ((expander-function first) x e)
              (map (lambda (x) (e x e)) x))))))

(define expand (lambda (x) (initial-expander x initial-expander)))
(define expand-once (lambda (x) (initial-expander x (lambda (x e) x))))

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
            (set! bindings (map (lambda (init)
                                  (set! init (#%strip-debug init))
                                  (if (pair? init) init (list init nil)))
                                (caddr x)))
            (e `(letrec ((,name (lambda ,(map car bindings) ,@(cdddr x))))
                  (,name ,@(map cadr bindings))) e))
          `(let ,(map (lambda (init) (if (pair? init)
                                         (list (car init) (e (cadr init) e))
                                         init))
                      bindings)
                ,@(#_body-expander (cddr x) e))))))

(install-expander 'let-values
  (lambda (x e)
    (let ((bindings (cadr x)))
      `(let-values ,(map (lambda (init) (list (#%strip-debug (car init)) (e (cadr init) e))) bindings)
         ,@(#_body-expander (cddr x) e)))))

(install-expander 'lambda
  (lambda (x e) `(lambda ,(#%strip-debug (cadr x)) ,@(#_body-expander (cddr x) e))))

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
          `(if ,(car items) (and ,@(cdr items)) #f))))

(install-expander 'try
  (lambda (x e)
    (let ((mex (lambda (x) (e x e))))
      `(try
        ,(e (cadr x) e)
        ,@(map (lambda (cf)
                 (if (pair? cf)
                     (case (car cf)
                      (catch `(catch ,(#%strip-debug (cadr cf)) ,@(map mex (cddr cf))))
                      (finally `(finally ,@(map mex (cdr cf))))
                      (else cf))
                     cf))
               (cddr x))))))        

(defmacro letrec (bindings . body)
  `(let ,(map (lambda (init) (if (pair? init) (car init) init)) bindings)
     ,@(apply append (map (lambda (init) (if (pair? init) `((set! ,(car init) ,(cadr init))))) bindings))
     ,@body))

(defmacro let* (bindings . body)
  (let rec ((bindings bindings))
    (if (null? bindings) `(begin ,@body)
        `(let (,(if (pair? (car bindings)) `(,(caar bindings) ,(cadar bindings))
                    (car bindings)))
           ,(rec (cdr bindings))))))

(defmacro fluid-let (bindings . body)
  (if (null? bindings) `(begin ,@body)
      (let ((gensyms (map (lambda (init) (gensym (symbol->string (car init)))) bindings)))
        `(let ,(map (lambda (init sym) (list sym (car init))) bindings gensyms)
          (try
            (begin
              ,@(map (lambda (init) `(set! ,(car init) ,(cadr init))) bindings)
              ,@body)
            (finally
              ,@(map (lambda (init sym) `(set! ,(car init) ,sym)) bindings gensyms)))))))

(defmacro or items
  (if (null? items) #f
      (letrec ((tmp (gensym ""tmp""))
               (rec (lambda (items)
                      (if (null? items) #f
                          `(if (set! ,tmp ,(car items)) ,tmp
                               ,(rec (cdr items)))))))
        `(let (,tmp) ,(rec items)))))

(defmacro defstruct (name . fields)
  (let* ((name-s (symbol->string name))
         (n+1    (+ (length fields) 1))
         (names  (make-vector n+1))
         (inits  (make-vector n+1)))
    (let loop ((i 1) (ff fields))
        (if (!= i n+1)
            (let ((f (car ff)))
              (vector-set! names i (if (pair? f) (car f) f))
              (vector-set! inits i (if (pair? f) (cadr f) nil))
              (loop (+ i 1) (cdr ff)))))
    (let ((fn (gensym ""args""))
          (vn (gensym ""vec""))
          (pn (gensym ""pos"")))
      `(begin
         (define ,(string->symbol (string-append ""make-"" name-s))
           (lambda ,fn
             (let ((,vn (make-vector ,n+1)) ,pn)
               (vector-set! ,vn 0 ',name)
               ,@(let loop ((i 1) (ret nil))
                   (if (= i n+1) ret
                       (loop (+ i 1)
                             (cons
                               `(set! ,pn (memq ',(vector-ref names i) ,fn))
                               (cons
                                 (if (null? (vector-ref inits i))
                                     `(if ,pn (vector-set! ,vn ,i (cadr ,pn)))
                                     `(vector-set! ,vn ,i (if ,pn (cadr ,pn) ,(vector-ref inits i))))
                                 ret)))))
               ,vn)))
         ,@(let loop ((i 1) (ret nil))
             (if (= i n+1) ret
                 (loop (+ i 1)
                       (cons
                         `(define ,(string->symbol (string-append name-s ""."" (symbol->string (vector-ref names i))))
                            (lambda (s) (vector-ref s ,i)))
                         (cons
                           `(define ,(string->symbol (string-append name-s "".set-""
                                                                    (symbol->string (vector-ref names i)) ""!""))
                              (lambda (s v) (vector-set! s ,i v)))
                           ret)))))
         (define ,(string->symbol (string-append name-s ""?""))
           (lambda (s) (and (vector? s) (eq? (vector-ref s 0) ',name))))))))

(defmacro do (inits test . body)
  (let ((loop (gensym ""loop"")))
    `(let ,loop ,(map (lambda (init) (list (car init) (cadr init))) inits)
       (if ,(car test)
           ,(if (null? (cdr test)) nil `(begin ,@(cdr test)))
           (begin ,@body
                  (,loop ,@(map (lambda (init)
                                  (if (cddr init) (caddr init) (car init)))
                                inits)))))))

(defmacro dynamic-wind (before thunk after)
  `(try
    (begin
     (,before)
     (,thunk))
    (finally (,after))))

(defmacro while (cond . body)
  (let ((loop (gensym ""loop"")))
    `(let ,loop ()
       (if ,cond
           (begin ,@body (,loop))))))

(defmacro .foreach ((name in) . body)
  (let ((e (gensym ""enum""))
        (loop (gensym ""loop"")))
    `(let ((,e (.call ""GetEnumerator"" ,in)))
       (while (.call ""MoveNext"" ,e)
         (let ((,name (.get ""Current"" ,e)))
           ,@body)))))

(defmacro delay (form)
  `(#%delay (lambda () ,form)))

(install-expander 'require
  (lambda (x e)
    (e `(#%import-module ',(cadr x)) e)))

(install-expander 'require-for-syntax
  (lambda (x e)
    (#%import-module (cadr x))
    nil))

(install-expander 'module
  (lambda (x e)
    (#_null-expander (caddr x) `(#%module ,(cadr x) ,@(cdddr x)) e)))

(install-expander 'install-expander
  (lambda (x e)
    (let ((name (cadr x)) (body (e (caddr x) e)))
      (install-expander (eval name) (eval body))
      `(install-expander ,name ,body))))
")]
#endregion

public sealed class Builtins
{ 
[SymbolName("load-assembly-by-name")]
public static void loadByName(string name) { Interop.LoadAssemblyByName(name); }
[SymbolName("load-assembly-from-file")]
public static void loadFromFile(string name) { Interop.LoadAssemblyFromFile(name); }
public static void println(object obj) { Console.WriteLine(Ops.Repr(obj)); }

  // TODO: add support for operators (.+ .- .*, etc)
  #region .NET functions
  #region MemberKey
  struct MemberKey
  { public MemberKey(Type type, string name) { Type=type; Name=name; }

    public override bool Equals(object obj)
    { MemberKey other = (MemberKey)obj;
      return other.Type==Type && other.Name==Name;
    }

    public override int GetHashCode()
    { return Type==null ? Name.GetHashCode() : Name.GetHashCode() ^ Type.GetHashCode();
    }

    public Type Type;
    public string Name;
  }
  #endregion

  #region .add-event
  public sealed class dotAddEvent : Primitive
  { public dotAddEvent() : base(".add-event", 2, 3) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object instance;
      IProcedure handler;
      Interop.MakeFunctionWrapper(core(name, args, out instance, out handler).GetAddMethod())
        .Call(instance==null ? new object[1] { handler } : new object[2] { instance, handler });
      return null;
    }

    internal static EventInfo core(string name, object[] args, out object instance, out IProcedure handler)
    { string eventName = Ops.ExpectString(args[0]);
      if(args.Length==2) { instance=null; handler=Ops.ExpectProcedure(args[1]); }
      else { instance=args[1]; handler=Ops.ExpectProcedure(args[2]); }

      Type type;
      if(instance==null)
      { int pos = eventName.LastIndexOf('.');
        type = Type.GetType(eventName.Substring(0, pos));
        if(type==null) throw Ops.ValueError(name+": unable to find type: "+eventName.Substring(0, pos));
        eventName = eventName.Substring(pos+1);
      }
      else type = instance.GetType();

      EventInfo ei = type.GetEvent(eventName);
      if(ei==null) throw new ArgumentException("type "+type.FullName+" does not have a '"+eventName+"' event");
      return ei;
    }
  }
  #endregion

  #region .call
  public sealed class dotCall : Primitive
  { public dotCall() : base(".call", 2, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return core(name, dotFuncs, args);
    }
    
    internal static object core(string name, Hashtable hash, object[] args)
    { object instance = args[1];
      MemberKey key = new MemberKey(instance==null ? null : instance.GetType(), Ops.ExpectString(args[0]));
      FunctionWrapper[] funcs;
      lock(hash) funcs = (FunctionWrapper[])hash[key];
      if(funcs==null)
      { Type type;
        string funcName = key.Name;

        if(instance==null)
        { int pos = funcName.LastIndexOf('.');
          type = Type.GetType(funcName.Substring(0, pos));
          if(type==null) throw Ops.ValueError(name+": unable to find type: "+funcName.Substring(0, pos));
          funcName = funcName.Substring(pos+1);
        }
        else type = key.Type;

        if(hash==dotFuncs) funcs = Interop.GetFunctions(type, funcName, instance==null);
        else if(hash==dotPgets) funcs = Interop.GetPropertyGetters(type, funcName, instance==null);
        else if(hash==dotPsets) funcs = Interop.GetPropertySetters(type, funcName, instance==null);
        else throw new NotImplementedException("unhandled hash");

        lock(hash) hash[key] = funcs;
      }

      object[] nargs;
      { int offset = instance==null ? 2 : 1;
        if(args.Length==offset) nargs = Ops.EmptyArray;
        else
        { nargs = new object[args.Length-offset];
          Array.Copy(args, offset, nargs, 0, nargs.Length);
        }
      }

      return Interop.Call(funcs, nargs);
    }
  }
  #endregion

  #region .collect
  public sealed class dotCollect : Primitive
  { public dotCollect() : base(".collect", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IEnumerator e = Ops.ExpectEnumerator(args[0]);
      if(!e.MoveNext()) return null;
      Pair head=new Pair(e.Current, null), tail=head;
      while(e.MoveNext())
      { Pair next = new Pair(e.Current, null);
        tail.Cdr=next;
        tail=next;
      }
      return head;
    }
  }
  #endregion
  
  #region .get
  public sealed class dotGet : Primitive
  { public dotGet() : base(".get", 2, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return dotCall.core(name, dotPgets, args);
    }
  }
  #endregion

  #region .getf
  public sealed class dotGetf : Primitive
  { public dotGetf() : base(".getf", 1, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object instance = args.Length==2 ? args[1] : null;
      return core(name, instance, Ops.ExpectString(args[0])).GetValue(instance);
    }
    
    internal static FieldInfo core(string name, object instance, string fieldName)
    { MemberKey key = new MemberKey(instance==null ? null : instance.GetType(), fieldName);
      FieldInfo fi;
      lock(dotFields) fi = (FieldInfo)dotFields[key];
      if(fi==null)
      { Type type;
        if(instance==null)
        { int pos = fieldName.LastIndexOf('.');
          type = Type.GetType(fieldName.Substring(0, pos));
          if(type==null) throw Ops.ValueError(name+": unable to find type: "+fieldName.Substring(0, pos));
          fieldName = fieldName.Substring(pos+1);
        }
        else type = key.Type;
        fi = type.GetField(fieldName);
        if(fi==null) throw new ArgumentException("type "+type.FullName+" does not have a '"+fieldName+"' field");
        lock(dotFields) dotFields[key] = fi;
      }
      return fi;
    }
  }
  #endregion

  #region .new
  public sealed class dotNew : Primitive
  { public dotNew() : base(".new", 1, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);

      string name = Ops.ExpectString(args[0]);
      FunctionWrapper[] funcs;
      lock(dotNews) funcs = (FunctionWrapper[])dotNews[name];
      if(funcs==null)
      { Type type = Type.GetType(name);
        if(type==null) throw Ops.ValueError(this.name+": unable to find type: "+name);
        funcs = Interop.GetConstructors(type);
        lock(funcs) dotNews[name] = funcs;
      }

      object[] nargs;
      if(args.Length==1) nargs = Ops.EmptyArray;
      else
      { nargs = new object[args.Length-1];
        Array.Copy(args, 1, nargs, 0, nargs.Length);
      }

      return Interop.Call(funcs, nargs);
    }
  }
  #endregion

  #region .remove-event
  public sealed class dotRemoveEvent : Primitive
  { public dotRemoveEvent() : base(".remove-event", 2, 3) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object instance;
      IProcedure handler;
      Interop.MakeFunctionWrapper(dotAddEvent.core(name, args, out instance, out handler).GetRemoveMethod())
        .Call(instance==null ? new object[1] { handler } : new object[2] { instance, handler });
      return null;
    }
  }
  #endregion

  #region .set
  public sealed class dotSet : Primitive
  { public dotSet() : base(".set", 2, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return dotCall.core(name, dotPsets, args);
    }
  }
  #endregion

  #region .setf
  public sealed class dotSetf : Primitive
  { public dotSetf() : base(".setf", 2, 3) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object instance, value;
      if(args.Length==2) { instance=null; value=args[1]; }
      else { instance=args[1]; value=args[2]; }
      dotGetf.core(name, instance, Ops.ExpectString(args[0])).SetValue(instance, value);
      return value;
    }
  }
  #endregion

  #region make-ref
  public sealed class makeRef : Primitive
  { public makeRef() : base("make-ref", 0, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return new Reference(args.Length==0 ? null : args[0]);
    }
  }
  #endregion

  #region ref
  public sealed class @ref : Primitive
  { public @ref() : base("ref", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IList list = args[0] as IList;
      if(list!=null) return list[Ops.ExpectInt(args[1])];
      IDictionary dict = args[0] as IDictionary;
      if(dict!=null) return dict[args[1]];
      string str = args[0] as string;
      if(str!=null) return str[Ops.ExpectInt(args[1])];
      Pair pair = args[0] as Pair;
      if(pair!=null) return listRef.core(name, pair, Ops.ExpectInt(args[1]));
      throw Ops.TypeError(name+": expected container type, but received "+Ops.TypeName(args[0]));
    }
  }
  #endregion

  #region ref-get
  public sealed class refGet : Primitive
  { public refGet() : base("ref-get", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectRef(args[0]).Value;
    }
  }
  #endregion

  #region ref-set!
  public sealed class refSetN : Primitive
  { public refSetN() : base("ref-set!", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectRef(args[0]).Value=args[1];
    }
  }
  #endregion
  #endregion

  #region Character functions
  #region char?
  public sealed class charP : Primitive
  { public charP() : base("char?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is char ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region char=?
  public sealed class charEqP : Primitive
  { public charEqP() : base("char=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectChar(args[0])==Ops.ExpectChar(args[1]);
    }
  }
  #endregion
  #region char!=?
  public sealed class charNeP : Primitive
  { public charNeP() : base("char!=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectChar(args[0])!=Ops.ExpectChar(args[1]);
    }
  }
  #endregion
  #region char<?
  public sealed class charLtP : Primitive
  { public charLtP() : base("char<?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectChar(args[0])<Ops.ExpectChar(args[1]);
    }
  }
  #endregion
  #region char<=?
  public sealed class charLeP : Primitive
  { public charLeP() : base("char<=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectChar(args[0])<=Ops.ExpectChar(args[1]);
    }
  }
  #endregion
  #region char>?
  public sealed class charGtP : Primitive
  { public charGtP() : base("char>?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectChar(args[0])>Ops.ExpectChar(args[1]);
    }
  }
  #endregion
  #region char>=?
  public sealed class charGeP : Primitive
  { public charGeP() : base("char>=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectChar(args[0])>=Ops.ExpectChar(args[1]);
    }
  }
  #endregion

  #region char-ci=?
  public sealed class charCiEqP : Primitive
  { public charCiEqP() : base("char-ci=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]))==char.ToUpper(Ops.ExpectChar(args[1]));
    }
  }
  #endregion
  #region char-ci!=?
  public sealed class charCiNeP : Primitive
  { public charCiNeP() : base("char-ci!=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]))!=char.ToUpper(Ops.ExpectChar(args[1]));
    }
  }
  #endregion
  #region char-ci<?
  public sealed class charCiLtP : Primitive
  { public charCiLtP() : base("char-ci<?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]))<char.ToUpper(Ops.ExpectChar(args[1]));
    }
  }
  #endregion
  #region char-ci<=?
  public sealed class charCiLeP : Primitive
  { public charCiLeP() : base("char-ci<=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]))<=char.ToUpper(Ops.ExpectChar(args[1]));
    }
  }
  #endregion
  #region char-ci>?
  public sealed class charCiGtP : Primitive
  { public charCiGtP() : base("char-ci>?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]))>char.ToUpper(Ops.ExpectChar(args[1]));
    }
  }
  #endregion
  #region char-ci>=?
  public sealed class charCiGeP : Primitive
  { public charCiGeP() : base("char-ci>=?", 2, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]))>=char.ToUpper(Ops.ExpectChar(args[1]));
    }
  }
  #endregion

  #region char->integer
  public sealed class charToInteger : Primitive
  { public charToInteger() : base("char->integer", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return (int)Ops.ExpectChar(args[0]);
    }
  }
  #endregion
  #region integer->char
  public sealed class integerToChar : Primitive
  { public integerToChar() : base("integer->char", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return (char)Ops.ExpectInt(args[0]);
    }
  }
  #endregion

  #region char-upcase
  public sealed class charUpcase : Primitive
  { public charUpcase() : base("char-upcase", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToUpper(Ops.ExpectChar(args[0]));
    }
  }
  #endregion
  #region char-downcase
  public sealed class charDowncase : Primitive
  { public charDowncase() : base("char-downcase", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.ToLower(Ops.ExpectChar(args[0]));
    }
  }
  #endregion

  #region char-upper-case?
  public sealed class charUpperCaseP : Primitive
  { public charUpperCaseP() : base("char-upper-case?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      char c = Ops.ExpectChar(args[0]);
      return char.IsLetter(c) && char.ToUpper(c)==c;
    }
  }
  #endregion
  #region char-lower-case?
  public sealed class charLowerCaseP : Primitive
  { public charLowerCaseP() : base("char-lower-case?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      char c = Ops.ExpectChar(args[0]);
      return char.IsLetter(c) && char.ToLower(c)==c;
    }
  }
  #endregion
  #region char-alphabetic?
  public sealed class charAlphabetic : Primitive
  { public charAlphabetic() : base("char-alphabetic?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.IsLetter(Ops.ExpectChar(args[0]));
    }
  }
  #endregion
  #region char-numeric?
  public sealed class charNumeric : Primitive
  { public charNumeric() : base("char-numeric?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.IsDigit(Ops.ExpectChar(args[0]));
    }
  }
  #endregion
  #region char-alphanumeric?
  public sealed class charAlphaumeric : Primitive
  { public charAlphaumeric() : base("char-alphanumeric?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.IsLetterOrDigit(Ops.ExpectChar(args[0]));
    }
  }
  #endregion
  #region char-whitespace?
  public sealed class charWhitespace : Primitive
  { public charWhitespace() : base("char-whitespace?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return char.IsWhiteSpace(Ops.ExpectChar(args[0]));
    }
  }
  #endregion
  #region char-punctuation?
  public sealed class charPunctuation : Primitive
  { public charPunctuation() : base("char-punctuation?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      char c = Ops.ExpectChar(args[0]);
      return char.IsPunctuation(c) || char.IsSymbol(c);
    }
  }
  #endregion
  #region char-printable?
  public sealed class charPrintable : Primitive
  { public charPrintable() : base("char-printable?", 1, 1) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      char c = Ops.ExpectChar(args[0]);
      return !char.IsWhiteSpace(c) && !char.IsControl(c);
    }
  }
  #endregion

  #region char->digit
  public sealed class charToDigit : Primitive
  { public charToDigit() : base("char->digit", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      int num = (int)Ops.ExpectChar(args[0]) - 48;
      int radix = args.Length==2 ? Ops.ExpectInt(args[1]) : 10;
      if(num>48) num -= 32 + 7;
      else if(num>16) num -= 7;
      else if(num>9) return Ops.FALSE;
      return num<0 || num>=radix ? Ops.FALSE : num;
    }
  }
  #endregion
  #region digit->char
  public sealed class digitToChar : Primitive
  { public digitToChar() : base("digit->char", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      int num = Ops.ExpectInt(args[0]);
      int radix = args.Length==2 ? Ops.ExpectInt(args[1]) : 10;
      return num<0 || num>=radix ? Ops.FALSE : convert[num];
    }
    
    const string convert = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
  }
  #endregion

  #region char->name
  public sealed class charToName : Primitive
  { public charToName() : base("char->name", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return core(Ops.ExpectChar(args[0]), args.Length==2 && Ops.IsTrue(args[1]));
    }
    
    internal static string core(char c, bool slashify)
    { string name;
      switch((int)c)
      { case 0:  name="nul"; break;
        case 7:  name="bel"; break;
        case 8:  name="bs"; break;
        case 9:  name="tab"; break;
        case 10: name="lf"; break;
        case 11: name="vt"; break;
        case 12: name="ff"; break;
        case 13: name="cr"; break;
        case 27: name="esc"; break;
        case 28: name="fs"; break;
        case 29: name="gs"; break;
        case 30: name="rs"; break;
        case 31: name="us"; break;
        case 32: name="space"; break;
        default: name = c>32 ? c==127 ? "del" : c.ToString() : "C-"+((char)(c+96)).ToString(); break;
      }
      return slashify ? "#\\"+name : name;
    }
  }
  #endregion
  #region name->char
  public sealed class nameToChar : Primitive
  { public nameToChar() : base("name->char", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return core(Ops.ExpectString(args[0]));
    }

    internal static char core(string name)
    { char c;
      if(name.Length==0) throw Ops.ValueError("name->char: expected non-empty string");
      else if(name.Length==1) c=name[0];
      else if(name.StartsWith("c-") && name.Length==3)
      { int i = char.ToUpper(name[2])-64;
        if(i<1 || i>26) throw Ops.ValueError("name->char: invalid control code "+name);
        c=(char)i;
      }
      else
        switch(name.ToLower())
        { case "space": c=(char)32; break;
          case "lf": case "linefeed": case "newline": c=(char)10; break;
          case "cr": case "return": c=(char)13; break;
          case "tab": case "ht": c=(char)9; break;
          case "bs": case "backspace": c=(char)8; break;
          case "esc": case "altmode": c=(char)27; break;
          case "del": case "rubout": c=(char)127; break;
          case "nul": c=(char)0; break;
          case "soh": c=(char)1; break;
          case "stx": c=(char)2; break;
          case "etx": c=(char)3; break;
          case "eot": c=(char)4; break;
          case "enq": c=(char)5; break;
          case "ack": c=(char)6; break;
          case "bel": c=(char)7; break;
          case "vt":  c=(char)11; break;
          case "ff": case "page": c=(char)12; break;
          case "so":  c=(char)14; break;
          case "si":  c=(char)15; break;
          case "dle": c=(char)16; break;
          case "dc1": c=(char)17; break;
          case "dc2": c=(char)18; break;
          case "dc3": c=(char)19; break;
          case "dc4": c=(char)20; break;
          case "nak": c=(char)21; break;
          case "syn": c=(char)22; break;
          case "etb": c=(char)23; break;
          case "can": c=(char)24; break;
          case "em":  c=(char)25; break;
          case "sub": case "call": c=(char)26; break;
          case "fs":  c=(char)28; break;
          case "gs":  c=(char)29; break;
          case "rs":  c=(char)30; break;
          case "us": case "backnext": c=(char)31; break;
          default: throw Ops.ValueError("name->char: unknown character name '"+name+"'");
        }

      return c;
    }
  }
  #endregion
  #endregion
  
  // TODO: almost everything (http://www.schemers.org/Documents/Standards/R5RS/HTML/r5rs-Z-H-2.html#%_toc_%_sec_6.6)
  #region I/O functions
  [SymbolName("input-port?")]
  public bool inputPortP(object obj)
  { Stream stream = obj as Stream;
    return stream!=null && stream.CanRead;
  }

  [SymbolName("output-port?")]
  public bool outputPortP(object obj)
  { Stream stream = obj as Stream;
    return stream!=null && stream.CanWrite;
  }
  #endregion

  #region List functions
  #region append
  public sealed class append : Primitive
  { public append() : base("append", 0, -1) { }

    public override object Call(object[] args)
    { if(args.Length==0) return null;
      if(args.Length==1) return args[0];

      Pair head=null, prev=null;
      int i;
      for(i=0; i<args.Length-1; i++)
      { if(args[i]==null) continue;
        Pair pair=Ops.ExpectPair(args[i]), tail=new Pair(pair.Car, pair.Cdr);
        if(prev==null) head = tail;
        else prev.Cdr = tail;
        while(true)
        { pair = pair.Cdr as Pair;
          if(pair==null) break;
          Pair next = new Pair(pair.Car, pair.Cdr);
          tail.Cdr = next;
          tail = next;
        }
        prev = tail;
      }
      if(prev==null) return args[i];
      prev.Cdr = args[i];
      return head;
    }
  }
  #endregion
  #region append!
  public sealed class appendN : Primitive
  { public appendN() : base("append!", 0, -1) { }
    public override object Call(object[] args)
    { if(args.Length==0) return null;
      if(args.Length==1) return args[0];
      
      Pair head=null, prev=null;
      int i;
      for(i=0; i<args.Length-1; i++)
      { if(args[i]==null) continue;
        Pair pair=Ops.ExpectPair(args[i]);
        if(prev==null) head=pair;
        else prev.Cdr=pair;
        do
        { prev = pair;
          pair = pair.Cdr as Pair;
        } while(pair!=null);
      }
      if(prev==null) return args[i];
      prev.Cdr = args[i];
      return head;
    }
  }
  #endregion

  #region assq
  public sealed class assq : Primitive
  { public assq() : base("assq", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj=args[0];
      Pair  list=Ops.ExpectList(args[1]);
      while(list!=null)
      { Pair pair = list.Car as Pair;
        if(pair==null) throw Ops.ValueError(name+": alists must contain only pairs");
        if(pair.Car==obj) return pair;
        list = list.Cdr as Pair;
      }
      return Ops.FALSE;
    }
  }
  #endregion
  #region assv
  public sealed class assv : Primitive
  { public assv() : base("assv", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj=args[0];
      Pair  list=Ops.ExpectList(args[1]);
      while(list!=null)
      { Pair pair = list.Car as Pair;
        if(pair==null) throw Ops.ValueError(name+": alists must contain only pairs");
        if(Ops.EqvP(obj, pair.Car)) return pair;
        list = list.Cdr as Pair;
      }
      return Ops.FALSE;
    }
  }
  #endregion
  #region assoc
  public sealed class assoc : Primitive
  { public assoc() : base("assoc", 2, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj=args[0];
      Pair  list=Ops.ExpectList(args[1]);
      if(args.Length==2)
        while(list!=null)
        { Pair pair = list.Car as Pair;
          if(pair==null) throw Ops.ValueError(name+": alists must contain only pairs");
          if(Ops.EqualP(obj, pair.Car)) return pair;
          list = list.Cdr as Pair;
        }
      else
      { IProcedure pred = Ops.ExpectProcedure(args[2]);
        bool realloc = pred.NeedsFreshArgs;
        if(!realloc)
        { args = new object[2];
          args[1] = obj;
        }
        while(list!=null)
        { Pair pair = list.Car as Pair;
          if(pair==null) throw Ops.ValueError(name+": alists must contain only pairs");
          if(realloc) args = new object[2] { pair.Car, obj };
          else args[0] = pair.Car;
          if(Ops.IsTrue(pred.Call(args))) return list;
          list = list.Cdr as Pair;
        }
      }
      return Ops.FALSE;
    }
  }
  #endregion

  #region except-last-pair
  public sealed class exceptLastPair : Primitive
  { public exceptLastPair() : base("except-last-pair", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair head=Ops.ExpectPair(args[0]), prev=head, pair=prev.Cdr as Pair;
      if(pair==null) return null;
      head = prev = new Pair(head.Car, null);
      while(true)
      { Pair next = pair.Cdr as Pair;
        if(next==null) return head;
        prev.Cdr=pair=new Pair(pair.Car, null); prev=pair; pair=next;
      }
    }
  }
  #endregion
  #region except-last-pair!
  public sealed class exceptLastPairN : Primitive
  { public exceptLastPairN() : base("except-last-pair!", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair head=Ops.ExpectPair(args[0]), prev=head, pair=prev.Cdr as Pair;
      if(pair==null) return null;
      while(true)
      { Pair next = pair.Cdr as Pair;
        if(next==null) { prev.Cdr=null; return head; }
        prev=pair; pair=next;
      }
    }
  }
  #endregion

  #region for-each
  public sealed class forEach : Primitive
  { public forEach() : base("for-each", 1, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==1) return null;

      IProcedure func = Ops.ExpectProcedure(args[0]);
      bool realloc = func.NeedsFreshArgs;

      if(args.Length==2)
      { Pair p = Ops.ExpectList(args[1]);
        if(!realloc) args = new object[1];
        while(p!=null)
        { if(realloc) args = new object[1];
          args[0] = p.Car;
          p = p.Cdr as Pair;
          func.Call(args);
        }
        return null;
      }
      else
      { Pair[] pairs = new Pair[args.Length-1];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        if(!realloc) args = new object[pairs.Length];

        while(true)
        { if(realloc) args = new object[pairs.Length];
          for(int i=0; i<pairs.Length; i++)
          { if(pairs[i]==null) return null;
            args[i] = pairs[i].Car;
            pairs[i] = pairs[i].Cdr as Pair;
          }
          func.Call(args);
        }
      }
    }
  }
  #endregion

  #region list?
  public sealed class listP : Primitive
  { public listP() : base("list?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair slow = args[0] as Pair;
      if(slow==null) return Ops.FALSE;
      
      Pair fast = slow.Cdr as Pair;
      if(fast==null) return slow.Cdr==null ? Ops.TRUE : Ops.FALSE;
    
      while(true)
      { if(slow==fast) return Ops.FALSE;
        slow = (Pair)slow.Cdr;
        Pair next = fast.Cdr as Pair;
        if(next==null) return fast.Cdr==null ? Ops.TRUE : Ops.FALSE;
        fast = next.Cdr as Pair;
        if(fast==null) return next.Cdr==null ? Ops.TRUE : Ops.FALSE;
      }
    }
  }
  #endregion

  #region list-ref
  public sealed class listRef : Primitive
  { public listRef() : base("list-ref", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return core(name, Ops.ExpectPair(args[0]), Ops.ExpectInt(args[1]));
    }
    internal static object core(string name, Pair pair, int index)
    { Pair p = Mods.Srfi1.drop.core(name, pair, index);
      if(p==null) throw new ArgumentException(name+": list is not long enough");
      return p.Car;
    }
  }
  #endregion

  #region length
  public sealed class length : Primitive
  { public length() : base("length", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return core(Ops.ExpectList(args[0]));
    }

    internal static int core(Pair pair)
    { if(pair==null) return 0;
      int total=1;
      while(true)
      { pair = pair.Cdr as Pair;
        if(pair==null) break;
        total++;
      }
      return total;
    }
  }
  #endregion

  #region map
  public sealed class map : Primitive
  { public map() : base("map", 1, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==1) return null;

      IProcedure func = Ops.ExpectProcedure(args[0]);
      bool realloc = func.NeedsFreshArgs;

      if(args.Length==2)
      { Pair p=Ops.ExpectList(args[1]), head=null, tail=null;
        if(!realloc) args = new object[1];
        while(p!=null)
        { if(realloc) args = new object[1];
          args[0] = p.Car;
          p = p.Cdr as Pair;
          Pair next = new Pair(func.Call(args), null);
          if(head==null) head=tail=next;
          else { tail.Cdr=next; tail=next; }
        }
        return head;
      }
      else
      { Pair[] pairs = new Pair[args.Length-1];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        if(!realloc) args = new object[pairs.Length];

        Pair head=null, tail=null;
        while(true)
        { if(realloc) args = new object[pairs.Length];
          for(int i=0; i<pairs.Length; i++)
          { if(pairs[i]==null) return head;
            args[i] = pairs[i].Car;
            pairs[i] = pairs[i].Cdr as Pair;
          }
          Pair next = new Pair(func.Call(args), null);
          if(head==null) head=tail=next;
          else { tail.Cdr=next; tail=next; }
        }
      }
    }
  }
  #endregion

  #region memq
  public sealed class memq : Primitive
  { public memq() : base("memq", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj=args[0];
      Pair  list=Ops.ExpectList(args[1]);
      while(list!=null)
      { if(list.Car==obj) return list;
        list = list.Cdr as Pair;
      }
      return Ops.FALSE;
    }
  }
  #endregion
  #region memv
  public sealed class memv : Primitive
  { public memv() : base("memv", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj=args[0];
      Pair  list=Ops.ExpectList(args[1]);
      while(list!=null)
      { if(Ops.EqvP(obj, list.Car)) return list;
        list = list.Cdr as Pair;
      }
      return Ops.FALSE;
    }
  }
  #endregion
  #region member
  public sealed class member : Primitive
  { public member() : base("member", 2, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj=args[0];
      Pair  list=Ops.ExpectList(args[1]);
      if(args.Length==2)
        while(list!=null)
        { if(Ops.EqualP(obj, list.Car)) return list;
          list = list.Cdr as Pair;
        }
      else
      { IProcedure pred = Ops.ExpectProcedure(args[2]);
        bool realloc = pred.NeedsFreshArgs;
        if(!realloc)
        { args = new object[2];
          args[1] = obj;
        }
        while(list!=null)
        { if(realloc) args = new object[2] { list.Car, obj };
          else args[0] = list.Car;
          if(Ops.IsTrue(pred.Call(args))) return list;
          list = list.Cdr as Pair;
        }
      }
      return Ops.FALSE;
    }
  }
  #endregion

  #region reverse
  public sealed class reverse : Primitive
  { public reverse() : base("reverse", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair pair=Ops.ExpectList(args[0]), list=null;
      while(pair!=null)
      { list = new Pair(pair.Car, list);
        pair = pair.Cdr as Pair;
      }
      return list;
    }
  }
  #endregion
  #region reverse!
  public sealed class reverseN : Primitive
  { public reverseN() : base("reverse!", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair pair=Ops.ExpectList(args[0]);
      if(pair==null) return null;
      Pair next = pair.Cdr as Pair;
      if(next==null) return pair;

      pair.Cdr = null;
      do
      { Pair nnext = next.Cdr as Pair;
        next.Cdr = pair;
        pair=next; next=nnext;
      } while(next!=null);
      return pair;
    }
  }
  #endregion

  #region sublist
  public sealed class sublist : Primitive
  { public sublist() : base("sublist", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair  pair = Ops.ExpectList(args[0]);
      int  start = Ops.ExpectInt(args[1]);
      int length = Ops.ExpectInt(args[2]);

      if(start!=0) pair = Mods.Srfi1.drop.core(name, pair, start);
      return Mods.Srfi1.take.core(name, pair, length);
    }
  }
  #endregion

  #region tree-copy
  public sealed class treeCopy : Primitive
  { public treeCopy() : base("tree-copy", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args[0]==null) return null;
      return copy(Ops.ExpectPair(args[0]));
    }

    static object copy(object obj) // TODO: optimize this
    { Pair pair = obj as Pair;
      if(pair==null) return obj;
      return new Pair(copy(pair.Car), copy(pair.Cdr));
    }
  }
  #endregion
  
  #region vector->list
  public sealed class vectorToList : Primitive
  { public vectorToList() : base("vector->list", 1, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object[] vec = Ops.ExpectVector(args[0]);
      if(vec.Length==0) return null;
      int start, length;
      if(args.Length==2) { start=0; length=vec.Length; }
      else
      { if(args.Length==3) { start=Ops.ExpectInt(args[1]); length=vec.Length-start; }
        else { start=Ops.ExpectInt(args[1]); length=Ops.ExpectInt(args[2]); }
        if(start<0 || length<0 || start+length>=vec.Length)
          throw Ops.ValueError(name+": start or length out of bounds");
      }
      return Ops.List(vec, start, length);
    }
  }
  #endregion
  #endregion
  
  #region Macro expansion
  public static object expand(object form) { return form; }

  [SymbolName("expander?")]
  public static bool expanderP(object obj)
  { Symbol sym = obj as Symbol;
    return sym!=null && TopLevel.Current.ContainsMacro(sym.Name);
  }

  [SymbolName("expander-function")]
  public static IProcedure expanderFunction(Symbol sym) { return TopLevel.Current.GetMacro(sym.Name); }

  [SymbolName("install-expander")]
  public static object installExpander(Symbol sym, Closure func)
  { TopLevel.Current.AddMacro(sym.Name, func);
    return sym;
  }
  
  public sealed class _nullExpander : Primitive
  { public _nullExpander() : base("#_null-expander", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      TopLevel old = TopLevel.Current;
      try
      { TopLevel.Current = new TopLevel(); // FIXME: ignores args[0] (base module)
        return Ops.ExpectProcedure(args[2]).Call(args[1], args[2]);
      }
      finally { TopLevel.Current = old; }
    }
  }
  #endregion

  // TODO: lcm, gcd, etc
  #region Math functions
  #region abs
  public sealed class abs : Primitive
  { public abs() : base("abs", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: return obj;
        case TypeCode.Decimal: return Math.Abs((Decimal)obj);
        case TypeCode.Double: return Math.Abs((double)obj);
        case TypeCode.Int16: return Math.Abs((short)obj);
        case TypeCode.Int32: return Math.Abs((int)obj);
        case TypeCode.Int64: return Math.Abs((long)obj);
        case TypeCode.SByte: return Math.Abs((sbyte)obj);
        case TypeCode.Single: return Math.Abs((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).Abs;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Abs(c.real);
          }
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region acos
  public sealed class acos : Primitive
  { public acos() : base("acos", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Acos((Complex)obj) : (object)Math.Acos(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region angle
  public sealed class angle : Primitive
  { public angle() : base("angle", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).Angle;
    }
  }
  #endregion

  #region asin
  public sealed class asin : Primitive
  { public asin() : base("asin", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Asin((Complex)obj) : (object)Math.Asin(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region atan
  public sealed class atan : Primitive
  { public atan() : base("atan", 1, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==2) return Math.Atan2(Ops.ToFloat(args[0]), Ops.ToFloat(args[1]));

      object obj = args[0];
      return obj is Complex ? Complex.Atan((Complex)obj) : (object)Math.Atan(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region ceiling
  public sealed class ceiling : Primitive
  { public ceiling() : base("ceiling", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal:
        { Decimal d=(Decimal)obj, t=Decimal.Truncate(d);
          return d==t ? obj : t+Decimal.One;
        }
        case TypeCode.Double: return Math.Ceiling((double)obj);
        case TypeCode.Single: return Math.Ceiling((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Ceiling(c.real);
          }
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region conjugate
  public sealed class conjugate : Primitive
  { public conjugate() : base("conjugate", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).Conjugate;
    }
  }
  #endregion

  #region cos
  public sealed class cos : Primitive
  { public cos() : base("cos", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Cos(Ops.ToFloat(args[0]));
    }
  }
  #endregion

  #region log
  public sealed class log : Primitive
  { public log() : base("log", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Log((Complex)obj) : (object)Math.Log(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region log10
  public sealed class log10 : Primitive
  { public log10() : base("log10", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Log10((Complex)obj) : (object)Math.Log10(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region magnitude
  public sealed class magnitude : Primitive
  { public magnitude() : base("magnitude", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: return obj;
        case TypeCode.Decimal: return Math.Abs((Decimal)obj);
        case TypeCode.Double: return Math.Abs((double)obj);
        case TypeCode.Int16: return Math.Abs((short)obj);
        case TypeCode.Int32: return Math.Abs((int)obj);
        case TypeCode.Int64: return Math.Abs((long)obj);
        case TypeCode.SByte: return Math.Abs((sbyte)obj);
        case TypeCode.Single: return Math.Abs((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).Abs;
          if(obj is Complex) return ((Complex)obj).Magnitude;
          goto default;
        default: throw Ops.TypeError(name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region make-polar
  public sealed class makePolar : Primitive
  { public makePolar() : base("make-polar", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      double phase = Ops.ToFloat(args[1]);
      return new Complex(Math.Cos(phase), Math.Sin(phase)) * Ops.ToFloat(args[0]);
    }
  }
  #endregion

  #region make-rectangular
  public sealed class makeRectangular : Primitive
  { public makeRectangular() : base("make-rectangular", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return new Complex(Ops.ToFloat(args[0]), Ops.ToFloat(args[1]));
    }
  }
  #endregion

  #region exp
  public sealed class exp : Primitive
  { public exp() : base("exp", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Exp(Ops.ToFloat(args[0]));
    }
  }
  #endregion

  #region floor
  public sealed class floor : Primitive
  { public floor() : base("floor", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal: return Decimal.Floor((Decimal)obj);
        case TypeCode.Double: return Math.Floor((double)obj);
        case TypeCode.Single: return Math.Floor((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Floor(c.real);
          }
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region round
  public sealed class round : Primitive
  { public round() : base("round", 1, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);

      object obj = args[0];
      int places = args.Length==2 ? Ops.ToInt(args[1]) : 0;
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal: return Decimal.Round((Decimal)obj, places);
        case TypeCode.Double:  return Math.Round((double)obj, places);
        case TypeCode.Single:  return Math.Round((float)obj, places);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return Math.Round(c.real, places);
          }
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }

    static object doubleCore(double d)
    { try { return checked((int)d); }
      catch(OverflowException)
      { try { return checked((long)d); }
        catch(OverflowException) { return new Integer(d); }
      }
    }
  }
  #endregion

  #region sin
  public sealed class sin : Primitive
  { public sin() : base("sin", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Sin(Ops.ToFloat(args[0]));
    }
  }
  #endregion

  #region sqrt
  public sealed class sqrt : Primitive
  { public sqrt() : base("sqrt", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      return obj is Complex ? Complex.Sqrt((Complex)obj) : (object)Math.Sqrt(Ops.ToFloat(obj));
    }
  }
  #endregion

  #region tan
  public sealed class tan : Primitive
  { public tan() : base("tan", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return Math.Tan(Ops.ToFloat(args[0]));
    }
  }
  #endregion

  #region truncate
  public sealed class truncate : Primitive
  { public truncate() : base("truncate", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:  case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: 
        case TypeCode.SByte: case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
          return obj;
        case TypeCode.Decimal: return Decimal.Truncate((Decimal)obj);
        case TypeCode.Double:  return doubleCore((double)obj);
        case TypeCode.Single:  return doubleCore((float)obj);
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) return doubleCore(c.real);
          }
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
    
    static object doubleCore(double d)
    { try { return checked((int)d); }
      catch(OverflowException)
      { try { return checked((long)d); }
        catch(OverflowException) { return new Integer(d); }
      }
    }
  }
  #endregion
  #endregion

  // TODO: number->string
  #region Numeric functions
  #region complex?
  public sealed class complexP : Primitive
  { public complexP() : base("complex?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      switch(Convert.GetTypeCode(args[0]))
      { case TypeCode.Byte:   case TypeCode.Decimal: case TypeCode.Double:
        case TypeCode.Int16:  case TypeCode.Int32:   case TypeCode.Int64:
        case TypeCode.SByte:  case TypeCode.Single:
        case TypeCode.UInt16: case TypeCode.UInt32:  case TypeCode.UInt64:
          return Ops.TRUE;
        case TypeCode.Object:
          return args[0] is Integer || args[0] is Complex ? Ops.TRUE : Ops.FALSE;
        default: return Ops.FALSE;
      }
    }
  }
  #endregion

  #region even?
  public sealed class evenP : Primitive
  { public evenP() : base("even?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);

      object obj = args[0];
      int iv;

      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:    iv = (byte)obj; goto isint;
        case TypeCode.SByte:   iv = (sbyte)obj; goto isint;
        case TypeCode.Int16:   iv = (short)obj; goto isint;
        case TypeCode.Int32:   iv = (int)obj; goto isint;
        case TypeCode.Int64:   iv = (int)(long)obj; goto isint;
        case TypeCode.UInt16:  iv = (ushort)obj; goto isint;
        case TypeCode.UInt32:  iv = (int)(uint)obj; goto isint;
        case TypeCode.UInt64:  iv = (int)(ulong)obj; goto isint;
        case TypeCode.Decimal: iv = (int)Decimal.ToDouble((Decimal)obj); goto isint;
        case TypeCode.Double:  iv = (int)(double)obj; goto isint;
        case TypeCode.Single:  iv = (int)(float)obj; goto isint;
        case TypeCode.Object:
          if(obj is Integer)
          { Integer i = (Integer)obj;
            return i.length!=0 && (i.data[0]&1)==0;
          }
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) { iv=(int)c.real; goto isint; }
          }
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
      
      isint: return (iv&1)==0 && iv!=0 ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region exact?
  public sealed class exactP : Primitive
  { public exactP() : base("exact?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return Ops.TRUE;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return Ops.FALSE;
        case TypeCode.Object:
          if(obj is Integer) return Ops.TRUE;
          if(obj is Complex) return Ops.FALSE;
          goto default;
        default: throw Ops.TypeError(name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region exact-integer?
  public sealed class exactIntegerP : Primitive
  { public exactIntegerP() : base("exact-integer?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return Ops.TRUE;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return Ops.FALSE;
        case TypeCode.Object:
          if(obj is Integer) return Ops.TRUE;
          if(obj is Complex) return Ops.FALSE;
          goto default;
        default: throw Ops.TypeError(name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region exact->inexact
  public sealed class exactToInexact : Primitive
  { public exactToInexact() : base("exact->inexact", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: return (double)(byte)obj;
        case TypeCode.SByte: return (double)(sbyte)obj;
        case TypeCode.Int16: return (double)(short)obj;
        case TypeCode.Int32: return (double)(int)obj;
        case TypeCode.Int64: return (double)(long)obj;
        case TypeCode.UInt16: return (double)(ushort)obj;
        case TypeCode.UInt32:  return (double)(uint)obj;
        case TypeCode.UInt64: return (double)(ulong)obj;

        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return obj;

        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).ToDouble();
          goto default;
        default: throw Ops.TypeError(name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region imag-part
  public sealed class imagPart : Primitive
  { public imagPart() : base("imag-part", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).imag;
    }
  }
  #endregion

  #region inexact?
  public sealed class inexactP : Primitive
  { public inexactP() : base("inexact?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return Ops.FALSE;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return Ops.TRUE;
        case TypeCode.Object:
          if(obj is Integer) return Ops.FALSE;
          if(obj is Complex) return Ops.TRUE;
          goto default;
        default: throw Ops.TypeError(name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region inexact->exact
  public sealed class inexactToExact : Primitive
  { public inexactToExact() : base("inexact->exact", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return core(args[0]);
    }
    
    internal static object core(object obj)
    { switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64:
          return obj;
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          throw new NotImplementedException("rationals");
        case TypeCode.Object:
          if(obj is Integer) return obj;
          if(obj is Complex) throw new NotImplementedException("rationals");
          goto default;
        default: throw Ops.TypeError("inexact->exact"+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region integer?
  public sealed class integerP : Primitive
  { public integerP() : base("integer?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:   case TypeCode.SByte:
        case TypeCode.Int16:  case TypeCode.Int32:   case TypeCode.Int64:
        case TypeCode.UInt16: case TypeCode.UInt32:  case TypeCode.UInt64:
          return true;
        case TypeCode.Decimal: return Decimal.Remainder((Decimal)obj, Decimal.One)==Decimal.Zero;
        case TypeCode.Double:  return Math.IEEERemainder((double)obj, 1)==0;
        case TypeCode.Single:  return Math.IEEERemainder((float)obj, 1)==0;
        case TypeCode.Object:
          if(obj is Integer) return true;
          if(obj is Complex)
          { Complex c = (Complex)obj;
            return c.imag==0 && Math.IEEERemainder(c.real, 1)==0;
          }
          return false;
        default: return false;
      }
    }
  }
  #endregion

  #region max
  public sealed class max : Primitive
  { public max() : base("max", 1, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object max=args[0];
      for(int i=1; i<args.Length; i++) if(Ops.Compare(args[i], max)>0) max=args[i];
      return max;
    }
  }
  #endregion

  #region min
  public sealed class min : Primitive
  { public min() : base("min", 1, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object min=args[0];
      for(int i=1; i<args.Length; i++) if(Ops.Compare(args[i], min)<0) min=args[i];
      return min;
    }
  }
  #endregion

  #region negative?
  public sealed class negativeP : Primitive
  { public negativeP() : base("negative?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: case TypeCode.UInt16: case TypeCode.UInt32: case TypeCode.UInt64: return Ops.FALSE;
        case TypeCode.SByte: return (sbyte)obj<0;
        case TypeCode.Int16: return (short)obj<0;
        case TypeCode.Int32: return (int)obj<0;
        case TypeCode.Int64: return (long)obj<0;
        case TypeCode.Decimal: return (Decimal)obj<Decimal.Zero;
        case TypeCode.Double:  return (double)obj<0;
        case TypeCode.Single:  return (float)obj<0;
        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).Sign==-1;
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region number?
  public sealed class numberP : Primitive
  { public numberP() : base("number?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      switch(Convert.GetTypeCode(args[0]))
      { case TypeCode.Byte:   case TypeCode.Decimal: case TypeCode.Double:
        case TypeCode.Int16:  case TypeCode.Int32:   case TypeCode.Int64:
        case TypeCode.SByte:  case TypeCode.Single:
        case TypeCode.UInt16: case TypeCode.UInt32:  case TypeCode.UInt64:
          return Ops.TRUE;
        case TypeCode.Object:
          return args[0] is Integer || args[0] is Complex ? Ops.TRUE : Ops.FALSE;
        default: return Ops.FALSE;
      }
    }
  }
  #endregion

  #region odd?
  public sealed class oddP : Primitive
  { public oddP() : base("odd?", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);

      object obj = args[0];
      int iv;

      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:    iv = (byte)obj; goto isint;
        case TypeCode.SByte:   iv = (sbyte)obj; goto isint;
        case TypeCode.Int16:   iv = (short)obj; goto isint;
        case TypeCode.Int32:   iv = (int)obj; goto isint;
        case TypeCode.Int64:   iv = (int)(long)obj; goto isint;
        case TypeCode.UInt16:  iv = (ushort)obj; goto isint;
        case TypeCode.UInt32:  iv = (int)(uint)obj; goto isint;
        case TypeCode.UInt64:  iv = (int)(ulong)obj; goto isint;
        case TypeCode.Decimal: iv = (int)Decimal.ToDouble((Decimal)obj); goto isint;
        case TypeCode.Double:  iv = (int)(double)obj; goto isint;
        case TypeCode.Single:  iv = (int)(float)obj; goto isint;
        case TypeCode.Object:
          if(obj is Integer)
          { Integer i = (Integer)obj;
            return i.length!=0 && (i.data[0]&1)==0;
          }
          if(obj is Complex)
          { Complex c = (Complex)obj;
            if(c.imag==0) { iv=(int)c.real; goto isint; }
          }
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
      
      isint: return (iv&1)!=0 ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region zero?
  public sealed class zeroP : Primitive
  { public zeroP() : base("zero?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: return (byte)obj==0;
        case TypeCode.SByte: return (sbyte)obj==0;
        case TypeCode.Int16: return (short)obj==0;
        case TypeCode.Int32: return (int)obj==0;
        case TypeCode.Int64: return (long)obj==0;
        case TypeCode.UInt16: return (ushort)obj==0;
        case TypeCode.UInt32: return (uint)obj==0;
        case TypeCode.UInt64: return (ulong)obj==0;
        case TypeCode.Decimal: return (Decimal)obj==Decimal.Zero;
        case TypeCode.Double:  return (double)obj==0;
        case TypeCode.Single:  return (float)obj==0;
        case TypeCode.Object:
          if(obj is Integer) return (Integer)obj==Integer.Zero;
          if(obj is Complex) return (Complex)obj==Complex.Zero;
          goto default;
        default: throw Ops.TypeError(name+": expected a number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion
  
  #region positive?
  public sealed class positiveP : Primitive
  { public positiveP() : base("positive?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte: return (byte)obj!=0;
        case TypeCode.SByte: return (sbyte)obj>0;
        case TypeCode.Int16: return (short)obj>0;
        case TypeCode.Int32: return (int)obj>0;
        case TypeCode.Int64: return (long)obj>0;
        case TypeCode.UInt16: return (ushort)obj!=0;
        case TypeCode.UInt32: return (uint)obj!=0;
        case TypeCode.UInt64: return (ulong)obj!=0;
        case TypeCode.Decimal: return (Decimal)obj>Decimal.Zero;
        case TypeCode.Double:  return (double)obj>0;
        case TypeCode.Single:  return (float)obj>0;
        case TypeCode.Object:
          if(obj is Integer) return ((Integer)obj).Sign==1;
          goto default;
        default: throw Ops.TypeError(name+": expected a real number, but received "+Ops.TypeName(obj));
      }
    }
  }
  #endregion

  #region real?
  public sealed class realP : Primitive
  { public realP() : base("real?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      switch(Convert.GetTypeCode(obj))
      { case TypeCode.Byte:    case TypeCode.SByte:
        case TypeCode.Int16:   case TypeCode.Int32:  case TypeCode.Int64:
        case TypeCode.UInt16:  case TypeCode.UInt32: case TypeCode.UInt64:
        case TypeCode.Decimal: case TypeCode.Double: case TypeCode.Single:
          return true;
        case TypeCode.Object:
          if(obj is Integer) return true;
          if(obj is Complex) return ((Complex)obj).imag==0;
          return false;
        default: return false;
      }
    }
  }
  #endregion

  #region real-part
  public sealed class realPart : Primitive
  { public realPart() : base("real-part", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectComplex(args[0]).real;
    }
  }
  #endregion

  #region string->number
  public sealed class stringToNumber : Primitive
  { public stringToNumber() : base("string->number", 1, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      int radix;
      if(args.Length==1) radix = 10;
      else
      { radix = Ops.ExpectInt(args[1]);
        if(radix!=10 && radix!=16 && radix!=8 && radix!=2) throw Ops.ValueError(name+": radix must be 2, 8, 10, or 16");
      }
      return Parser.ParseNumber(Ops.ExpectString(args[0]), radix);
    }
  }
  #endregion
  #endregion

  #region Numeric operators
  #region +
  public sealed class opadd : Primitive
  { public opadd() : base("+", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Add(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Add(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region -
  public sealed class opsub : Primitive
  { public opsub() : base("-", 1, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==1) return Ops.Negate(args[0]);
      object ret = Ops.Subtract(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Subtract(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region *
  public sealed class opmul : Primitive
  { public opmul() : base("*", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Multiply(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Multiply(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region /
  public sealed class opdiv : Primitive
  { public opdiv() : base("/", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Divide(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Divide(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region //
  public sealed class opfdiv : Primitive
  { public opfdiv() : base("//", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.FloorDivide(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.FloorDivide(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region %
  public sealed class opmod : Primitive
  { public opmod() : base("%", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.Modulus(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.Modulus(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region expt
  public sealed class expt : Primitive
  { public expt() : base("expt", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.Power(args[0], args[1]);
    }
  }
  #endregion
  #region exptmod
  public sealed class exptmod : Primitive
  { public exptmod() : base("exptmod", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.PowerMod(args[0], args[1], args[2]);
    }
  }
  #endregion

  #region =
  public sealed class opeq : Primitive
  { public opeq() : base("=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])!=0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region !=
  public sealed class opne : Primitive
  { public opne() : base("!=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])==0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region <
  public sealed class oplt : Primitive
  { public oplt() : base("<", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])>=0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region <=
  public sealed class ople : Primitive
  { public ople() : base("<=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])>0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region >
  public sealed class opgt : Primitive
  { public opgt() : base(">", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])<=0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion
  #region >=
  public sealed class opge : Primitive
  { public opge() : base(">=", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      for(int i=0; i<args.Length-1; i++) if(Ops.Compare(args[i], args[i+1])<0) return Ops.FALSE;
      return Ops.TRUE;
    }
  }
  #endregion

  #region bitand
  public sealed class bitand : Primitive
  { public bitand() : base("bitand", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.BitwiseAnd(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.BitwiseAnd(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region bitor
  public sealed class bitor : Primitive
  { public bitor() : base("bitor", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.BitwiseOr(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.BitwiseOr(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region bitxor
  public sealed class bitxor : Primitive
  { public bitxor() : base("bitxor", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object ret = Ops.BitwiseXor(args[0], args[1]);
      for(int i=2; i<args.Length; i++) ret = Ops.BitwiseXor(ret, args[i]);
      return ret;
    }
  }
  #endregion
  #region bitnot
  public sealed class bitnot : Primitive
  { public bitnot() : base("bitnot", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.BitwiseNegate(args[0]);
    }
  }
  #endregion
  #region lshift
  public sealed class lshift : Primitive
  { public lshift() : base("lshift", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.LeftShift(args[0], args[1]);
    }
  }
  #endregion
  #region rshift
  public sealed class rshift : Primitive
  { public rshift() : base("rshift", 2, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.RightShift(args[0], args[1]);
    }
  }
  #endregion
  #endregion
  
  #region Pair functions
  #region car
  public sealed class car : Primitive
  { public car() : base("car", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Car;
    }
  }
  #endregion

  #region cdr
  public sealed class cdr : Primitive
  { public cdr() : base("cdr", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Cdr;
    }
  }
  #endregion

  #region cons
  public sealed class cons : Primitive
  { public cons() : base("cons", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return new Pair(args[0], args[1]);
    }
  }
  #endregion
  #region pair?
  public sealed class pairP : Primitive
  { public pairP() : base("pair?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is Pair ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region set-car!
  public sealed class setCarN : Primitive
  { public setCarN() : base("set-car!", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Car = args[1];
    }
  }
  #endregion

  #region set-cdr!
  public sealed class setCdrN : Primitive
  { public setCdrN() : base("set-cdr!", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPair(args[0]).Cdr = args[1];
    }
  }
  #endregion
  #endregion

  #region Procedure functions
  #region apply
  public sealed class apply : Primitive
  { public apply() : base("apply", 2, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);

      int  alen = args.Length-2;
      Pair pair = Ops.ExpectPair(args[alen+1]);
      object[] nargs = new object[Builtins.length.core(pair) + alen];
      if(alen!=0) Array.Copy(args, 1, nargs, 0, alen);

      do
      { nargs[alen++] = pair.Car;
        pair = pair.Cdr as Pair;
      } while(pair!=null);

      return Ops.Call(args[0], nargs);
    }
  }
  #endregion
  
  [SymbolName("call-with-current-continuation")]
  public void callCC(IProcedure proc)
  { throw new NotImplementedException("sorry, manipulable continuations aren't implemented yet");
  }

  #region call-with-values
  public sealed class callWithValues : Primitive
  { public callWithValues() : base("call-with-values", 2, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure thunk=Ops.ExpectProcedure(args[0]), func=Ops.ExpectProcedure(args[1]);
      object ret = thunk.Call(Ops.EmptyArray);
      MultipleValues mv = ret as MultipleValues;
      return mv==null ? func.Call(ret) : func.Call(func.NeedsFreshArgs ? (object[])mv.Values.Clone() : mv.Values);
    }
  }
  #endregion

  [SymbolName("compiled-procedure?")]
  public static object compiledProcedureP(object obj) { return Ops.FromBool(obj is IProcedure); }

  [SymbolName("compound-procedure?")]
  public static object compoundProcedureP(object obj) { return Ops.FALSE; }

  #region dynamic-wind
  public sealed class dynamicWind : Primitive
  { public dynamicWind() : base("dynamic-wind", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure before=Ops.ExpectProcedure(args[0]), thunk=Ops.ExpectProcedure(args[1]),
                  after=Ops.ExpectProcedure(args[2]);
      try
      { before.Call(Ops.EmptyArray);
        return thunk.Call(Ops.EmptyArray);
      }
      finally { after.Call(Ops.EmptyArray); }
    }
  }
  #endregion

  #region primitive-procedure?
  public sealed class primitiveProcedureP : Primitive
  { public primitiveProcedureP() : base("primitive-procedure?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      object func = args[0];
      return Ops.FromBool(!(func is Closure) && func is IProcedure);
    }
  }
  #endregion

  #region procedure?
  public sealed class procedureP : Primitive
  { public procedureP() : base("procedure?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.FromBool(args[0] is IProcedure);
    }
  }
  #endregion

  #region procedure-arity-valid?
  public sealed class procedureArityValidP : Primitive
  { public procedureArityValidP() : base("procedure-arity-valid?", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure proc = Ops.ExpectProcedure(args[0]);
      int min=proc.MinArgs, max=proc.MaxArgs;
      return Ops.FromBool(max==-1 ? args.Length>=min : args.Length>=min && args.Length<=max);
    }
  }
  #endregion

  #region procedure-arity
  public sealed class procedureArity : Primitive
  { public procedureArity() : base("procedure-arity", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure proc = Ops.ExpectProcedure(args[0]);
      int max = proc.MaxArgs;
      return new Pair(proc.MinArgs, max==-1 ? Ops.FALSE : max);
    }
  }
  #endregion
  #endregion
  
  #region Promise functions
  #region #%delay
  public sealed class _delay : Primitive
  { public _delay() : base("#%delay", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return new Promise(Ops.ExpectProcedure(args[0]));
    }
  }
  #endregion
  
  #region force
  public sealed class force : Primitive
  { public force() : base("force", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Promise p = Ops.ExpectPromise(args[0]);
      if(p.Form!=null)
      { p.Value = p.Form.Call(Ops.EmptyArray);
        p.Form  = null;
      }
      return p.Value;
    }
  }
  #endregion
  
  #region promise?
  public sealed class promiseP : Primitive
  { public promiseP() : base("promise?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is Promise;
    }
  }
  #endregion
  
  #region promise-forced?
  public sealed class promiseForced : Primitive
  { public promiseForced() : base("promise-forced?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectPromise(args[0]).Form==null ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion
  
  #region promise-value
  public sealed class promiseValue : Primitive
  { public promiseValue() : base("promise-value", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Promise p = Ops.ExpectPromise(args[0]);
      if(p.Form!=null) throw Ops.ValueError(name+": the promise has not be forced yet");
      return p.Value;
    }
  }
  #endregion
  #endregion

  #region Symbol functions
  #region gensym
  public sealed class gensym : Primitive
  { public gensym() : base("gensym", 0, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return new Symbol((args.Length==0 ? "#<g" : "#<"+Ops.ExpectString(args[0])) + gensyms.Next + ">");
    }
  }
  #endregion

  #region string->symbol
  public sealed class stringToSymbol : Primitive
  { public stringToSymbol() : base("string->symbol", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Symbol.Get(Ops.ExpectString(args[0]));
    }
  }
  #endregion

  #region string->uninterned-symbol
  public sealed class stringToUninternedSymbol : Primitive
  { public stringToUninternedSymbol() : base("string->uninterned-symbol", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return new Symbol(Ops.ExpectString(args[0]));
    }
  }
  #endregion

  #region symbol?
  public sealed class symbolP : Primitive
  { public symbolP() : base("symbol?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is Symbol ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region symbol-hash
  public sealed class symbolHash : Primitive
  { public symbolHash() : base("symbol-hash", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectSymbol(args[0]).GetHashCode();
    }
  }
  #endregion
  #region symbol-hash-mod
  public sealed class symbolHashMod : Primitive
  { public symbolHashMod() : base("symbol-hash-mod", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectSymbol(args[0]).GetHashCode() % Ops.ExpectInt(args[1]);
    }
  }
  #endregion
  #endregion

  // TODO: string-search-chars (and related)
  #region String functions
  #region list->string
  public sealed class listToString : Primitive
  { public listToString() : base("list->string", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args[0]==null) return "";

      Pair pair = Ops.ExpectPair(args[0]);
      System.Text.StringBuilder sb = new System.Text.StringBuilder();

      try { while(pair!=null) { sb.Append((char)pair.Car); pair=pair.Cdr as Pair; } }
      catch(InvalidCastException) { throw Ops.TypeError(name+": expects a list of characters"); }
      return sb.ToString();
    }
  }
  #endregion
  #region string->list
  public sealed class stringToList : Primitive
  { public stringToList() : base("string->list", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      string str = Ops.ExpectString(args[0]);
      Pair head=null, tail=null;
      for(int i=0; i<str.Length; i++)
      { Pair next = new Pair(str[i], null);
        if(head==null) head=tail=next;
        else { tail.Cdr=next; tail=next; }
      }
      return head;
    }
  }
  #endregion

  #region make-string
  public sealed class makeString : Primitive
  { public makeString() : base("make-string", 1, 2) { }
    
    public override object Call(object[] args)
    { CheckArity(args);
      return new string(args.Length==2 ? Ops.ExpectChar(args[1]) : '\0', Ops.ExpectInt(args[0]));
    }
  }
  #endregion

  #region string
  public sealed class @string : Primitive
  { public @string() : base("string", 0, -1) { }

    public override object Call(object[] args)
    { char[] chars = new char[args.Length];
      try { for(int i=0; i<args.Length; i++) chars[i] = (char)args[i]; }
      catch(InvalidCastException) { throw Ops.TypeError(name+": expects character arguments"); }
      return new string(chars);
    }
  }
  #endregion

  #region string?
  public sealed class stringP : Primitive
  { public stringP() : base("string?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is string ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region string=?
  public sealed class stringEqP : Primitive
  { public stringEqP() : base("string=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0])==Ops.ExpectString(args[1]);
    }
  }
  #endregion
  #region string!=?
  public sealed class stringNeP : Primitive
  { public stringNeP() : base("string!=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0])!=Ops.ExpectString(args[1]);
    }
  }
  #endregion
  #region string<?
  public sealed class stringLtP : Primitive
  { public stringLtP() : base("string<?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1])) < 0;
    }
  }
  #endregion
  #region string<=?
  public sealed class stringLeP : Primitive
  { public stringLeP() : base("string<=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1])) <= 0;
    }
  }
  #endregion
  #region string>?
  public sealed class stringGtP : Primitive
  { public stringGtP() : base("string>?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1])) > 0;
    }
  }
  #endregion
  #region string>=?
  public sealed class stringGeP : Primitive
  { public stringGeP() : base("string>=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1])) >= 0;
    }
  }
  #endregion

  #region string-ci=?
  public sealed class stringCiEqP : Primitive
  { public stringCiEqP() : base("string-ci=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), true) == 0;
    }
  }
  #endregion
  #region string-ci!=?
  public sealed class stringCiNeP : Primitive
  { public stringCiNeP() : base("string-ci!=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), true) != 0;
    }
  }
  #endregion
  #region string-ci<?
  public sealed class stringCiLtP : Primitive
  { public stringCiLtP() : base("string-ci<?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), true) < 0;
    }
  }
  #endregion
  #region string-ci<=?
  public sealed class stringCiLeP : Primitive
  { public stringCiLeP() : base("string-ci<=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), true) <= 0;
    }
  }
  #endregion
  #region string-ci>?
  public sealed class stringCiGtP : Primitive
  { public stringCiGtP() : base("string-ci>?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), true) > 0;
    }
  }
  #endregion
  #region string-ci>=?
  public sealed class stringCiGeP : Primitive
  { public stringCiGeP() : base("string-ci>=?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), true) >= 0;
    }
  }
  #endregion

  #region string-append
  public sealed class stringAppend : Primitive
  { public stringAppend() : base("string-append", 0, -1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      switch(args.Length)
      { case 0: return "";
        case 1: return Ops.ExpectString(args[0]);
        case 2: return string.Concat(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]));
        case 3: return string.Concat(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), Ops.ExpectString(args[2]));
        case 4: return string.Concat(Ops.ExpectString(args[0]), Ops.ExpectString(args[1]), Ops.ExpectString(args[2]),
                                     Ops.ExpectString(args[3]));
        default:
          System.Text.StringBuilder sb = new System.Text.StringBuilder();
          for(int i=0; i<args.Length; i++) sb.Append(Ops.ExpectString(args[i]));
          return sb.ToString();
      }
    }
  }
  #endregion

  #region string-compare
  public sealed class stringCompare : Primitive
  { public stringCompare() : base("string-compare", 2, 7) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      string str1, str2;
      int start1, start2, len;

      switch(args.Length)
      { case 2: case 3:
          str1=Ops.ExpectString(args[0]); str2=Ops.ExpectString(args[1]);
          start1=start2=0; len=Math.Min(str1.Length, str2.Length);
          break;
        case 6: case 7:
          str1   = Ops.ExpectString(args[0]); str2   = Ops.ExpectString(args[3]);
          start1 = Ops.ExpectInt(args[1]);    start2 = Ops.ExpectInt(args[4]);
          len = Math.Min(Ops.ExpectInt(args[2]), Ops.ExpectInt(args[5]));
          if(len<0 || start1<0 || start2<0 || len+start1>str1.Length || len+start2>str2.Length)
            throw new ArgumentException(name+": start+length exceeded one of the string parameters, "+
                                        "or a parameter was negative");
          break;
        default: throw new ArgumentException(name+": expects either 2, 3, 6, or 7 arguments, but received "+
                                             args.Length.ToString());
      }

      return string.Compare(str1, start1, str2, start2, len, (args.Length&1)!=0 && Ops.IsTrue(args[args.Length-1]));
    }
  }
  #endregion

  #region string-downcase
  public sealed class stringDowncase : Primitive
  { public stringDowncase() : base("string-downcase", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).ToLower();
    }
  }
  #endregion
  #region string-upcase
  public sealed class stringUpcase : Primitive
  { public stringUpcase() : base("string-upcase", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).ToUpper();
    }
  }
  #endregion

  #region string-hash
  public sealed class stringHash : Primitive
  { public stringHash() : base("string-hash", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).GetHashCode();
    }
  }
  #endregion
  #region string-hash-mod
  public sealed class stringHashMod : Primitive
  { public stringHashMod() : base("string-hash-mod", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).GetHashCode() % Ops.ExpectInt(args[1]);
    }
  }
  #endregion

  #region string-head
  public sealed class stringHead : Primitive
  { public stringHead() : base("string-head", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).Substring(Ops.ExpectInt(args[1]));
    }
  }
  #endregion
  #region string-tail
  public sealed class stringTail : Primitive
  { public stringTail() : base("string-tail", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      string str = Ops.ExpectString(args[0]);
      int length = Ops.ExpectInt(args[1]);
      return str.Substring(str.Length-length, length);
    }
  }
  #endregion

  #region string-length
  public sealed class stringLength : Primitive
  { public stringLength() : base("string-length", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).Length;
    }
  }
  #endregion

  #region string-match
  public sealed class stringMatch : Primitive
  { public stringMatch() : base("string-match", 2, 6) { }

    public override object Call(object[] args)
    { string str1, str2;
      int pos, len, start1, start2;
      handleArgs(name, args, out str1, out str2, out len, out start1, out start2);
      for(pos=0; pos<len; pos++) if(str1[pos+start1] != str2[pos+start2]) break;
      return pos;
    }

    internal static void handleArgs(string name, object[] args, out string str1, out string str2, out int len,
                                    out int start1, out int start2)
    { switch(args.Length)
      { case 2:
          str1=Ops.ExpectString(args[0]); str2=Ops.ExpectString(args[1]);
          start1=start2=0; len=Math.Min(str1.Length, str2.Length);
          break;
        case 6:
          str1   = Ops.ExpectString(args[0]); str2   = Ops.ExpectString(args[3]);
          start1 = Ops.ExpectInt(args[1]);    start2 = Ops.ExpectInt(args[4]);
          len = Math.Min(Ops.ExpectInt(args[2]), Ops.ExpectInt(args[5]));
          if(len<0 || start1<0 || start2<0 || len+start1>str1.Length || len+start2>str2.Length)
            throw new ArgumentException(name+": start+length exceeded one of the string parameters, "+
                                        "or a parameter was negative");
          break;
        default: throw new ArgumentException(name+": expects either 2 or 6 arguments, but received "+args.Length);
      }
    }
  }
  #endregion
  #region string-match-ci
  public sealed class stringMatchCi : Primitive
  { public stringMatchCi() : base("string-match-ci", 2, 6) { }

    public override object Call(object[] args)
    { string str1, str2;
      int pos, len, start1, start2;
      stringMatch.handleArgs(name, args, out str1, out str2, out len, out start1, out start2);
      for(pos=0; pos<len; pos++) if(char.ToLower(str1[pos+start1]) != char.ToLower(str2[pos+start2])) break;
      return pos;
    }
  }
  #endregion
  #region string-match-backward
  public sealed class stringMatchBackward : Primitive
  { public stringMatchBackward() : base("string-match-backward", 2, 6) { }

    public override object Call(object[] args)
    { string str1, str2;
      int pos, len, start1, start2;
      handleArgs(name, args, out str1, out str2, out len, out start1, out start2);
      for(pos=0; pos<len; pos++) if(str1[start1-pos] != str2[start2-pos]) break;
      return pos;
    }

    internal static void handleArgs(string name, object[] args, out string str1, out string str2, out int len,
                                    out int start1, out int start2)
    { switch(args.Length)
      { case 2:
          str1=Ops.ExpectString(args[0]); str2=Ops.ExpectString(args[1]);
          len=Math.Min(str1.Length, str2.Length); start1=str1.Length-1; start2=str2.Length-1;
          break;
        case 6:
          { int len1=Ops.ExpectInt(args[2]), len2=Ops.ExpectInt(args[5]);
            str1=Ops.ExpectString(args[0]); str2=Ops.ExpectString(args[3]);
            start1=Ops.ExpectInt(args[1])+len1-1; start2=Ops.ExpectInt(args[4])+len2-1;
            len = Math.Min(len1, len2);
            if(len<0 || start1>=str1.Length || start2>=str2.Length)
              throw new ArgumentException(name+": start+length exceeded one of the string parameters, "+
                                          "or a parameter was negative");
          }
          break;
        default: throw new ArgumentException(name+": expects either 2 or 6 arguments, but received "+args.Length);
      }
    }
  }
  #endregion
  #region string-match-backward-ci
  public sealed class stringMatchBackwardCi : Primitive
  { public stringMatchBackwardCi() : base("string-match-backward-ci", 2, 6) { }

    public override object Call(object[] args)
    { string str1, str2;
      int pos, len, start1, start2;
      stringMatchBackward.handleArgs(name, args, out str1, out str2, out len, out start1, out start2);
      for(pos=0; pos<len; pos++) if(char.ToLower(str1[start1-pos]) != char.ToLower(str2[start2-pos])) break;
      return pos;
    }
  }
  #endregion

  #region string-null?
  public sealed class stringNullP : Primitive
  { public stringNullP() : base("string-null?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.FromBool(Ops.ExpectString(args[0]).Length==0);
    }
  }
  #endregion

  #region string-pad-left
  public sealed class stringPadLeft : Primitive
  { public stringPadLeft() : base("string-pad-left", 2, 3) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).PadLeft(Ops.ExpectInt(args[1]), args.Length==3 ? Ops.ExpectChar(args[2]) : ' ');
    }
  }
  #endregion
  #region string-pad-right
  public sealed class stringPadRight : Primitive
  { public stringPadRight() : base("string-pad-right", 2, 3) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0]).PadRight(Ops.ExpectInt(args[1]), args.Length==3 ? Ops.ExpectChar(args[2]) : ' ');
    }
  }
  #endregion

  #region string-prefix?
  public sealed class stringPrefixP : Primitive
  { public stringPrefixP() : base("string-prefix?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.FromBool(Ops.ExpectString(args[1]).StartsWith(Ops.ExpectString(args[0])));
    }
  }
  #endregion
  #region string-suffix?
  public sealed class stringSuffixP : Primitive
  { public stringSuffixP() : base("string-suffix?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.FromBool(Ops.ExpectString(args[1]).EndsWith(Ops.ExpectString(args[0])));
    }
  }
  #endregion

  #region string-replace
  public sealed class stringReplace : Primitive
  { public stringReplace() : base("string-replace", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);

      string str = Ops.ExpectString(args[0]);
      string search = args[1] as string;
      return search!=null ? str.Replace(search, Ops.ExpectString(args[2]))
                          : str.Replace(Ops.ExpectChar(args[1]), Ops.ExpectChar(args[2]));
    }
  }
  #endregion

  #region string-reverse
  public sealed class stringReverse : Primitive
  { public stringReverse() : base("string-reverse", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      string str = Ops.ExpectString(args[0]);
      if(str.Length==0) return str;
      unsafe
      { char* dest = stackalloc char[str.Length];
        fixed(char* src=str)
        { int end=str.Length-1, hlen=(str.Length+1)/2;
          for(int i=0; i<hlen; i++)
          { int ei=end-i;
            dest[i]  = src[ei];
            dest[ei] = src[i];
          }
        }
        return new string(dest, 0, str.Length);
      }
    }
  }
  #endregion
  
  #region string-search
  public sealed class stringSearch : Primitive
  { public stringSearch() : base("string-search", 2, 4) { }
    public override object Call(object[] args)
    { CheckArity(args);
      string haystack=Ops.ExpectString(args[1]), needle=args[0] as string;
      int start  = args.Length==2 ? 0 : Ops.ExpectInt(args[2]);
      int length = args.Length==4 ? Ops.ExpectInt(args[3]) : haystack.Length-start;
      int index  = needle==null ? haystack.IndexOf(Ops.ExpectChar(args[0]), start, length)
                               : haystack.IndexOf(needle, start, length);
      return index==-1 ? Ops.FALSE : (object)index;
    }
  }
  #endregion
  #region string-search-all
  public sealed class stringSearchAll : Primitive
  { public stringSearchAll() : base("string-search-all", 2, 4) { }
    public override object Call(object[] args)
    { CheckArity(args);
      string haystack=Ops.ExpectString(args[1]), needle=args[0] as string;
      Pair head=null, tail=null;
      int index;
      int pos = args.Length==2 ? 0 : Ops.ExpectInt(args[2]);
      int end = args.Length==4 ? Ops.ExpectInt(args[3])+pos : haystack.Length;
      if(needle!=null)
        while(pos<end)
        { index = haystack.IndexOf(needle, pos);
          if(index==-1) break;
          Pair next = new Pair(index, null);
          if(head==null) head=tail=next;
          else { tail.Cdr=next; tail=next; }
          pos = index+needle.Length;
        }
      else
      { char c = Ops.ExpectChar(args[0]);
        while(pos<end)
        { index = haystack.IndexOf(c, pos);
          if(index==-1) break;
          Pair next = new Pair(index, null);
          if(head==null) head=tail=next;
          else { tail.Cdr=next; tail=next; }
          pos = index+1;
        }
      }
      return head;
    }
  }
  #endregion
  #region string-search-backward
  public sealed class stringSearchBackward : Primitive
  { public stringSearchBackward() : base("string-search-backward", 2, 4) { }
    public override object Call(object[] args)
    { CheckArity(args);
      string haystack=Ops.ExpectString(args[1]), needle=args[0] as string;
      int start  = args.Length==2 ? 0 : Ops.ExpectInt(args[2]);
      int length = args.Length==4 ? Ops.ExpectInt(args[3]) : haystack.Length-start;
      int index  = needle==null ? haystack.LastIndexOf(Ops.ExpectChar(args[0]), start, length)
                                : haystack.LastIndexOf(needle, start, length);
      return index==-1 ? Ops.FALSE : (object)index;
    }
  }
  #endregion

  [SymbolName("string-trim")]
  public static string stringTrim(string str, params char[] chars)
  { return chars.Length==0 ? str.Trim() : str.Trim(chars);
  }
  [SymbolName("string-trim-left")]
  public static string stringTrimLeft(string str, params char[] chars)
  { return str.TrimStart(chars.Length==0 ? null : chars);
  }
  [SymbolName("string-trim-right")]
  public static string stringTrimRight(string str, params char[] chars)
  { return str.TrimEnd(chars.Length==0 ? null : chars);
  }

  #region string-ref
  public sealed class stringRef : Primitive
  { public stringRef() : base("string-ref", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectString(args[0])[Ops.ExpectInt(args[1])];
    }
  }
  #endregion
  
  #region substring
  public sealed class substring : Primitive
  { public substring() : base("substring", 2, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      string str = Ops.ExpectString(args[0]);
      int  start = Ops.ExpectInt(args[1]);
      return args.Length==2 ? str.Substring(start) : str.Substring(start, Ops.ExpectInt(args[2]));
    }
  }
  #endregion

  #region substring?
  public sealed class substringP : Primitive
  { public substringP() : base("substring?", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.FromBool(Ops.ExpectString(args[1]).IndexOf(Ops.ExpectString(args[0])) != -1);
    }
  }
  #endregion

  #region substring=?
  public sealed class substringEqP : Primitive
  { public substringEqP() : base("substring=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4])) == 0;
    }
  }
  #endregion
  #region substring!=?
  public sealed class substringNeP : Primitive
  { public substringNeP() : base("substring!=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4])) != 0;
    }
  }
  #endregion
  #region substring<?
  public sealed class substringLtP : Primitive
  { public substringLtP() : base("substring<?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4])) < 0;
    }
  }
  #endregion
  #region substring<=?
  public sealed class substringLeP : Primitive
  { public substringLeP() : base("substring<=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4])) <= 0;
    }
  }
  #endregion
  #region substring>?
  public sealed class substringGtP : Primitive
  { public substringGtP() : base("substring>?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4])) > 0;
    }
  }
  #endregion
  #region substring>=?
  public sealed class substringGeP : Primitive
  { public substringGeP() : base("substring>=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4])) >= 0;
    }
  }
  #endregion

  #region substring-ci=?
  public sealed class substringCiEqP : Primitive
  { public substringCiEqP() : base("substring-ci=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4]), true) == 0;
    }
  }
  #endregion
  #region substring-ci!=?
  public sealed class substringCiNeP : Primitive
  { public substringCiNeP() : base("substring-ci!=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4]), true) != 0;
    }
  }
  #endregion
  #region substring-ci<?
  public sealed class substringCiLtP : Primitive
  { public substringCiLtP() : base("substring-ci<?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4]), true) < 0;
    }
  }
  #endregion
  #region substring-ci<=?
  public sealed class substringCiLeP : Primitive
  { public substringCiLeP() : base("substring-ci<=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4]), true) <= 0;
    }
  }
  #endregion
  #region substring-ci>?
  public sealed class substringCiGtP : Primitive
  { public substringCiGtP() : base("substring-ci>?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4]), true) > 0;
    }
  }
  #endregion
  #region substring-ci>=?
  public sealed class substringCiGeP : Primitive
  { public substringCiGeP() : base("substring-ci>=?", 5, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return string.Compare(Ops.ExpectString(args[0]), Ops.ToInt(args[1]), Ops.ExpectString(args[2]),
                            Ops.ExpectInt(args[3]),    Ops.ExpectInt(args[4]), true) >= 0;
    }
  }
  #endregion

  #region symbol->string
  public sealed class symbolToString : Primitive
  { public symbolToString() : base("symbol->string", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectSymbol(args[0]).Name;
    }
  }
  #endregion
  #endregion
  
  #region Vector functions
  #region list->vector
  public sealed class listToVector : Primitive
  { public listToVector() : base("list->vector", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ListToArray(Ops.ExpectList(args[0]));
    }
  }
  #endregion

  #region make-vector
  public sealed class makeVector : Primitive
  { public makeVector() : base("make-vector", 1, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object[] ret = new object[Ops.ExpectInt(args[0])];
      if(args.Length==2)
      { object fill = args[1];
        for(int i=0; i<ret.Length; i++) ret[i]=fill;
      }
      return ret;
    }
  }
  #endregion
  
  #region subvector
  public sealed class subvector : Primitive
  { public subvector() : base("subvector", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object[] ret, vec = Ops.ExpectVector(args[0]);
      int start=Ops.ExpectInt(args[1]), length=Ops.ExpectInt(args[2]);
      if(start<0 || length<0 || start+length>=vec.Length) throw Ops.ValueError(name+": start or length out of bounds");
      ret = new object[length];
      Array.Copy(vec, start, ret, 0, length);
      return ret;
    }
  }
  #endregion
  
  #region vector
  public sealed class vector : Primitive
  { public vector() : base("vector", 0, -1) { }
    public override object Call(object[] args) { return args; }
  }
  #endregion
  
  #region vector?
  public sealed class vectorP : Primitive
  { public vectorP() : base("vector?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0] is object[] ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region vector-copy
  public sealed class vectorCopy : Primitive
  { public vectorCopy() : base("vector-copy", 1, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object[] ret, src=Ops.ExpectVector(args[0]);
      if(args.Length==2)
      { ret = new object[Ops.ToInt(args[1])];
        Array.Copy(src, ret, Math.Min(ret.Length, src.Length));
      }
      else ret=(object[])src.Clone();
      return ret;
    }
  }
  #endregion
  
  #region vector-fill!
  public sealed class vectorFillN : Primitive
  { public vectorFillN() : base("vector-fill!", 2, 4) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object[] vec=Ops.ExpectVector(args[0]);
      object  fill=args[1];

      int start, length;
      if(args.Length==2) { start=0; length=vec.Length; }
      else
      { if(args.Length==3) { start=Ops.ExpectInt(args[2]); length=vec.Length-start; }
        else { start=Ops.ExpectInt(args[2]); length=Ops.ExpectInt(args[3]); }
        if(start<0 || length<0 || start+length>=vec.Length)
          throw Ops.ValueError(name+": start or length out of bounds");
      }

      for(int end=start+length; start<end; start++); vec[start]=fill;
      return vec;
    }
  }
  #endregion

  #region vector-length
  public sealed class vectorLength : Primitive
  { public vectorLength() : base("vector-length", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectVector(args[0]).Length;
    }
  }
  #endregion

  #region vector-map
  public sealed class vectorMap : Primitive
  { public vectorMap() : base("vector-map", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure func = Ops.ExpectProcedure(args[0]);
      object[] vector=Ops.ExpectVector(args[1]), ret=new object[vector.Length];
      bool realloc = func.NeedsFreshArgs;
      if(!realloc) args = new object[1];
      for(int i=0; i<vector.Length; i++)
      { if(realloc) args = new object[1];
        args[0] = vector[i];
        ret[i] = func.Call(args);
      }
      return ret;
    }
  }
  #endregion

  #region vector-ref
  public sealed class vectorRef : Primitive
  { public vectorRef() : base("vector-ref", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectVector(args[0])[Ops.ExpectInt(args[1])];
    }
  }
  #endregion

  #region vector-set!
  public sealed class vectorSetN : Primitive
  { public vectorSetN() : base("vector-set!", 3, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.ExpectVector(args[0])[Ops.ExpectInt(args[1])]=args[2];
    }
  }
  #endregion
  
  #region vector-sort!
  public sealed class vectorSortN : Primitive
  { public vectorSortN() : base("vector-sort!", 1, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object[] vec = Ops.ExpectVector(args[0]);
      Array.Sort(vec, args.Length==2 ? new LispComparer(Ops.ExpectProcedure(args[1])) : LispComparer.Default);
      return vec;
    }
  }
  #endregion
  #endregion

  #region eq?
  public sealed class eqP : Primitive
  { public eqP() : base("eq?", 2, 2) { }

    public override object Call(object[] args)
    { CheckArity(args);
      return args[0]==args[1] ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region eqv?
  public sealed class eqvP : Primitive
  { public eqvP() : base("eqv?", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.EqvP(args[0], args[1]) ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion
  
  #region equal?
  public sealed class equalP : Primitive
  { public equalP() : base("equal?", 2, 2) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return Ops.EqualP(args[0], args[1]) ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  // TODO: scheme-report-environment, null-environment, interaction-environment
  #region Evaluation / compilation
  public static Snippet compile(object obj) { return Ops.CompileRaw(Ops.Call("expand", obj)); }
  // TODO: consider not always compiling... perhaps simple expressions can be interpreted
  #region eval
  public sealed class eval : Primitive
  { public eval() : base("eval", 1, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Snippet snip = args[0] as Snippet;
      if(args.Length==1)
      { if(snip==null) snip = compile(args[0]);
        return snip.Run(null);
      }
      else
      { TopLevel top=args[1] as TopLevel, old=TopLevel.Current;
        if(top==null) throw Ops.TypeError(name+": expected environment, but received "+Ops.TypeName(args[1]));
        try
        { TopLevel.Current = top;
          if(snip==null) snip = compile(args[0]);
          return snip.Run(null);
        }
        finally { TopLevel.Current = old; }
      }
    }

    public static object core(object obj)
    { Snippet snip = obj as Snippet;
      if(snip==null) snip = compile(obj);
      return snip.Run(null);
    }
  }
  #endregion
  #endregion

  #region #%strip-debug
  public sealed class _stripDebug : Primitive
  { public _stripDebug() : base("#%strip-debug", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      object obj = args[0];
      if(Options.Debug)
      { Pair pair = obj as Pair;
        if(pair!=null)
        { Symbol sym = pair.Car as Symbol;
          if(sym!=null && sym.Name=="#%mark-position") obj = Ops.FastCadr(pair);
        }
      }
      return obj;
    }
  }
  #endregion

  public static void error(params object[] objs) // TODO: use a macro to provide source information
  { System.Text.StringBuilder sb = new System.Text.StringBuilder();
    foreach(object o in objs) sb.Append(Ops.Str(o));
    throw new RuntimeException(sb.ToString());
  }
  
  [SymbolName("#%import-module")]
  public static void _importModule(object module)
  { Importer.GetModule(module).ImportAll(TopLevel.Current);
  }

  #region not
  public sealed class not : Primitive
  { public not() : base("not", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return !Ops.IsTrue(args[0]) ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region null?
  public sealed class nullP : Primitive
  { public nullP() : base("null?", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0]==null ? Ops.TRUE : Ops.FALSE;
    }
  }
  #endregion

  #region values
  public sealed class values : Primitive
  { public values() : base("values", 1, -1) { needsFreshArgs=true; }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args.Length==1 ? args[0] : new MultipleValues(args);
    }
  }
  #endregion

  static Hashtable dotFuncs=new Hashtable(), dotPgets=new Hashtable(), dotPsets=new Hashtable(),
                   dotFields=new Hashtable(), dotNews=new Hashtable();
  static Index gensyms = new Index();

  public static readonly Module Instance = ModuleGenerator.Generate(typeof(Builtins), true);
}

} // namespace NetLisp.Backend