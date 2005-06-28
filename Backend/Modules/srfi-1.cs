using System;
using NetLisp.Backend;

namespace NetLisp.Mods
{

[LispCode(@"
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

(define (circular-list val1 . vals)
  (let ((ans (cons val1 vals)))
    (set-cdr! (last-pair ans) ans)
    ans))

(define (concatenate lists) (apply append lists))
(define (concatenate! lists) (apply append! lists))

(define (drop-right lis k)
  (let recur ((lag lis) (lead (drop lis k)))
    (if (pair? lead)
        (cons (car lag) (recur (cdr lag) (cdr lead)))
        nil)))

(define (drop-right! lis k)
  (check-arg integer? k drop-right!)
  (let ((lead (drop lis k)))
    (if (pair? lead)
        (let lp ((lag lis)  (lead (cdr lead)))
          (if (pair? lead)
              (lp (cdr lag) (cdr lead))
              (begin (set-cdr! lag '())
                     lis)))
        '())))

(define map-in-order map)

(define (not-pair? obj) (not (pair? obj)))

(define (null-list? l)
  (cond ((pair? l) #f)
        ((null? l) #t)
        (else (error ""null-list?: argument out of domain"" l))))

(define (list-tabulate len proc)
  (do ((i (- len 1) (- i 1))
       (ans '() (cons (proc i) ans)))
      ((< i 0) ans)))

(define (proper-list? obj) (or (null? obj) (list? obj)))

(define (reduce f ridentity lis)
  (if (null-list? lis) ridentity
      (fold f (car lis) (cdr lis))))

(define (reduce-right f ridentity lis)
  (if (null-list? lis) ridentity
      (fold-right f (car lis) (cdr lis))))

(define (split-at x k) (values (take x k) (drop x k))

(define (split-at! x k)
  (if (zero? k) (values nil x)
      (let* ((prev (drop x (- k 1)))
             (suffix (cdr prev)))
        (set-cdr! prev nil)
        (values x suffix))))

(define (take-right lis k)
  (let lp ((lag lis)  (lead (drop lis k)))
    (if (pair? lead)
        (lp (cdr lag) (cdr lead))
        lag)))

(define (unzip1 lis) (map car lis))

(define (unzip2 lis)
  (let recur ((lis lis))
    (if (null-list? lis) (values lis lis)
        (let ((elt (car lis)))
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

(define (xcons a b) (cons b a))

(define (zip list1 . more-lists) (apply map list list1 more-lists))
")]
public sealed class Srfi1
{ 
  append-map
  append-map!

  #region car+cdr
  public sealed class carAndCdr : Primitive
  { public carAndCdr() : base("car+cdr", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair pair = Ops.ExpectPair(obj);
      return new MultipleValues(pair.Car, pair.Cdr);
    }
  }
  #endregion
  
  #region circular-list?
  public sealed class circularListP : Primitive
  { public circularListP() : base("circular-list?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair pair = args[0] as Pair;
      return pair==null ? Ops.FALSE : Ops.FromBool(core(pair));
    }

    internal static bool core(Pair slow)
    { if(slow==null) return false;
      
      Pair fast = slow.Cdr as Pair;
      if(fast==null) return false;

      while(true)
      { if(slow==fast) return true;
        slow = (Pair)slow.Cdr;
        fast = fast.Cdr as Pair;
        if(fast==null) return false;
        fast = fast.Cdr as Pair;
        if(fast==null) return false;
      }
    }
  }
  #endregion

  #region cons*
  public sealed class consAll : Primitive
  { public consAll() : base("cons*", 1, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      int i = args.Length-1;
      object obj = args[i];
      while(i-- != 0) obj = new Pair(args[i], obj);
      return obj;
    }
  }
  #endregion

  #region count
  public sealed class count : Primitive
  { public count() : base("count", 2, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);

      IProcedure func = Ops.ExpectProcedure(args[0]);
      int count=0;
      if(args.Length==2)
      { Pair p = Ops.ExpectList(args[1]);
        args = new object[1];
        while(p!=null)
        { args[0] = p.Car;
          p = p.Cdr as Pair;
          if(Ops.IsTrue(func.Call(args))) count++;
        }
        return count;
      }
      else
      { Pair[] pairs = new Pair[args.Length-1];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        args = new object[pairs.Length];

        while(true)
        { for(int i=0; i<pairs.Length; i++)
          { if(pairs[i]==null) return count;
            args[i] = pairs[i].Car;
            pairs[i] = pairs[i].Cdr as Pair;
          }
          if(Ops.IsTrue(func.Call(args))) count++;
        }
      }
    }
  }
  #endregion

  #region dotted-list?
  public sealed class dottedListP : Primitive
  { public dottedListP() : base("dotted-list?", 1, 1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair slow = args[0] as Pair;
      if(slow==null) return Ops.FALSE;

      Pair fast = slow.Cdr as Pair;
      if(fast==null) return slow.Cdr==null ? Ops.FALSE : Ops.TRUE;
    
      while(true)
      { if(slow==fast) return Ops.FALSE;
        slow = (Pair)slow.Cdr;
        Pair next = fast.Cdr as Pair;
        if(next==null) return fast.Cdr==null ? Ops.FALSE : Ops.TRUE;
        fast = next.Cdr as Pair;
        if(fast==null) return next.Cdr==null ? Ops.FALSE : Ops.TRUE;
      }
    }
  }
  #endregion

  #region drop
  public sealed class drop : Primitive
  { public drop() : base("drop", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return core(Name, Ops.ExpectList(args[0]), Ops.ExpectInt(args[1]));
    }
    
    internal static Pair core(string name, Pair pair, int length)
    { for(int i=0; i<length; i++)
      { Pair next = pair.Cdr as Pair;
        if(next==null)
        { if(pair.Cdr==null && i==length-1) return null;
          throw new ArgumentException(name+": list is not long enough");
        }
        pair = next;
      }
      return pair;
    }
  }
  #endregion

  #region filter-map
  public sealed class filterMap : Primitive
  { public filterMap() : base("filter-map", 1, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==1) return null;

      IProcedure func = Ops.ExpectProcedure(args[0]);
      object value;

      if(args.Length==2)
      { Pair p=Ops.ExpectList(args[1]), head=null, tail=null;
        args = new object[1];
        while(p!=null)
        { args[0] = p.Car;
          p = p.Cdr as Pair;
          value = func.Call(args);
          if(value is bool && !(bool)value) continue;
          Pair next = new Pair(value, null);
          if(head==null) head=tail=next;
          else { tail.Cdr=next; tail=next; }
        }
        return head;
      }
      else
      { Pair[] pairs = new Pair[args.Length-1];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        args = new object[pairs.Length];

        Pair head=null, tail=null;
        while(true)
        { for(int i=0; i<pairs.Length; i++)
          { if(pairs[i]==null) return head;
            args[i] = pairs[i].Car;
            pairs[i] = pairs[i].Cdr as Pair;
          }
          value = func.Call(args);
          if(value is bool && !(bool)value) continue;
          Pair next = new Pair(value, null);
          if(head==null) head=tail=next;
          else { tail.Cdr=next; tail=next; }
        }
      }
    }
  }
  #endregion

  #region fold
  public sealed class fold : Primitive
  { public fold() : base("fold", 3, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure kons=Ops.ExpectProcedure(args[0]);
      object ans=args[1];

      if(args.Length==3)
      { Pair pair = Ops.ExpectList(args[2]);
        args = new object[2];
        while(true)
        { if(pair==null) return ans;
          args[0] = pair.Car;
          args[1] = ans;
          ans = kons.Call(args);
          pair = pair.Cdr as Pair;
        }
      }
      else
      { Pair[] pairs = new Pair[args.Length-2];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        args = new object[pairs.Length+1];
        args[pairs.Length] = ans;
        while(true)
        { for(int i=0; i<pairs.Length; i++)
          { if(pairs[i]==null) return args[pairs.Length];
            args[i]  = pairs[i].Car;
            pairs[i] = pairs[i].Cdr as Pair;
          }
          args[pairs.Length] = func.Call(args);
        }
      }
    }
  }
  #endregion

  #region fold-right
  public sealed class foldRight : Primitive
  { public foldRight() : base("fold-right", 3, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure kons=Ops.ExpectProcedure(args[0]);
      object ans=args[1];

      if(args.Length==3) return new folder1(kons, ans).Run(Ops.ExpectList(args[2]));
      else
      { Pair[] pairs = new Pair[args.Length-2];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);
        return new folderN(kons, ans, args.Length-2).Run(pairs);
      }
    }

    struct folder1
    { public folder1(IProcedure kons, object ans) { this.kons=kons; this.ans=ans; args=new object[2]; }

      public object Run(Pair pair)
      { if(pair==null) return ans;
        args[1] = Run(pair.Cdr as Pair);
        args[0] = pair.Car;
        return kons.Call(args);
      }
      
      object[] args;
      object ans;
      IProcedure kons;
    }

    struct folderN
    { public folderN(IProcedure kons, object ans, int nlists)
      { this.kons=kons; this.ans=ans; args=new object[nlists+1];
      }

      public object Run(Pair[] pairs)
      { Pair[] next = new Pair[pairs.Length];
        for(int i=0; i<pairs.Length; i++)
          if((next[i]=pairs[i].Cdr as Pair)==null) return ans;
        args[pairs.Length] = Run(next);
        for(int i=0; i<pairs.Length; i++) args[i] = pairs[i].Car;
        return kons.Call(args);
      }

      object[] args;
      object ans;
      IProcedure kons;
    }
  }
  #endregion

  #region for-each-pair
  public sealed class forEachPair : Primitive
  { public forEachPair() : base("for-each-pair", 1, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==1) return null;

      IProcedure func = Ops.ExpectProcedure(args[0]);
      if(args.Length==2)
      { Pair p = Ops.ExpectList(args[1]);
        args = new object[1];
        while(p!=null)
        { args[0] = p;
          p = p.Cdr as Pair;
          func.Call(args);
        }
        return null;
      }
      else
      { Pair[] pairs = new Pair[args.Length-1];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        args = new object[pairs.Length];
        while(true)
        { for(int i=0; i<pairs.Length; i++)
          { if(pairs[i]==null) return null;
            args[i] = pairs[i];
            pairs[i] = pairs[i].Cdr as Pair;
          }
          func.Call(args);
        }
      }
    }
  }
  #endregion

  #region iota
  public sealed class iota : Primitive
  { public iota() : base("iota", 1, 3) { }
    public override object Call(object[] args)
    { CheckArity(args);
      int count = Ops.ToInt(args[0]);
      if(count==0) return null;
      if(count<0) throw Ops.ValueError(Name+": count cannot be negative");
      object start=args.Length<2 ? 0 : args[1], step=args.Length<3 ? 1 : args[2];
      // this can be optimized with type-specific paths (eg, int and float)
      Pair head=new Pair(start, null), tail=head;
      while(--count!=0)
      { start = Ops.Add(start, step);
        Pair next = new Pair(start, null);
        tail.Cdr=next; tail=next;
      }
      return head;
    }
  }
  #endregion

  #region last
  public sealed class last : Primitive
  { public last() : base("last", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return lastPair.core(Ops.ExpectPair(args[0])).Car;
    }
  }
  #endregion

  #region last-pair
  public sealed class lastPair : Primitive
  { public lastPair() : base("last-pair", 1, 1) { }
  
    public override object Call(object[] args)
    { CheckArity(args);
      return args[0]==null ? null : core(Ops.ExpectPair(args[0]));
    }

    internal static Pair core(Pair pair)
    { while(true)
      { Pair next = pair.Cdr as Pair;
        if(next==null) return pair;
        pair = next;
      }
    }
  }
  #endregion

  #region length+
  public sealed class lengthPlus : Primitive
  { public lengthPlus() : base("length+", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      Pair slow = Ops.ExpectList(args[0]);
      if(slow==null) return 0;
      
      Pair fast = slow.Cdr as Pair;
      if(fast==null) return 1;
    
      int length=1;
      while(true)
      { if(slow==fast) return Ops.FALSE;
        slow = (Pair)slow.Cdr;
        fast = fast.Cdr as Pair;
        if(fast==null) return length+1;
        fast = fast.Cdr as Pair;
        length += 2;
        if(fast==null) return length;
      }
    }
  }
  #endregion

  #region list-copy
  public sealed class listCopy : Primitive
  { public listCopy() : base("list-copy", 1, 1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args[0]==null) return null;
      Pair list=Ops.ExpectPair(args[0]), head=new Pair(list.Car, list.Cdr), tail=head;
      while(true)
      { list = list.Cdr as Pair;
        if(list==null) return head;
        Pair next = new Pair(list.Car, list.Cdr);
        tail.Cdr = next;
        tail = next;
      }
    }
  }
  #endregion

  #region list=?
  public sealed class listEqP : Primitive
  { public listEqP() : base("list=?", 1, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length<3) return Ops.TRUE;

      IProcedure test = Ops.ExpectProcedure(args[0]);
      object[] nargs = new object[2];

      Pair last=Ops.ExpectList(args[1]);
      for(int i=2; i<args.Length; i++)
      { Pair cur=Ops.ExpectList(args[i]), cpair=cur;
        while(true)
        { if(cur==null)
          { if(last!=null) return Ops.FALSE;
            else break;
          }
          else if(last==null) return Ops.FALSE;
          
          args[0]=last.Car; args[1]=cur.Car;
          if(!Ops.IsTrue(test.Call(args))) return Ops.FALSE;

          last=last.Cdr as Pair; cur=cur.Cdr as Pair;
        }
        last=cur;
      }
      
      return Ops.TRUE;
    }
  }
  #endregion

  #region make-list
  public sealed class makeList : Primitive
  { public makeList() : base("make-list", 1, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      int length  = Ops.ExpectInt(args[0]);
      object fill = args.Length==2 ? args[1] : null;

      Pair head=null, tail=null;
      for(int i=0; i<length; i++)
      { Pair next = new Pair(fill, null);
        if(head==null) head=tail=next;
        else { tail.Cdr=next; tail=next; }
      }
      return head;
    }
  }
  #endregion

  #region mapN
  public sealed class mapN : Primitive
  { public mapN() : base("mapN", 1, -1) { }

    public override object Call(object[] args)
    { CheckArity(args);
      if(args.Length==1) return null;

      IProcedure func = Ops.ExpectProcedure(args[0]);
      if(args.Length==2)
      { Pair head=Ops.ExpectList(args[1]), tail=head;
        args = new object[1];
        while(tail!=null)
        { args[0]  = tail.Car;
          tail.Car = func.Call(args);
          tail = tail.Cdr as Pair;
        }
        return head;
      }
      else
      { Pair[] pairs = new Pair[args.Length-1];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        args = new object[pairs.Length];

        Pair head=pairs[0], tail=head;
        while(tail!=null)
        { args[0] = tail.Car;
          for(int i=1; i<pairs.Length; i++)
          { if(pairs[i]==null) goto done;
            args[i] = pairs[i].Car;
            pairs[i] = pairs[i].Cdr as Pair;
          }
          tail.Car = func.Call(args);
          tail = tail.Cdr;
        }
        done: return head;
      }
    }
  }
  #endregion

  #region pair-fold
  public sealed class pairFold : Primitive
  { public pairFold() : base("pair-fold", 3, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure kons=Ops.ExpectProcedure(args[0]);
      object ans=args[1];

      if(args.Length==3)
      { Pair pair = Ops.ExpectList(args[2]);
        args = new object[2];
        while(true)
        { if(pair==null) return ans;
          args[0] = pai;
          args[1] = ans;
          ans = kons.Call(args);
          pair = pair.Cdr as Pair;
        }
      }
      else
      { Pair[] pairs = new Pair[args.Length-2];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);

        args = new object[pairs.Length+1];
        args[pairs.Length] = ans;
        while(true)
        { for(int i=0; i<pairs.Length; i++)
          { if(pairs[i]==null) return args[pairs.Length];
            args[i]  = pairs[i];
            pairs[i] = pairs[i].Cdr as Pair;
          }
          args[pairs.Length] = func.Call(args);
        }
      }
    }
  }
  #endregion

  #region pair-fold-right
  public sealed class pairFoldRight : Primitive
  { public pairFoldRight() : base("pair-fold-right", 3, -1) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure kons=Ops.ExpectProcedure(args[0]);
      object ans=args[1];

      if(args.Length==3) return new folder1(kons, ans).Run(Ops.ExpectList(args[2]));
      else
      { Pair[] pairs = new Pair[args.Length-2];
        for(int i=0; i<pairs.Length; i++) pairs[i] = Ops.ExpectList(args[i+1]);
        return new folderN(kons, ans, args.Length-2).Run(pairs);
      }
    }

    struct folder1
    { public folder1(IProcedure kons, object ans) { this.kons=kons; this.ans=ans; args=new object[2]; }

      public object Run(Pair pair)
      { if(pair==null) return ans;
        args[1] = Run(pair.Cdr as Pair);
        args[0] = pair;
        return kons.Call(args);
      }
      
      object[] args;
      object ans;
      IProcedure kons;
    }

    struct folderN
    { public folderN(IProcedure kons, object ans, int nlists)
      { this.kons=kons; this.ans=ans; args=new object[nlists+1];
      }

      public object Run(Pair[] pairs)
      { Pair[] next = new Pair[pairs.Length];
        for(int i=0; i<pairs.Length; i++)
          if((next[i]=pairs[i].Cdr as Pair)==null) return ans;
        args[pairs.Length] = Run(next);
        for(int i=0; i<pairs.Length; i++) args[i] = pairs[i];
        return kons.Call(args);
      }

      object[] args;
      object ans;
      IProcedure kons;
    }
  }
  #endregion

  #region take
  public sealed class take : Primitive
  { public take() : base("take", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return core(Name, Ops.ExpectList(args[0]), Ops.ExpectInt(args[1]));
    }

    internal static Pair core(string name, Pair pair, int length)
    { if(length<=0) return null;

      Pair head=null, tail=null;
      do
      { if(pair==null) throw new ArgumentException(name+": list is not long enough");
        Pair next = new Pair(pair.Car, null);
        if(head==null) head=tail=next;
        else { tail.Cdr=next; tail=next; }
        pair = pair.Cdr as Pair;
      } while(--length != 0);
      return head;
    }
  }
  #endregion

  #region take!
  public sealed class takeN : Primitive
  { public takeN() : base("take!", 2, 2) { }
    public override object Call(object[] args)
    { CheckArity(args);
      Pair  head = Ops.ExpectList(args[0]), pair=head;
      int length = Ops.ExpectInt(args[1]);
      if(length<=0) return null;
      while(--length != 0)
      { if(pair==null) throw new ArgumentException(Name+": list is not long enough");
        pair = pair.Cdr as Pair;
      }
      pair.Cdr = null;
      return head;
    }
  }
  #endregion
  
  #region unfold
  public sealed class unfold : Primitive
  { public unfold() : base("unfold", 4, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      return new unfolder(args).Run(args[3]);
    }
    
    struct unfolder
    { public unfolder(object[] args)
      { stop=Ops.ExpectProcedure(args[0]);  val=Ops.ExpectProcedure(args[1]);
        next=Ops.ExpectProcedure(args[2]); tail=args.Length==4 ? null : Ops.ExpectProcedure(args[4]);
        seed=new object[1];
      }

      public object Run(object seed)
      { seed[0] = seed;
        if(Ops.IsTrue(seed)) return tail==null ? null : tail.Call(seed);
        return new Pair(val.Call(seed), Run(next.Call(seed)));
      }

      IProcedure stop, val, next, tail;
      object[] seed;
    }
  }
  #endregion

  #region unfold-right
  public sealed class unfoldRight : Primitive
  { public unfoldRight() : base("unfold-right", 4, 5) { }
    public override object Call(object[] args)
    { CheckArity(args);
      IProcedure stop=Ops.ExpectProcedure(args[0]), val=Ops.ExpectProcedure(args[1]),
                 next=Ops.ExpectProcedure(args[2]);
      object list=args.Length==4 ? null : args[4];
      object[] seed = new object[1] { args[3] };

      while(!Ops.IsTrue(stop.Call(seed)))
      { list = new Pair(val.Call(seed), list);
        seed[0] = next.Call(seed);
      }
      return list;
    }
  }
  #endregion
}

} // namespace NetLisp.Mods