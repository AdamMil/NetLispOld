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
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region Index, Name, Options, Singleton, IWalker
public sealed class Index
{ public long Next { get { lock(this) return index++; } }
  long index;
}

public sealed class Name
{ public Name(string name) { String=name; Depth=Local; Index=-1; }
  public Name(string name, int depth) { String=name; Depth=depth; Index=-1; }
  public Name(string name, int depth, int index) { String=name; Depth=depth; Index=index; }

  public const int Global=-2, Local=-1;

  public override bool Equals(object obj)
  { Name o = obj as Name;
    return o!=null && o.String==String && o.Depth==Depth && o.Index==Index;
  }

  public override int GetHashCode() { return String.GetHashCode() ^ Depth ^ Index; }

  public string String;
  public int Depth, Index;
}

public sealed class Options
{ private Options() { }
  public static bool Debug, Optimize;
}

public sealed class Singleton
{ public Singleton(string name) { Name=name; }
  public override string ToString() { return "#<singleton: "+Name+">"; }
  public string Name;
}

public interface IWalker
{ void PostWalk(Node node);
  bool Walk(Node node);
}
#endregion

#region AST
public sealed class AST
{ public static LambdaNode Create(object obj) { return (LambdaNode)Create(obj, false); }
  public static Node Create(object obj, bool interpreted)
  { Node body = Parse(obj);
    if(!interpreted)
    { // wrapping it in a lambda node is done so we can keep the preprocessing code simple, and so that we can support
      // top-level closures. it's unwrapped later on by SnippetMaker.Generate()
      body = new LambdaNode(new string[0], false, body);
      body.Preprocess();
      if(Options.Optimize) body.Walk(new Optimizer());
    }
    return body;
  }

  #region Optimizer
  sealed class Optimizer : IWalker
  { public bool Walk(Node node) { return true; }

    public void PostWalk(Node node)
    { node.Optimize();

      AccessNode an = node as AccessNode;
      if(an!=null && an.Value is VariableNode && an.Members.IsConstant)
      { object obj = an.Members.Evaluate();
        if(!(obj is string))
          throw new SyntaxErrorException("(.member) expects a string value as the second argument");
        
        if(accessNodes==null) accessNodes = new Hashtable();
        AccessKey key = new AccessKey(((VariableNode)an.Value).Name, (string)obj);
        AccessNode.CachePromise promise = (AccessNode.CachePromise)accessNodes[key];
        if(promise==null) accessNodes[key] = promise = new AccessNode.CachePromise();
        an.Cache = promise;
      }
    }

    #region AccessKey
    struct AccessKey
    { public AccessKey(Name name, string members) { Name=name; Members=members; }

      public override bool Equals(object obj)
      { AccessKey other = (AccessKey)obj;
        return (Name==other.Name ||
                Name.Depth==Name.Global && other.Name.Depth==Name.Global && Name.String==other.Name.String) &&
               Members==other.Members;
      }

      public override int GetHashCode()
      { return Name.Depth ^ Name.Index ^ Name.String.GetHashCode() ^ Members.GetHashCode();
      }

      public Name Name;
      public string Members;
    }
    #endregion

    Hashtable accessNodes;
  }
  #endregion

  static Node Parse(object obj)
  { Symbol sym = obj as Symbol;
    if(sym!=null)
    { string name = sym.Name;
      if(name==".last") return new dotLastNode();
      int pos = name.IndexOf('.', 1);
      Node var = new VariableNode(pos==-1 ? name : name.Substring(0, pos));
      return pos==-1 ? var : new AccessNode(var, new LiteralNode(name.Substring(pos+1)));
    }

    Pair pair = obj as Pair;
    if(pair==null) return new LiteralNode(obj);

    sym = pair.Car as Symbol;
    if(sym!=null)
      switch(sym.Name)
      { case "quote":
        { if(Builtins.length.core(pair)!=2) throw Ops.SyntaxError("quote: expects exactly 1 form");
          return Quote(Ops.FastCadr(pair));
        }
        case "if":
        { int len = Builtins.length.core(pair);
          if(len<3 || len>4) throw Ops.SyntaxError("if: expects 2 or 3 forms");
          pair = (Pair)pair.Cdr;
          Pair next = (Pair)pair.Cdr;
          return new IfNode(Parse(pair.Car), Parse(next.Car), next.Cdr==null ? null : Parse(Ops.FastCadr(next)));
        }
        case "let":
        { if(Builtins.length.core(pair)<3) goto error;
          pair = (Pair)pair.Cdr;

          Pair bindings = pair.Car as Pair;
          if(bindings==null) goto error;
          string[] names = new string[Builtins.length.core(bindings)];
          Node[]   inits = new Node[names.Length];
          for(int i=0; i<names.Length; bindings=(Pair)bindings.Cdr,i++)
          { if(bindings.Car is Pair)
            { Pair binding = (Pair)bindings.Car;
              sym = binding.Car as Symbol;
              inits[i] = Parse(Ops.FastCadr(binding));
            }
            else sym = bindings.Car as Symbol;
            if(sym==null) goto error;
            names[i] = sym.Name;
          }

          return new LetNode(names, inits, ParseBody((Pair)pair.Cdr));
          error: throw Ops.SyntaxError("let: must be of the form (let ([symbol | (symbol form)] ...) forms ...)");
        }
        case "begin":
        { if(Builtins.length.core(pair)<2) throw Ops.SyntaxError("begin: no forms given");
          return ParseBody((Pair)pair.Cdr);
        }
        case "lambda":
        { if(Builtins.length.core(pair)<3) throw Ops.SyntaxError("lambda: must be of the form (lambda bindings forms ...)");
          pair = (Pair)pair.Cdr;
          bool hasList;
          return new LambdaNode(ParseLambaList(pair.Car, out hasList), hasList, ParseBody((Pair)pair.Cdr));
        }
        case "set!":
        { if(Builtins.length.core(pair)<3) goto error;
          ArrayList names=new ArrayList(), values=new ArrayList();
          pair = (Pair)pair.Cdr;
          do
          { sym = pair.Car as Symbol;
            if(sym==null) goto error;
            names.Add(new Name(sym.Name));
            pair = pair.Cdr as Pair;
            if(pair==null) goto error;
            values.Add(Parse(pair.Car));
            pair = pair.Cdr as Pair;
          } while(pair!=null);
          return new SetNode((Name[])names.ToArray(typeof(Name)), (Node[])values.ToArray(typeof(Node)));
          error: throw Ops.SyntaxError("set!: must be of form (set! symbol form [symbol form] ...)");
        }
        case "define":
        { int length = Builtins.length.core(pair);
          if(length!=3) throw Ops.SyntaxError("define: must be of form (define name value)");
          pair = (Pair)pair.Cdr;
          sym = (Symbol)pair.Car;
          return new DefineNode(sym.Name, Parse(Ops.FastCadr(pair)));
        }
        case "vector":
        { ArrayList items = new ArrayList();
          while(true)
          { pair = pair.Cdr as Pair;
            if(pair==null) break;
            items.Add(Parse(pair.Car));
          }
          return new VectorNode((Node[])items.ToArray(typeof(Node)));
        }
        /* (let-values (((a b c) (values 1 2 3))
                        ((x y) (values 4 5 6)))
              (+ a b c x y))
        */
        case "let-values":
        { if(Builtins.length.core(pair)<3) goto error;
          pair = (Pair)pair.Cdr;
          Pair bindings = pair.Car as Pair;
          if(bindings==null)
          { if(pair.Car==null) return ParseBody((Pair)pair.Cdr);
            goto error;
          }
          ArrayList names=new ArrayList(), inits=new ArrayList();
          do
          { Pair binding = bindings.Car as Pair;
            if(binding==null) goto bindingError;
            Pair namePair = binding.Car as Pair;
            if(namePair==null) goto bindingError;
            Name[] narr = new Name[Builtins.length.core(namePair)];
            for(int i=0; i<narr.Length; namePair=namePair.Cdr as Pair,i++)
            { sym = namePair.Car as Symbol;
              if(sym==null) goto bindingError;
              narr[i] = new Name(sym.Name);
            }
            names.Add(narr);
            inits.Add(Parse(Ops.FastCadr(binding)));
            bindings = bindings.Cdr as Pair;
          } while(bindings!=null);

          return new LetValuesNode((Name[][])names.ToArray(typeof(Name[])),
                                   (Node[])inits.ToArray(typeof(Node)), ParseBody((Pair)pair.Cdr));
          error: throw Ops.SyntaxError("let-value: must be of form (let-values bindings form ...)");
          bindingError: throw Ops.SyntaxError("let-value: bindings must be of form (((symbol ...) form) ...)");
        }
        /* (try
            (begin forms ...)
            (catch (e exn:syntaxerror exn:othererror)
              forms ...)
            (catch () forms ...)
            (finally forms ...))
        */
        case "try":
        { if(Builtins.length.core(pair)<3) goto error;
          pair = (Pair)pair.Cdr;
          Node final=null, body=Parse(pair.Car);
          ArrayList excepts=null, etypes=null;

          while((pair=(Pair)pair.Cdr) != null)
          { Pair form = pair.Car as Pair;
            if(form==null) goto error;
            sym = form.Car as Symbol;
            if(sym==null) goto error;
            if(sym.Name=="catch")
            { if(excepts==null) excepts=new ArrayList();
              string evar;
              if(Builtins.length.core(form)<3) goto catchError;
              form = (Pair)form.Cdr;
              if(form.Car is Pair)
              { Pair epair = (Pair)form.Car;
                if(Builtins.length.core(epair)>2) goto catchError;
                sym = epair.Car as Symbol;
                if(sym==null) goto catchError;
                evar = sym.Name;
                epair = (Pair)epair.Cdr;
                if(epair!=null)
                { if(etypes==null) etypes = new ArrayList();
                  do
                  { sym = epair.Car as Symbol;
                    if(sym==null) goto catchError;
                    etypes.Add(sym.Name);
                  } while((epair=(Pair)epair.Cdr) != null);
                }
              }
              else if(form.Car!=null) goto catchError;
              else evar = null;
              excepts.Add(new TryNode.Except(evar, etypes==null ? null : (string[])etypes.ToArray(typeof(string)),
                                             ParseBody((Pair)form.Cdr)));
              if(etypes!=null) etypes.Clear();
            }
            else if(sym.Name=="finally")
            { if(final!=null) goto error;
              final = ParseBody((Pair)form.Cdr);
            }
            else goto error;
          }

          if(excepts==null && final==null) goto error;
          return new TryNode(body, excepts==null ? null : (TryNode.Except[])excepts.ToArray(typeof(TryNode.Except)),
                             final);
          error: throw Ops.SyntaxError("try form expects one body form followed by catch forms and/or an optional finally form");
          catchError: throw Ops.SyntaxError("catch form should be of the form: (catch ([e [type ...]]) forms...)");
        }
        // (throw [type [objects ...]]) ; type-less throw only allowed within catch form
        case "throw":
        { string type=null;
          ArrayList objs=null;

          pair = pair.Cdr as Pair;
          if(pair!=null)
          { sym = pair.Car as Symbol;
            if(sym==null) throw Ops.SyntaxError("throw must be of form (throw [type [forms ...]])");
            type = sym.Name;
            
            pair = pair.Cdr as Pair;
            if(pair!=null)
            { objs = new ArrayList();
              do
              { objs.Add(Parse(pair.Car));
                pair = pair.Cdr as Pair;
              } while(pair!=null);
            }
          }
          
          return new ThrowNode(type, objs==null ? null : (Node[])objs.ToArray(typeof(Node)));
        }
        // (#%mark-position form startLine startCol endLine endCol)
        case "#%mark-position":
        { pair = (Pair)pair.Cdr;
          Node node = Parse(pair.Car);
          pair = (Pair)pair.Cdr;
          node.StartLine = (int)pair.Car;
          pair = (Pair)pair.Cdr;
          node.StartColumn = (int)pair.Car;
          pair = (Pair)pair.Cdr;
          node.EndLine = (int)pair.Car;
          pair = (Pair)pair.Cdr;
          node.EndColumn = (int)pair.Car;
          return node;
        }
        // (#%mark-source "filename" "source")
        case "#%mark-source":
        { pair = (Pair)pair.Cdr;
          return new MarkSourceNode((string)pair.Car, (string)Ops.FastCadr(pair));
        }
        // (.member object member-names)
        case ".member":
        { int length = Builtins.length.core(pair);
          if(length!=3) throw Ops.SyntaxError(".member: must be of form (.member obj-form names-form)");
          pair = (Pair)pair.Cdr;
          return new AccessNode(Parse(pair.Car), Parse(Ops.FastCadr(pair)));
        }
      }

    Node func = Parse(pair.Car);
    ArrayList args = new ArrayList();
    while(true)
    { pair = pair.Cdr as Pair;
      if(pair==null) break;
      args.Add(Parse(pair.Car));
    }
    if(Options.Optimize && func is LambdaNode) // optimization: transform ((lambda (a) ...) x) into (let ((a x)) ...)
    { LambdaNode fl = (LambdaNode)func;
      string[] names = new string[fl.Parameters.Length];
      for(int i=0; i<names.Length; i++) names[i] = fl.Parameters[i].String;
      int positional = fl.Parameters.Length-(fl.HasList ? 1 : 0);
      Ops.CheckArity("<unnamed lambda>", args.Count, positional, fl.HasList ? -1 : positional);

      Node[] inits = new Node[names.Length];
      for(int i=0; i<positional; i++) inits[i] = (Node)args[i];
      if(fl.HasList)
      { if(args.Count==positional) inits[positional] = new LiteralNode(null);
        else
        { Node[] elems = new Node[args.Count-positional];
          for(int i=positional; i<args.Count; i++) elems[i-positional] = (Node)args[i];
          inits[positional] = new ListNode(elems, null);
        }
      }
      return new LetNode(names, inits, fl.Body);
    }
    else return new CallNode(func, (Node[])args.ToArray(typeof(Node)));
  }

  static Node ParseBody(Pair start)
  { if(start==null) return null;
    ArrayList items = new ArrayList();
    while(start!=null)
    { items.Add(Parse(start.Car));
      start = start.Cdr as Pair;
    }
    if(items.Count==1) return (Node)items[0];
    return new BodyNode((Node[])items.ToArray(typeof(Node)));
  }

  static string[] ParseLambaList(object obj, out bool hasList)
  { hasList = false;
    if(obj is Symbol) { hasList=true; return new string[] { ((Symbol)obj).Name }; }

    Pair list = (Pair)obj;
    ArrayList names = new ArrayList();
    while(list!=null)
    { Symbol sym = list.Car as Symbol;
      if(sym==null) goto error;
      names.Add(sym.Name);
      object next = list.Cdr;
      list = next as Pair;
      if(list==null && next!=null)
      { sym = next as Symbol;
        if(sym==null) goto error;
        names.Add(sym.Name);
        hasList=true;
        break;
      }
    }
    return (string[])names.ToArray(typeof(string));
    
    error: throw Ops.SyntaxError("lambda bindings must be of the form: symbol | (symbol... [ . symbol])");
  }

  static Node Quote(object obj)
  { Pair pair = obj as Pair;
    if(pair==null) return new LiteralNode(obj);
    
    ArrayList items = new ArrayList();
    Node dot=null;
    while(true)
    { items.Add(Quote(pair.Car));
      object next = pair.Cdr;
      pair = next as Pair;
      if(pair==null)
      { if(next!=null) dot = Quote(next);
        break;
      }
    }
    return new ListNode((Node[])items.ToArray(typeof(Node)), dot);
  }
}
#endregion

#region Node
public abstract class Node
{ [Flags] public enum Flag : byte { Tail=1, Const=2, };

  public bool IsConstant
  { get { return (Flags&Flag.Const) != 0; }
    set { if(value) Flags|=Flag.Const; else Flags&=~Flag.Const; }
  }

  public bool Tail
  { get { return (Flags&Flag.Tail) != 0; }
    set { if(value) Flags|=Flag.Tail; else Flags&=~Flag.Tail; }
  }

  public void Emit(CodeGenerator cg)
  { Type type = typeof(object);
    Emit(cg, ref type);
  }

  public abstract void Emit(CodeGenerator cg, ref Type etype);

  public void EmitVoid(CodeGenerator cg)
  { Type type = typeof(void);
    Emit(cg, ref type);
    if(type!=typeof(void)) cg.ILG.Emit(OpCodes.Pop);
  }

  public virtual object Evaluate() { throw new NotSupportedException(); }

  public abstract Type GetNodeType();

  public virtual void MarkTail(bool tail) { Tail=tail; }
  public virtual void Optimize() { }
  public virtual void Preprocess() { MarkTail(true); }

  public virtual void Walk(IWalker w)
  { w.Walk(this);
    w.PostWalk(this);
  }

  public LambdaNode InFunc;
  public TryNode InTry;
  public int StartLine, StartColumn, EndLine, EndColumn;
  public Flag Flags;
  
  public static bool AreEquivalent(Type type, Type desired)
  { Conversion conv = Ops.ConvertTo(type, desired);
    return conv==Conversion.Identity || conv==Conversion.Reference;
  }

  public static bool Compatible(Type type, Type desired)
  { if((type!=null && type.IsValueType) != desired.IsValueType) return false;
    Conversion conv = Ops.ConvertTo(type, desired);
    return conv!=Conversion.None && conv!=Conversion.Unsafe;
  }

  public static void EmitConstant(CodeGenerator cg, object value, ref Type etype)
  { if(etype==null) cg.ILG.Emit(OpCodes.Ldnull);
    else if(etype==typeof(void)) return;
    else
    { value = TryConvert(value, ref etype);
      if(etype.IsValueType)
      { if(Type.GetTypeCode(etype)!=TypeCode.Object) cg.EmitConstant(value);
        else
        { cg.EmitConstantObject(value);
          cg.ILG.Emit(OpCodes.Unbox, etype);
          cg.EmitIndirectLoad(etype);
        }
      }
      else cg.EmitConstantObject(value);
    }
  }

  public static object MaybeEmitBranch(CodeGenerator cg, Node test, Label label, bool onTrue)
  { OpCode brtrue=onTrue ? OpCodes.Brtrue : OpCodes.Brfalse, brfalse=onTrue ? OpCodes.Brfalse : OpCodes.Brtrue;
    Type type = typeof(bool);
    test.Emit(cg, ref type);

    if(type==typeof(bool)) cg.ILG.Emit(brtrue, label);
    else if(type==typeof(negbool)) cg.ILG.Emit(brfalse, label);
    else if(type==typeof(object))
    { cg.EmitIsTrue();
      cg.ILG.Emit(brtrue, label);
    }
    else
    { cg.ILG.Emit(OpCodes.Pop);
      return type!=null;
    }
    return null;
  }

  public static object TryConvert(object value, ref Type etype)
  { if(etype==null) return null;
    if(etype==typeof(object)) return value;

    Type vtype = value==null ? null : value.GetType();
    if(Compatible(vtype, etype))
    { value = Ops.ConvertTo(value, etype);
      etype = value.GetType();
    }
    else etype = value.GetType();
    return value;
  }

  protected struct negbool { }

  protected void TailReturn(CodeGenerator cg)
  { if(Tail)
    { if(InTry==null) cg.EmitReturn();
      else
      { InTry.ReturnSlot.EmitSet(cg);
        cg.ILG.Emit(OpCodes.Leave, InTry.LeaveLabel);
      }
    }
  }

  protected static object[] MakeObjectArray(Node[] nodes)
  { if(nodes.Length==0) return Ops.EmptyArray;
    object[] ret = new object[nodes.Length];
    for(int i=0; i<ret.Length; i++) ret[i] = nodes[i].Evaluate();
    return ret;
  }
}
#endregion

#region dotLastNode
public sealed class dotLastNode : Node
{ public override void Emit(CodeGenerator cg, ref Type etype)
  { if(etype!=typeof(void))
    { cg.EmitFieldGet(typeof(Ops), "LastPtr");
      etype = typeof(object);
      TailReturn(cg);
    }
  }

  public override object Evaluate() { return Ops.LastPtr; }
  public override Type GetNodeType() { return typeof(object); }
  public override void Walk(IWalker w) { if(w.Walk(this)) w.PostWalk(this); }
}
#endregion

#region AccessNode
public sealed class AccessNode : Node
{ public AccessNode(Node value, Node members) { Value=value; Members=members; }

  public sealed class CachePromise
  { public Slot GetCache(CodeGenerator cg)
    { if(cache==null)
        cache = cg.TypeGenerator.DefineStaticField(FieldAttributes.Private, "tc$"+cindex.Next, typeof(MemberCache));
      return cache;
    }

    Slot cache;

    static Index cindex = new Index();
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { Value.Emit(cg);
    if(Members.IsConstant)
    { object obj = Members.Evaluate();
      if(!(obj is string)) throw new SyntaxErrorException("(.member) expects a string value as the second argument");

      Slot cache;
      Label done;
      if(!Options.Optimize) { cache=null; done=new Label(); }
      else // TODO: optimize this so the cache is shared among others referencing the same name object
      { Slot tmp=cg.AllocLocalTemp(typeof(object));
        Label miss=cg.ILG.DefineLabel(), isNull=cg.ILG.DefineLabel();
        cache = Cache.GetCache(cg);
        done = cg.ILG.DefineLabel();

        cg.ILG.Emit(OpCodes.Dup);
        cg.ILG.Emit(OpCodes.Brfalse_S, isNull);
        cg.ILG.Emit(OpCodes.Dup);
        tmp.EmitSet(cg);
        cg.EmitCall(typeof(MemberCache), "TypeFromObject"); // TODO: maybe we should inline this
        cache.EmitGetAddr(cg);
        cg.EmitFieldGet(typeof(MemberCache), "Type");
        cg.ILG.Emit(OpCodes.Bne_Un_S, miss);
        tmp.EmitGet(cg);
        cg.EmitFieldSet(typeof(Ops), "LastPtr"); // this is not exactly the same as the way it works below...
        cache.EmitGetAddr(cg);
        cg.EmitFieldGet(typeof(MemberCache), "Value");
        cg.ILG.Emit(OpCodes.Br, done);
        cg.ILG.MarkLabel(miss);
        cache.EmitGetAddr(cg);
        tmp.EmitGet(cg);
        cg.EmitCall(typeof(MemberCache), "TypeFromObject"); // TODO: maybe we should inline this
        cg.EmitFieldSet(typeof(MemberCache), "Type");
        tmp.EmitGet(cg);
        cg.ILG.MarkLabel(isNull);

        cg.FreeLocalTemp(tmp);
      }

      string[] bits = ((string)obj).Split('.');
      for(int i=0; i<bits.Length; i++)
      { if(i==bits.Length-1)
        { cg.ILG.Emit(OpCodes.Dup);
          cg.EmitFieldSet(typeof(Ops), "LastPtr");
        }
        cg.EmitCall(typeof(MemberContainer), "FromObject");
        cg.EmitString(bits[i]);
        cg.EmitCall(typeof(MemberContainer), "GetMember", new Type[] { typeof(string) });
      }

      if(Options.Optimize)
      { Slot tmp = cg.AllocLocalTemp(typeof(object));
        tmp.EmitSet(cg);
        cache.EmitGetAddr(cg);
        tmp.EmitGet(cg);
        cg.EmitFieldSet(typeof(MemberCache), "Value");
        tmp.EmitGet(cg);
        cg.ILG.MarkLabel(done);
        cg.FreeLocalTemp(tmp);
      }
    }
    else
    { cg.EmitTypedNode(Members, typeof(string));
      cg.EmitCall(typeof(Ops), "GetMember");
    }
    etype = typeof(object);
    TailReturn(cg);
  }

  public override object Evaluate()
  { return Ops.GetMember(Value.Evaluate(), Ops.ExpectString(Members.Evaluate()));
  }

  public override Type GetNodeType() { return typeof(object); }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { Value.Walk(w);
      Members.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node Value, Members;
  public CachePromise Cache;
}
#endregion

#region BodyNode
public sealed class BodyNode : Node
{ public BodyNode(Node[] forms) { Forms=forms; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(Forms.Length==0)
    { if(etype!=typeof(void))
      { cg.ILG.Emit(OpCodes.Ldnull);
        etype = null;
      }
    }
    else
    { int i;
      for(i=0; i<Forms.Length-1; i++) Forms[i].EmitVoid(cg);
      Forms[i].Emit(cg, ref etype);
    }
  }

  public override object Evaluate()
  { object ret=null;
    foreach(Node n in Forms) ret = n.Evaluate();
    return ret;
  }

  public override Type GetNodeType() { return Forms.Length==0 ? null : Forms[Forms.Length-1].GetNodeType(); }

  public override void MarkTail(bool tail)
  { for(int i=0; i<Forms.Length; i++) Forms[i].MarkTail(tail && i==Forms.Length-1);
  }

  public override void Optimize()
  { bool isconst=true;
    for(int i=0; i<Forms.Length; i++) if(!Forms[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) foreach(Node n in Forms) n.Walk(w);
    w.PostWalk(this);
  }

  public readonly Node[] Forms;
}
#endregion

#region CallNode
public sealed class CallNode : Node
{ public CallNode(Node func, params Node[] args) { Function=func; Args=args; }
  public CallNode(string name, params Node[] args) { Function=new VariableNode(name); Args=args; }
  static CallNode()
  { ArrayList cfunc = new ArrayList();

    ops = new Hashtable();
    string[] arr = new string[]
    { "-", "Subtract", "bitnot", "BitwiseNegate", "+", "Add", "*", "Multiply", "/", "Divide", "//", "FloorDivide",
      "%", "Modulus",  "bitand", "BitwiseAnd", "bitor", "BitwiseOr", "bitxor", "BitwiseXor", "=", "AreEqual",
      "!=", "NotEqual", "<", "Less", ">", "More", "<=", "LessEqual", ">=", "MoreEqual", "expt", "Power",
      "lshift", "LeftShift", "rshift", "RightShift", "exptmod", "PowerMod", "->tostring", "Str"
    };
    for(int i=0; i<arr.Length; i+=2)
    { ops[arr[i]] = arr[i+1];
      cfunc.Add(arr[i]);
    }
    cfunc.AddRange(new string[] {
      "eq?", "eqv?", "equal?", "null?", "pair?", "char?", "symbol?", "string?", "procedure?", "vector?", "values",
      "not", "string-null?", "string-length", "string-ref", "vector-length", "vector-ref", "car", "cdr", "promise?",
      "char-upcase", "char-downcase", "->string"});

    cfunc.Sort();
    constant = (string[])cfunc.ToArray(typeof(string));
  }

  static Hashtable ops;
  static string[] constant;

  #region Emit
  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(IsConstant)
    { EmitConstant(cg, Evaluate(), ref etype);
      TailReturn(cg);
      return;
    }

    cg.MarkPosition(this);
    if(Options.Optimize && Function is VariableNode)
    { VariableNode vn = (VariableNode)Function;
      if(Tail && FuncNameMatch(vn.Name, InFunc)) // see if we can tailcall ourselves with a branch
      { int positional = InFunc.Parameters.Length-(InFunc.HasList ? 1 : 0);
        if(Args.Length<positional)
          throw new TargetParameterCountException(
            string.Format("{0} expects {1}{2} args, but is being passed {3}",
                          vn.Name, InFunc.HasList ? "at least " : "", positional, Args.Length));
        for(int i=0; i<positional; i++) Args[i].Emit(cg);
        if(InFunc.HasList) cg.EmitList(Args, positional);
        for(int i=InFunc.Parameters.Length-1; i>=0; i--) cg.EmitSet(InFunc.Parameters[i]);
        cg.ILG.Emit(InTry==null ? OpCodes.Br : OpCodes.Leave, InFunc.StartLabel);
        etype = typeof(object);
        return;
      }
      else // inline common functions
      { string name=vn.Name.String, opname=(string)ops[name];
        switch(name) // functions with side effects
        { case "set-car!": case "set-cdr!":
            CheckArity(2);
            if(etype==typeof(void))
            { cg.EmitPair(Args[0]);
              Args[1].Emit(cg);
              cg.EmitFieldSet(typeof(Pair), name=="set-car!" ? "Car" : "Cdr");
            }
            else
            { Slot tmp = cg.AllocLocalTemp(typeof(object));
              Args[1].Emit(cg);
              tmp.EmitSet(cg);
              cg.EmitPair(Args[0]);
              tmp.EmitGet(cg);
              cg.EmitFieldSet(typeof(Pair), name=="set-car!" ? "Car" : "Cdr");
              tmp.EmitGet(cg);
              cg.FreeLocalTemp(tmp);
              goto objret;
            }
            goto ret;
        }

        switch(name)
        { case "eq?": case "eqv?": case "equal?":
          { CheckArity(2);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            if(name=="eq?") cg.ILG.Emit(OpCodes.Ceq);
            else cg.EmitCall(typeof(Ops), name=="eqv?" ? "EqvP" : "EqualP");
            if(etype!=typeof(bool))
            { EmitFromBool(cg, true);
              goto objret;
            }
            goto ret;
          }
          case "not":
          { CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Type type = typeof(bool);
            Args[0].Emit(cg, ref type);
            if(etype==typeof(bool))
            { if(type==typeof(bool)) etype=typeof(negbool);
              else
              { if(type!=typeof(negbool)) cg.EmitIsFalse(type);
                etype=typeof(bool);
              }
            }
            else
            { if(type!=typeof(bool) && type!=typeof(negbool)) cg.EmitIsTrue(type);
              EmitFromBool(cg, type==typeof(negbool));
              goto objret;
            }
            goto ret;
          }
          case "null?": case "pair?": case "char?": case "symbol?": case "string?": case "procedure?": case "vector?":
          case "promise?": case "string-null?":
          { CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Type type=null;
            if(name=="string-null?") cg.EmitString(Args[0]);
            else Args[0].Emit(cg);
            switch(name)
            { case "pair?": type=typeof(Pair); break;
              case "char?": type=typeof(char); break;
              case "symbol?": type=typeof(Symbol); break;
              case "string?": type=typeof(string); break;
              case "procedure?": type=typeof(IProcedure); break;
              case "vector?": type=typeof(object[]); break;
              case "promise?": type=typeof(Promise); break;
              case "not": cg.EmitCall(typeof(Ops), "IsTrue"); break;
              case "string-null?": cg.EmitPropGet(typeof(string), "Length"); break;
            }
            if(etype==typeof(bool))
            { if(type!=null) cg.ILG.Emit(OpCodes.Isinst, type);
              else etype=typeof(Node.negbool);
            }
            else
            { if(type!=null) cg.ILG.Emit(OpCodes.Isinst, type);
              EmitFromBool(cg, type!=null);
              goto objret;
            }
            goto ret;
          }
          case "string-length": case "vector-length":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            if(name=="string-length")
            { cg.EmitString(Args[0]);
              cg.EmitPropGet(typeof(string), "Length");
            }
            else
            { cg.EmitTypedNode(Args[0], typeof(object[]));
              cg.EmitPropGet(typeof(object[]), "Length");
            }
            if(etype!=typeof(int))
            { cg.ILG.Emit(OpCodes.Box, typeof(int));
              goto objret;
            }
            goto ret;
          case "string-ref":
            CheckArity(2);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            cg.EmitString(Args[0]);
            { Type type=typeof(int);
              Args[1].Emit(cg, ref type);
              if(type!=typeof(int)) cg.EmitCall(typeof(Ops), "ToInt");
            }
            cg.EmitPropGet(typeof(string), "Chars");
            if(etype!=typeof(char))
            { cg.ILG.Emit(OpCodes.Box, typeof(char));
              goto objret;
            }
            goto ret;
          case "vector-ref": case "vector-set!":
            if(name=="vector-ref")
            { CheckArity(2);
              if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            }
            else CheckArity(3);
            cg.EmitTypedNode(Args[0], typeof(object[]));
            { Type type=typeof(int);
              Args[1].Emit(cg, ref type);
              if(type!=typeof(int)) cg.EmitCall(typeof(Ops), "ToInt");
            }
            if(name=="vector-ref") cg.ILG.Emit(OpCodes.Ldelem_Ref);
            else
            { Args[2].Emit(cg);
              if(etype==typeof(void)) { cg.ILG.Emit(OpCodes.Stelem_Ref); goto ret; }
              else
              { Slot tmp = cg.AllocLocalTemp(typeof(object));
                cg.ILG.Emit(OpCodes.Dup);
                tmp.EmitSet(cg);
                cg.ILG.Emit(OpCodes.Stelem_Ref);
                tmp.EmitGet(cg);
                cg.FreeLocalTemp(tmp);
              }
            }
            goto objret;
          case "car": case "cdr":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            cg.EmitPair(Args[0]);
            cg.EmitFieldGet(typeof(Pair), name=="car" ? "Car" : "Cdr");
            goto objret;
          case "char-upcase": case "char-downcase":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            cg.EmitTypedNode(Args[0], typeof(char));
            cg.EmitCall(typeof(char), name=="char-upcase" ? "ToUpper" : "ToLower", new Type[] { typeof(char) });
            if(etype!=typeof(char))
            { cg.ILG.Emit(OpCodes.Box, typeof(char));
              goto objret;
            }
            goto ret;
          case "#%delay":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            cg.EmitTypedNode(Args[0], typeof(IProcedure));
            cg.EmitNew(typeof(Promise), new Type[] { typeof(IProcedure) });
            etype = typeof(Promise);
            goto ret;
          case "bitnot": case "->string":
            CheckArity(1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            break;
          case "-":
            if(Args.Length==1)
            { if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
              opname = "Negate";
              Args[0].Emit(cg);
              break;
            }
            else goto plusetc;
          case "+": case "*": case "/": case "//": case "%": case "bitand": case "bitor": case "bitxor": plusetc:
            CheckArity(2, -1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            for(int i=1; i<Args.Length-1; i++)
            { Args[i].Emit(cg);
              cg.EmitCall(typeof(Ops), opname);
            }
            Args[Args.Length-1].Emit(cg);
            break;
          case "=": case "!=": case "<": case ">": case "<=": case ">=":
            CheckArity(2, -1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            if(Args.Length!=2) goto normal;
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            if(etype==typeof(bool))
            { switch(name)
              { case "=": case "!=":
                  cg.EmitCall(typeof(Ops), "EqvP");
                  if(name=="!=") etype = typeof(negbool);
                  break;
                default:
                  cg.EmitCall(typeof(Ops), "Compare");
                  cg.EmitInt(0);
                  switch(name)
                  { case "<":  cg.ILG.Emit(OpCodes.Clt); break;
                    case "<=": cg.ILG.Emit(OpCodes.Cgt); etype=typeof(negbool); break;
                    case ">":  cg.ILG.Emit(OpCodes.Cgt); break;
                    case ">=": cg.ILG.Emit(OpCodes.Clt); etype=typeof(negbool); break;
                  }
                  break;
              }
              goto ret;
            }
            break;
          case "expt": case "lshift": case "rshift":
            CheckArity(2);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            break;
          case "exptmod":
            CheckArity(3);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            Args[0].Emit(cg);
            Args[1].Emit(cg);
            Args[2].Emit(cg);
            break;
          case "values":
            CheckArity(1, -1);
            if(etype==typeof(void)) { EmitVoids(cg); goto ret; }
            if(Args.Length==1) { cg.EmitExpression(Args[0]); goto objret; }
            else
            { cg.EmitObjectArray(Args);
              cg.EmitNew(typeof(MultipleValues), new Type[] { typeof(object[]) });
              etype = typeof(MultipleValues);
            }
            goto ret;
          default: goto normal;
        }

        if(Tail && InTry==null) cg.ILG.Emit(OpCodes.Tailcall);
        cg.EmitCall(typeof(Ops), opname);
        objret:
        etype = typeof(object);
        ret:
        TailReturn(cg);
        return;
      }
    }

    normal:
    cg.EmitTypedNode(Function, typeof(IProcedure));
    cg.EmitObjectArray(Args);
    if(Tail && InTry==null) cg.ILG.Emit(OpCodes.Tailcall);
    cg.EmitCall(typeof(IProcedure), "Call");
    etype = typeof(object);
    TailReturn(cg);
  }
  #endregion
  
  #region Evaluate
  public override object Evaluate()
  { IProcedure proc = Ops.ExpectProcedure(Function.Evaluate()); // this is up here to keep the same evaluation order
    object[] a = MakeObjectArray(Args);

    if(IsConstant)
    { string name = ((VariableNode)Function).Name.String;
      try
      { switch(name)
        { case "+":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Add(ret, a[i]);
            return ret;
          }
          case "-":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Subtract(ret, a[i]);
            return ret;
          }
          case "*":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Multiply(ret, a[i]);
            return ret;
          }
          case "/":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Divide(ret, a[i]);
            return ret;
          }
          case "//":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.FloorDivide(ret, a[i]);
            return ret;
          }
          case "%":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.Modulus(ret, a[i]);
            return ret;
          }
          case "expt": CheckArity(2); return Ops.Power(a[0], a[1]);
          case "exptmod": CheckArity(3); return Ops.PowerMod(a[0], a[1], a[2]);
          case "bitnot": CheckArity(1); return Ops.BitwiseNegate(a[0]);
          case "bitand":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.BitwiseAnd(ret, a[i]);
            return ret;
          }
          case "bitor":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.BitwiseOr(ret, a[i]);
            return ret;
          }
          case "bitxor":
          { CheckArity(2, -1);
            object ret = a[0];
            for(int i=1; i<a.Length; i++) ret = Ops.BitwiseXor(ret, a[i]);
            return ret;
          }
          case "lshift": CheckArity(2); return Ops.LeftShift(a[0], a[1]);
          case "rshift": CheckArity(2); return Ops.RightShift(a[0], a[1]);
          case "=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(!Ops.EqvP(a[i], a[i+1])) return Ops.FALSE;
            return Ops.TRUE;
          }
          case "!=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.EqvP(a[i], a[i+1])) return Ops.FALSE;
            return Ops.TRUE;
          }
          case "<":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])>=0) return Ops.FALSE;
            return Ops.TRUE;
          }
          case "<=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])>0) return Ops.FALSE;
            return Ops.TRUE;
          }
          case ">":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])<=0) return Ops.FALSE;
            return Ops.TRUE;
          }
          case ">=":
          { CheckArity(2, -1);
            for(int i=0; i<a.Length-1; i++) if(Ops.Compare(a[i], a[i+1])<0) return Ops.FALSE;
            return Ops.TRUE;
          }
        }
      }
      catch(Exception e) { throw new ArgumentException(name+": "+e.Message); }
      
      switch(name)
      { case "->string": CheckArity(1); return Ops.Str(a[0]);
        case "car": CheckArity(1); CheckType(a, 0, typeof(Pair)); return ((Pair)a[0]).Car;
        case "cdr": CheckArity(1); CheckType(a, 0, typeof(Pair)); return ((Pair)a[0]).Cdr;
        case "char?": CheckArity(1); return a[0] is char;
        case "char-downcase": CheckArity(1); CheckType(a, 0, typeof(char)); return char.ToLower((char)a[0]);
        case "char-upcase": CheckArity(1); CheckType(a, 0, typeof(char)); return char.ToUpper((char)a[0]);
        case "eq?": CheckArity(2); return a[0]==a[1];
        case "eqv?": CheckArity(2); return Ops.EqvP(a[0], a[1]);
        case "equal?": CheckArity(2); return Ops.EqualP(a[0], a[1]);
        case "not": CheckArity(1); return !Ops.IsTrue(a[0]);
        case "null?": CheckArity(1); return a[0]==null;
        case "pair?": CheckArity(1); return a[0] is Pair;
        case "procedure?": CheckArity(1); return a[0] is IProcedure;
        case "promise?": CheckArity(1); return a[0] is Promise;
        case "string?": CheckArity(1); return a[0] is string;
        case "string-length":
          CheckArity(1); CheckType(a, 0, typeof(string));
          return ((string)a[0]).Length;
        case "string-null?": CheckArity(1); return a[0] is string && (string)a[0]=="";
        case "string-ref":
          CheckArity(2); CheckType(a, 0, typeof(string)); CheckType(a, 1, typeof(int));
          return ((string)a[0])[Ops.ToInt(a[1])];
        case "symbol?": CheckArity(1); return a[0] is Symbol;
        case "values": CheckArity(1, -1); return a.Length==1 ? a[0] : new MultipleValues(a);
        case "vector?": CheckArity(1); return a[0] is object[];
        case "vector-ref":
          CheckArity(2); CheckType(a, 0, typeof(object[])); CheckType(a, 1, typeof(int));
          return ((object[])a[0])[Ops.ToInt(a[1])];
        case "vector-length":
          CheckArity(1); CheckType(a, 0, typeof(object[]));
          return ((object[])a[0]).Length;
        default: throw new NotImplementedException("unhandled inline: "+name);
      }
    }
    
    return proc.Call(a);
  }
  #endregion

  #region GetNodeType
  public override Type GetNodeType()
  { if(Options.Optimize && Function is VariableNode)
      switch(((VariableNode)Function).Name.String)
      { case "eq?": case "eqv?": case "equal?":
        case "null?": case "pair?": case "char?": case "symbol?": case "string?": case "procedure?":
        case "not": case "string-null?":
          return typeof(bool);
        case "string-ref": case "char-upcase": case "char-downcase":
          return typeof(char);
        case "string-length":
          return typeof(int);
      }
    return typeof(object);
  }
  #endregion

  public override void MarkTail(bool tail)
  { Tail=tail;
    Function.MarkTail(false);
    foreach(Node n in Args) n.MarkTail(false);
  }

  public override void Optimize()
  { bool isconst=true;
    for(int i=0; i<Args.Length; i++) if(!Args[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst && Function is VariableNode && IsConstFunc(((VariableNode)Function).Name.String);
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { Function.Walk(w);
      foreach(Node n in Args) n.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node Function;
  public readonly Node[] Args;
  
  void CheckArity(int min) { CheckArity(min, min); }
  void CheckArity(int min, int max) { Ops.CheckArity(((VariableNode)Function).Name.String, Args.Length, min, max); }

  void CheckType(object[] args, int num, Type type)
  { if(AreEquivalent(args[num]==null ? null : args[num].GetType(), type)) return;
    try
    { if(type==typeof(int)) { Ops.ExpectInt(args[num]); return; }
    }
    catch { }
    throw new ArgumentException(string.Format("{0}: for argument {1}, expects type {2} but received {3}",
                                              ((VariableNode)Function).Name.String, num, Ops.TypeName(type),
                                              Ops.TypeName(args[num])));
  }

  void EmitVoids(CodeGenerator cg) { for(int i=0; i<Args.Length; i++) Args[i].EmitVoid(cg); }

  static void EmitFromBool(CodeGenerator cg, bool brtrue)
  { Label yes=cg.ILG.DefineLabel(), end=cg.ILG.DefineLabel();
    cg.ILG.Emit(brtrue ? OpCodes.Brtrue_S : OpCodes.Brfalse_S, yes);
    cg.EmitFieldGet(typeof(Ops), "FALSE");
    cg.ILG.Emit(OpCodes.Br_S, end);
    cg.ILG.MarkLabel(yes);
    cg.EmitFieldGet(typeof(Ops), "TRUE");
    cg.ILG.MarkLabel(end);
  }

  static bool FuncNameMatch(Name var, LambdaNode func)
  { Name binding = func.Binding;
    return binding!=null && var.Index==binding.Index && var.String==binding.String &&
           var.Depth==binding.Depth+(func.MaxNames!=0 ? 1 : 0);
  }
  
  static bool IsConstFunc(string name) { return Array.BinarySearch(constant, name)>=0; }
}
#endregion

#region DebugNode
public abstract class DebugNode : Node
{ public override Type GetNodeType() { return typeof(void); }
  public override void Walk(IWalker w) { }
}
#endregion

#region DefineNode
public sealed class DefineNode : Node
{ public DefineNode(string name, Node value)
  { Name=new Name(name, Name.Global); Value=value;
    if(value is LambdaNode) ((LambdaNode)value).Name = name;
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { Debug.Assert(InFunc==null);
    cg.MarkPosition(this);
    cg.EmitTopLevel();
    cg.EmitString(Name.String);
    Value.Emit(cg);
    cg.EmitCall(typeof(TopLevel), "Bind", new Type[] { typeof(string), typeof(object) });
    cg.Namespace.GetSlot(Name); // side effect of creating the slot
    if(etype!=typeof(void))
    { cg.EmitConstantObject(Symbol.Get(Name.String));
      etype = typeof(Symbol);
    }
    TailReturn(cg);
  }

  public override object Evaluate()
  { Debug.Assert(InFunc==null);
    TopLevel.Current.Bind(Name.String, Value.Evaluate());
    return Symbol.Get(Name.String);
  }

  public override Type GetNodeType() { return typeof(Symbol); }

  public override void MarkTail(bool tail) { Tail=tail; Value.MarkTail(false); }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Value.Walk(w);
    w.PostWalk(this);
  }

  public readonly Name Name;
  public readonly Node Value;
}
#endregion

#region IfNode
public sealed class IfNode : Node
{ public IfNode(Node test, Node iftrue, Node iffalse) { Test=test; IfTrue=iftrue; IfFalse=iffalse; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(IsConstant)
    { EmitConstant(cg, Evaluate(), ref etype);
      TailReturn(cg);
    }
    else
    { Label endlbl=Tail ? new Label() : cg.ILG.DefineLabel(), falselbl=cg.ILG.DefineLabel();
      Type truetype=IfTrue.GetNodeType(), falsetype=IfFalse==null ? null : IfFalse.GetNodeType();
      if(truetype!=falsetype || !Compatible(truetype, etype)) truetype=falsetype=etype=typeof(object);
      else etype=truetype;

      cg.MarkPosition(this);
      object ttype = MaybeEmitBranch(cg, Test, falselbl, false);
      if(ttype==null)
      { IfTrue.Emit(cg, ref truetype);
        Debug.Assert(Compatible(truetype, etype));
        if(!Tail) cg.ILG.Emit(OpCodes.Br, endlbl);
        cg.ILG.MarkLabel(falselbl);
        cg.EmitExpression(IfFalse, ref falsetype);
        Debug.Assert(Compatible(falsetype, etype));
      }
      else
      { if((bool)ttype) IfTrue.Emit(cg, ref truetype);
        else cg.EmitExpression(IfFalse, ref falsetype);
        cg.ILG.MarkLabel(falselbl);
      }

      if(!Tail) cg.ILG.MarkLabel(endlbl);
      else if(IfFalse==null) TailReturn(cg);
    }
  }

  public override object Evaluate()
  { if(Ops.IsTrue(Test.Evaluate())) return IfTrue.Evaluate();
    else if(IfFalse!=null) return IfFalse.Evaluate();
    else return null;
  }

  public override Type GetNodeType()
  { if(IsConstant)
      return Ops.IsTrue(Test.Evaluate()) ? IfTrue.GetNodeType() : IfFalse!=null ? IfFalse.GetNodeType() : null;
    Type truetype=IfTrue.GetNodeType(), falsetype=IfFalse==null ? null : IfFalse.GetNodeType();
    return truetype==falsetype ? truetype : typeof(object);
  }

  public override void MarkTail(bool tail)
  { Tail=tail;
    Test.MarkTail(false);
    IfTrue.MarkTail(tail);
    if(IfFalse!=null) IfFalse.MarkTail(tail);
  }

  public override void Optimize()
  { if(Test.IsConstant)
    { bool test = Ops.IsTrue(Test.Evaluate());
      IsConstant = test && IfTrue.IsConstant || !test && (IfFalse==null || IfFalse.IsConstant);
    }
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { Test.Walk(w);
      IfTrue.Walk(w);
      if(IfFalse!=null) IfFalse.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node Test, IfTrue, IfFalse;
}
#endregion

#region LambdaNode
public class LambdaNode : Node
{ public LambdaNode(Node body) { Parameters=new Name[0]; Body=body; }
  public LambdaNode(string[] names, bool hasList, Node body)
  { Body=body; HasList=hasList; MaxNames=names.Length;

    Parameters=new Name[names.Length];
    for(int i=0; i<names.Length; i++) Parameters[i] = new Name(names[i], 0, i);
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { cg.MarkPosition(this);
    if(etype!=typeof(void))
    { index = lindex.Next;
      CodeGenerator impl = MakeImplMethod(cg);

      Slot tmpl;
      if(!cg.TypeGenerator.GetNamedConstant("template"+index, typeof(Template), out tmpl))
      { CodeGenerator icg = cg.TypeGenerator.GetInitializer();
        icg.ILG.Emit(OpCodes.Ldftn, (MethodInfo)impl.MethodBase);
        icg.EmitString(Name!=null ? Name : Binding!=null ? Binding.String : null);
        icg.EmitInt(Parameters.Length);
        icg.EmitBool(HasList);
        icg.EmitBool(ArgsClosed);
        icg.EmitNew(typeof(Template), new Type[] { typeof(IntPtr), typeof(string), typeof(int),
                                                   typeof(bool), typeof(bool) });
        tmpl.EmitSet(icg);
      }

      tmpl.EmitGet(cg);
      cg.EmitArgGet(0);
      cg.EmitNew(RG.ClosureType, new Type[] { typeof(Template), typeof(LocalEnvironment) });
      etype = RG.ClosureType;
    }
    TailReturn(cg);
  }

  public override object Evaluate()
  { string[] names = new string[Parameters.Length];
    for(int i=0; i<names.Length; i++) names[i] = Parameters[i].String;
    return new InterpretedProcedure(Name!=null ? Name : Binding!=null ? Binding.String : null, names, HasList, Body);
  }

  public override Type GetNodeType() { return RG.ClosureType; }

  public override void MarkTail(bool tail)
  { Tail=tail;
    Body.MarkTail(true);
  }

  public override void Preprocess()
  { base.Preprocess();
    Walk(new NodeDecorator((LambdaNode)this));
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) Body.Walk(w);
    w.PostWalk(this);
  }

  public readonly Name[] Parameters;
  public readonly Node Body;
  public Name Binding;
  public string Name;
  public Label StartLabel;
  public int MaxNames;
  public readonly bool HasList;
  public bool ArgsClosed;

  protected CodeGenerator MakeImplMethod(CodeGenerator cg)
  { CodeGenerator icg;
    icg = cg.TypeGenerator.DefineStaticMethod("lambda$" + index, typeof(object),
                                              new Type[] { typeof(LocalEnvironment), typeof(object[]) });

    icg.Namespace = new LocalNamespace(cg.Namespace, icg);
    if(MaxNames!=0)
    { icg.EmitArgGet(0);
      if(Parameters.Length!=0) icg.EmitArgGet(1);
      if(MaxNames==Parameters.Length)
        icg.EmitNew(typeof(LocalEnvironment), new Type[] { typeof(LocalEnvironment), typeof(object[]) });
      else
      { icg.EmitInt(MaxNames);
        icg.EmitNew(typeof(LocalEnvironment),
                    Parameters.Length==0 ? new Type[] { typeof(LocalEnvironment), typeof(int) }
                                         : new Type[] { typeof(LocalEnvironment), typeof(object[]), typeof(int) });
      }
      icg.EmitArgSet(0);
    }

    StartLabel = icg.ILG.DefineLabel();
    icg.ILG.MarkLabel(StartLabel);
    Body.Emit(icg);
    icg.Finish();
    return icg;
  }

  sealed class NodeDecorator : IWalker
  { public NodeDecorator(LambdaNode top) { func=this.top=top; }

    public bool Walk(Node node)
    { node.InFunc = func;
      node.InTry  = inTry;

      if(node is LambdaNode)
      { LambdaNode oldFunc=func;
        TryNode oldTry;
        int oldFree=freeStart, oldBound=boundStart;
        bool oldCatch=inCatch;

        func  = (LambdaNode)node;
        oldTry = null;
        freeStart = free.Count;
        boundStart = bound.Count;
        inCatch = false;

        foreach(Name name in func.Parameters)
        { bound.Add(name);
          values.Add(null);
        }

        func.Body.Walk(this);

        for(int i=freeStart; i<free.Count; i++)
        { Name name = (Name)free[i];
          int index = IndexOf(name.String, bound, oldBound, boundStart);
          if(index==-1)
          { if(oldFunc==top/* || oldFunc is ModuleNode*/) name.Depth=Backend.Name.Global; // TODO: uncomment later?
            else
            { if(func.MaxNames!=0) name.Depth++;
              free[freeStart++] = name;
            }
          }
          else
          { Name bname = (Name)bound[index];
            int argPos = IndexOf(name.String, oldFunc.Parameters);
            if(bname.Depth==Backend.Name.Local && argPos==-1)
            { bname.Depth = 0;
              bname.Index = name.Index = oldFunc.MaxNames++;
            }
            else
            { if(argPos!=-1) oldFunc.ArgsClosed=true;
              name.Index=bname.Index;
            }
            if(func.MaxNames!=0) name.Depth++;

            LambdaNode lambda = values[index] as LambdaNode;
            if(lambda!=null) lambda.Binding = name;
          }
        }

        values.RemoveRange(boundStart, bound.Count-boundStart);
        bound.RemoveRange(boundStart, bound.Count-boundStart);
        free.RemoveRange(freeStart, free.Count-freeStart);
        func=oldFunc; boundStart=oldBound; freeStart=oldFree; inTry=oldTry; inCatch=oldCatch;
        return false;
      }
      else if(node is LetNode)
      { LetNode let = (LetNode)node;
        foreach(Node n in let.Inits) if(n!=null) n.Walk(this);
        for(int i=0; i<let.Names.Length; i++)
        { bound.Add(let.Names[i]);
          values.Add(let.Inits[i]==null ? Backend.Binding.Unbound : let.Inits[i]);
        }
        let.Body.Walk(this);
        return false;
      }
      else if(node is LetValuesNode)
      { LetValuesNode let = (LetValuesNode)node;
        foreach(Node n in let.Inits) n.Walk(this);
        foreach(Name[] names in let.Names)
          foreach(Name name in names) { bound.Add(name); values.Add(null); }
        let.Body.Walk(this);
        return false;
      }
      else if(node is VariableNode) HandleLocalReference(ref ((VariableNode)node).Name, null);
      else if(node is SetNode)
      { SetNode set = (SetNode)node;
        for(int i=0; i<set.Names.Length; i++) HandleLocalReference(ref set.Names[i], set.Values[i]);
      }
      else if(!inCatch && node is ThrowNode && ((ThrowNode)node).Type==null)
        throw Ops.SyntaxError(node, "type-less throw form is only allowed within a catch statement");
      else if(node is TryNode)
      { TryNode oldTry=inTry, tn=(TryNode)node;
        inTry = tn;

        tn.Body.Walk(this);

        if(tn.Excepts!=null)
          foreach(TryNode.Except ex in tn.Excepts)
          { if(ex.Types!=null)
              for(int i=0; i<ex.Types.Length; i++) HandleLocalReference(ref ex.Types[i].Name, null);
            if(ex.Var!=null)
            { bound.Add(ex.Var);
              values.Add(null);
            }
            inCatch = true;
            ex.Body.Walk(this);
            inCatch = false;
            if(ex.Var!=null)
            { bound.RemoveAt(bound.Count-1);
              values.RemoveAt(values.Count-1);
            }
          }
        
        if(tn.Finally!=null) tn.Finally.Walk(this);

        inTry = oldTry;
        return false;
      }
      return true;
    }

    public void PostWalk(Node node)
    { if(node is LetNode)
      { LetNode let = (LetNode)node;
        int len=let.Names.Length, start=bound.Count-len;

        for(int i=start; i<values.Count; i++)
        { LambdaNode lambda = values[i] as LambdaNode;
          if(lambda!=null) lambda.Binding = (Name)bound[i];
        }

        bound.RemoveRange(start, len);
        values.RemoveRange(start, len);
      }
      else if(node is LetValuesNode)
      { LetValuesNode let = (LetValuesNode)node;
        int len=0, start;
        foreach(Name[] names in let.Names) len += names.Length;
        start = bound.Count-len;
        bound.RemoveRange(start, len);
        values.RemoveRange(start, len);
      }
      else if(node is DefineNode)
      { DefineNode def = (DefineNode)node;
        if(def.InFunc==top/* || def.InFunc is ModuleNode*/) def.InFunc=null; // TODO: uncomment later?
        else if(def.InFunc!=null) throw Ops.SyntaxError(node, "define: only allowed at toplevel scope");
      }
    }

    int IndexOf(string name, IList list) // these are kind of DWIMish
    { if(list==bound) return IndexOf(name, list, boundStart, list.Count);
      if(list==free)  return IndexOf(name, list, freeStart, list.Count);
      return IndexOf(name, list, 0, list.Count);
    }

    void HandleLocalReference(ref Name name, Node assign)
    { int index = IndexOf(name.String, bound);
      if(index==-1)
      { if(func==top) name.Depth=Backend.Name.Global;
        else
        { index = IndexOf(name.String, free);
          if(index==-1) { free.Add(name); name.Depth=0; }
          else name = (Name)free[index];
        }
      }
      else
      { name = (Name)bound[index];
        if(assign!=null)
        { if(values[index]==Backend.Binding.Unbound) values[index]=assign;
          else values[index]=null;
        }
      }
    }

    LambdaNode func, top;
    TryNode inTry;
    ArrayList bound=new ArrayList(), free=new ArrayList(), values=new ArrayList();
    int boundStart, freeStart;
    bool inCatch;

    static int IndexOf(string name, IList list, int start, int end)
    { for(end--; end>=start; end--) if(((Name)list[end]).String==name) return end;
      return -1;
    }
  }

  long index;

  static Index lindex = new Index();
}
#endregion

#region ListNode
public sealed class ListNode : Node
{ public ListNode(Node[] items, Node dot) { Items=items; Dot=dot; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(!IsConstant || etype!=typeof(void))
    { if(IsConstant) cg.EmitConstantObject(Evaluate());
      else
      { cg.MarkPosition(this);
        cg.EmitList(Items, Dot);
      }
      etype = Items.Length==0 && Dot==null ? null : typeof(Pair);
    }
    TailReturn(cg);
  }

  public override object Evaluate()
  { object obj = Dot==null ? null : Dot.Evaluate();
    for(int i=Items.Length-1; i>=0; i--) obj = new Pair(Items[i].Evaluate(), obj);
    return obj;
  }

  public override Type GetNodeType() { return Items.Length==0 ? null : typeof(Pair); }

  public override void Optimize()
  { bool isconst=(Dot==null || Dot.IsConstant);
    if(isconst) for(int i=0; i<Items.Length; i++) if(!Items[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { foreach(Node n in Items) n.Walk(w);
      if(Dot!=null) Dot.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node[] Items;
  public readonly Node Dot;
}
#endregion

#region LiteralNode
public sealed class LiteralNode : Node
{ public LiteralNode(object value) { Value=value; }
  public override void Emit(CodeGenerator cg, ref Type etype)
  { EmitConstant(cg, Value, ref etype);
    TailReturn(cg);
  }
  public override object Evaluate() { return Value; }
  public override Type GetNodeType() { return Value==null ? null : Value.GetType(); }
  public override void Optimize() { IsConstant = true; }

  public readonly object Value;
}
#endregion

#region LetNode
public sealed class LetNode : Node
{ public LetNode(string[] names, Node[] inits, Node body)
  { Inits=inits; Body=body;

    Names = new Name[names.Length];
    for(int i=0; i<names.Length; i++) Names[i] = new Name(names[i]);
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { cg.MarkPosition(this);
    for(int i=0; i<Inits.Length; i++)
      if(Inits[i]!=null)
      { Inits[i].Emit(cg);
        cg.EmitSet(Names[i]);
      }
      else if(Options.Debug)
      { cg.EmitFieldGet(typeof(Binding), "Unbound");
        cg.EmitSet(Names[i]);
      }
    Body.Emit(cg, ref etype);
    for(int i=0; i<Names.Length; i++) cg.Namespace.RemoveSlot(Names[i]);
  }

  public override object Evaluate()
  { if(IsConstant || Inits.Length==0) return Body.Evaluate();

    InterpreterEnvironment ne, old=InterpreterEnvironment.Current;
    try
    { InterpreterEnvironment.Current = ne = new InterpreterEnvironment(old);
      for(int i=0; i<Inits.Length; i++)
        ne.Bind(Names[i].String, Inits[i]==null ? null : Inits[i].Evaluate());
      return Body.Evaluate();
    }
    finally { InterpreterEnvironment.Current=old; }
  }

  public override Type GetNodeType() { return Body.GetNodeType(); }

  public override void MarkTail(bool tail)
  { foreach(Node n in Inits) if(n!=null) n.MarkTail(false);
    Body.MarkTail(tail);
  }

  public override void Optimize()
  { bool isconst = Body.IsConstant;
    if(isconst) for(int i=0; i<Inits.Length; i++) if(Inits[i]!=null && !Inits[i].IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { foreach(Node n in Inits) if(n!=null) n.Walk(w);
      Body.Walk(w);
    }
    w.PostWalk(this);
  }

  public Name[] Names;
  public Node[] Inits;
  public Node Body;
}
#endregion

#region LetValuesNode
public sealed class LetValuesNode : Node
{ public LetValuesNode(Name[][] names, Node[] inits, Node body) { Names=names; Inits=inits; Body=body; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(IsConstant) EmitConstant(cg, Evaluate(), ref etype);
    else
    { cg.MarkPosition(this);
      for(int i=0; i<Names.Length; i++)
      { Name[] bindings = Names[i];
        Label end = new Label();
        bool useEnd = false;

        object constValue = this; // this means "not set"
        { int constLength;
          if(Inits[i].IsConstant)
          { constValue = Inits[i].Evaluate();
            MultipleValues mv = constValue as MultipleValues;
            if(mv==null) constLength = -1;
            else { constLength=mv.Values.Length; constValue=mv.Values; }
          }
          else if(Options.Optimize && Inits[i] is CallNode)
          { CallNode cn = (CallNode)Inits[i];
            if(cn.Function is VariableNode && ((VariableNode)cn.Function).Name.String=="values")
            { constLength = cn.Args.Length;
              if(constLength<bindings.Length) goto checkLength;
              object[] values = new object[bindings.Length];
              for(int j=0; j<bindings.Length; j++)
                values[j] = cn.Args[j].IsConstant ? cn.Args[j].Evaluate() : cn.Args[j];
              constValue = values;
            }
            else goto skip;
          }
          else goto skip;
          checkLength:
          if(constLength!=-2 && (constLength==-1 ? 1 : constLength) < bindings.Length)
            throw Ops.SyntaxError("expected at least "+bindings.Length.ToString()+" values, but received "+
                                  constLength.ToString());
        }

        skip:
        if(constValue==this)
        { if(bindings.Length!=1)
          { cg.EmitTypedNode(Inits[i], typeof(MultipleValues));
            cg.EmitInt(bindings.Length);
            cg.EmitCall(typeof(Ops), "CheckValues");
          }
          else
          { Type itype = typeof(MultipleValues);
            Inits[i].Emit(cg, ref itype);
            if(itype==typeof(MultipleValues))
            { cg.EmitInt(bindings.Length);
              cg.EmitCall(typeof(Ops), "CheckValues");
            }
            else
            { Slot tmp = cg.AllocLocalTemp(typeof(object));
              Label loop = cg.ILG.DefineLabel();
              end = cg.ILG.DefineLabel();
              useEnd = true;

              cg.ILG.Emit(OpCodes.Dup);
              tmp.EmitSet(cg);
              cg.ILG.Emit(OpCodes.Isinst, typeof(MultipleValues));
              cg.ILG.Emit(OpCodes.Dup);
              cg.ILG.Emit(OpCodes.Brtrue_S, loop);
              cg.ILG.Emit(OpCodes.Pop);
              tmp.EmitGet(cg);
              cg.EmitSet(bindings[0]);
              cg.ILG.Emit(OpCodes.Br, end);

              cg.FreeLocalTemp(tmp);
              cg.ILG.MarkLabel(loop);
            }
          }
          for(int j=0; j<bindings.Length; j++)
          { if(j!=bindings.Length-1) cg.ILG.Emit(OpCodes.Dup);
            cg.EmitInt(j);
            cg.ILG.Emit(OpCodes.Ldelem_Ref);
            cg.EmitSet(bindings[j]);
          }
          if(useEnd) cg.ILG.MarkLabel(end);
        }
        else if(constValue is object[])
        { object[] values = (object[])constValue;
          for(int j=0; j<bindings.Length; j++)
          { if(values[j] is Node) ((Node)values[j]).Emit(cg);
            else cg.EmitConstantObject(values[j]);
            cg.EmitSet(bindings[j]);
          }
        }
        else
        { Debug.Assert(bindings.Length==1);
          cg.EmitConstantObject(constValue);
          cg.EmitSet(bindings[0]);
        }
      }
      Body.Emit(cg, ref etype);
    }
  }

  public override object Evaluate()
  { if(IsConstant) return Body.Evaluate();

    InterpreterEnvironment ne, old=InterpreterEnvironment.Current;
    try
    { InterpreterEnvironment.Current = ne = new InterpreterEnvironment(old);
      for(int i=0; i<Names.Length; i++)
      { Name[] bindings = Names[i];
        object value = Inits[i].Evaluate();
        if(bindings.Length==1 && !(value is MultipleValues)) ne.Bind(bindings[0].String, value);
        else
        { object[] values = Ops.CheckValues(Ops.ExpectValues(value), bindings.Length);
          for(int j=0; j<bindings.Length; j++) ne.Bind(bindings[j].String, values[j]);
        }
      }
      return Body.Evaluate();
    }
    finally { InterpreterEnvironment.Current=old; }
  }

  public override Type GetNodeType() { return Body.GetNodeType(); }

  public override void MarkTail(bool tail)
  { Tail=tail;
    foreach(Node n in Inits) n.MarkTail(false);
    Body.MarkTail(tail);
  }

  public override void Optimize()
  { bool isconst = true;
    foreach(Node n in Inits) if(!n.IsConstant) { isconst=false; break; }
    IsConstant = isconst && Body.IsConstant;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { foreach(Node n in Inits) n.Walk(w);
      Body.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Name[][] Names;
  public readonly Node[] Inits;
  public readonly Node Body;
}
#endregion

#region MarkSourceNode
public sealed class MarkSourceNode : DebugNode
{ public MarkSourceNode(string file, string code) { File=file; Code=code; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { cg.TypeGenerator.Assembly.Symbols =
      cg.TypeGenerator.Assembly.Module.DefineDocument(File, Guid.Empty, Guid.Empty, Guid.Empty);
    // TODO: figure this out. cg.TypeGenerator.Assembly.Symbols.SetSource(System.Text.Encoding.UTF8.GetBytes(Code));
    cg.MarkPosition(1, 0, 1, 0);
    if(etype!=typeof(void))
    { cg.ILG.Emit(OpCodes.Ldnull);
      etype = typeof(object);
    }
  }

  public readonly string File, Code;
}
#endregion

#region SetNode
public sealed class SetNode : Node
{ public SetNode(Name[] names, Node[] values)
  { foreach(Name name in names)
      if(name.String.IndexOf('.', 1)!=-1) throw new SyntaxErrorException("Unable to set a dotted identifier");
    Names=names; Values=values;
  }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { cg.MarkPosition(this);
    for(int i=0; i<Names.Length; i++)
    { Values[i].Emit(cg);
      if(i==Names.Length-1 && etype!=typeof(void))
      { cg.ILG.Emit(OpCodes.Dup);
        etype = typeof(object);
      }
      cg.EmitSet(Names[i]);
    }
    TailReturn(cg);
  }

  public override object Evaluate()
  { InterpreterEnvironment cur = InterpreterEnvironment.Current;
    object value=null;
    for(int i=0; i<Names.Length; i++)
    { value = Values[i].Evaluate();
      if(Names[i].Depth==Name.Global || cur==null) TopLevel.Current.Set(Names[i].String, value);
      else cur.Set(Names[i].String, value);
    }
    return value;
  }

  public override Type GetNodeType() { return Values[Values.Length-1].GetNodeType(); }

  public override void MarkTail(bool tail)
  { Tail=tail;
    foreach(Node n in Values) n.MarkTail(false);
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) foreach(Node n in Values) n.Walk(w);
    w.PostWalk(this);
  }

  public Name[] Names;
  public readonly Node[] Values;
}
#endregion

#region ThrowNode
public sealed class ThrowNode : Node
{ public ThrowNode(string type, Node[] objects) { Type = type==null ? null : new VariableNode(type); Objects=objects; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { cg.MarkPosition(this);
    if(Type==null) cg.ILG.Emit(OpCodes.Rethrow);
    else
    { Type ttype = typeof(Type);
      Type.Emit(cg, ref ttype);
      if(ttype!=typeof(Type)) cg.EmitCall(typeof(Ops), "ExpectType");
      if(Objects==null) cg.ILG.Emit(OpCodes.Ldnull);
      else cg.EmitObjectArray(Objects);
      cg.EmitCall(typeof(Ops), "MakeException");
      cg.ILG.Emit(OpCodes.Throw);
      if(etype!=typeof(void))
      { cg.ILG.Emit(OpCodes.Ldnull);
        etype = typeof(object);
      }
    }
  }

  public override Type GetNodeType() { return typeof(void); }
  
  public override object Evaluate()
  { if(Type==null) throw (Exception)Ops.ExceptionStack.Peek();
    throw Ops.MakeException(Ops.ExpectType(Type.Evaluate()), Objects==null ? null : MakeObjectArray(Objects));
  }

  public override void MarkTail(bool tail)
  { Tail=false;
    if(Type!=null)
    { Type.MarkTail(false);
      if(Objects!=null) foreach(Node n in Objects) n.MarkTail(false);
    }
  }
  
  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { if(Type!=null)
      { Type.Walk(w);
        if(Objects!=null) foreach(Node n in Objects) n.Walk(w);
      }
    }
    w.PostWalk(this);
  }

  public readonly VariableNode Type;
  public readonly Node[] Objects;
}
#endregion

#region TryNode
public sealed class TryNode : Node
{ public TryNode(Node body, Except[] excepts, Node final) { Body=body; Excepts=excepts; Finally=final; }

  public struct Except
  { public Except(string var, string[] types, Node body)
    { Var  = var==null ? null : new Name(var);
      Body = body;
      if(types==null || types.Length==0) Types=null;
      else
      { Types = new VariableNode[types.Length];
        for(int i=0; i<types.Length; i++) Types[i] = new VariableNode(types[i]);
      }
    }
    public Name Var;
    public readonly VariableNode[] Types;
    public readonly Node Body;
  }

  public Label LeaveLabel { get { return InTry==null ? leaveLabel : InTry.LeaveLabel; } }
  public Slot  ReturnSlot { get { return InTry==null ? returnSlot : InTry.ReturnSlot; } }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { returnSlot = etype==typeof(void) ? null : cg.AllocLocalTemp(typeof(object));
    Debug.Assert(returnSlot!=null || !Tail);

    cg.MarkPosition(this);
    leaveLabel = cg.ILG.BeginExceptionBlock();
    if(returnSlot==null)
    { Body.Emit(cg, ref etype);
      if(etype!=typeof(void))
      { etype = typeof(object);
        cg.ILG.Emit(OpCodes.Pop);
      }
    }
    else
    { etype = typeof(object);
      Body.Emit(cg);
      if(!Tail) returnSlot.EmitSet(cg);
    }

    if(Excepts!=null && Excepts.Length!=0)
    { cg.ILG.BeginCatchBlock(typeof(Exception));
      Slot eslot=null;
      bool needRethrow=true;

      foreach(Except ex in Excepts)
      { Label next;
        if(ex.Types==null)
        { needRethrow = false;
          next = new Label();
        }
        else
        { Label body=cg.ILG.DefineLabel();
          next=cg.ILG.DefineLabel();
          if(eslot==null)
          { eslot = cg.AllocLocalTemp(typeof(Exception));
            eslot.EmitSet(cg);
          }
          for(int i=0; i<ex.Types.Length; i++)
          { Type ttype = typeof(Type);
            ex.Types[i].Emit(cg, ref ttype);
            if(ttype!=typeof(Type)) cg.EmitCall(typeof(Ops), "ExpectType");
            eslot.EmitGet(cg);
            cg.EmitCall(typeof(Type), "IsInstanceOfType");
            if(i<ex.Types.Length-1) cg.ILG.Emit(OpCodes.Brtrue, body);
            else cg.ILG.Emit(OpCodes.Brfalse, next);
          }
          cg.ILG.MarkLabel(body);
        }
        if(ex.Var!=null)
        { if(eslot!=null) eslot.EmitGet(cg);
          cg.EmitSet(ex.Var);
        }
        if(returnSlot==null || ex.Body.GetNodeType()==typeof(void)) ex.Body.EmitVoid(cg);
        else
        { ex.Body.Emit(cg);
          returnSlot.EmitSet(cg);
        }
        cg.ILG.Emit(OpCodes.Leave, Tail ? LeaveLabel : leaveLabel);
        if(ex.Types==null) break;
        else cg.ILG.MarkLabel(next);
      }

      if(needRethrow) cg.ILG.Emit(OpCodes.Rethrow);
      if(eslot!=null) cg.FreeLocalTemp(eslot);
    }

    if(Finally!=null)
    { cg.ILG.BeginFinallyBlock();
      Finally.EmitVoid(cg);
    }
    cg.ILG.EndExceptionBlock();

    if(returnSlot!=null)
    { returnSlot.EmitGet(cg);
      cg.FreeLocalTemp(returnSlot);
      returnSlot = null;
      TailReturn(cg);
    }
    else if(etype!=typeof(void)) cg.ILG.Emit(OpCodes.Ldnull);

    leaveLabel = new Label();
  }

  public override object Evaluate()
  { if(Excepts==null)
      try { return Body.Evaluate(); }
      finally { if(Finally!=null) Finally.Evaluate(); }
    else
      try { return Body.Evaluate(); }
      catch(Exception e)
      { foreach(Except ex in Excepts)
        { if(ex.Types!=null)
          { bool isMatch=false;  
            foreach(VariableNode name in ex.Types)
            { Type type = Ops.ExpectType(name.Evaluate());
              if(type.IsInstanceOfType(e)) { isMatch=true; break; }
            }
            if(!isMatch) continue;
          }

          object ret;
          if(Ops.ExceptionStack==null) Ops.ExceptionStack = new Stack();
          Ops.ExceptionStack.Push(e);

          if(ex.Var==null)
            try { ret=ex.Body.Evaluate(); }
            finally { Ops.ExceptionStack.Pop(); }
          else
          { InterpreterEnvironment ne, old=InterpreterEnvironment.Current;
            try
            { InterpreterEnvironment.Current = ne = new InterpreterEnvironment(old);
              ne.Bind(ex.Var.String, e);
              ret = ex.Body.Evaluate();
            }
            finally { InterpreterEnvironment.Current=old; Ops.ExceptionStack.Pop(); }
          }
          return ret;
        }
        throw;
      }
      finally { if(Finally!=null) Finally.Evaluate(); }
  }

  public override Type GetNodeType() { return Body.GetNodeType(); }

  public override void MarkTail(bool tail)
  { Tail=tail;
    Body.MarkTail(tail);
    if(Excepts!=null)
      foreach(Except ex in Excepts)
      { if(ex.Types!=null) foreach(Node n in ex.Types) n.MarkTail(false);
        ex.Body.MarkTail(false);
      }
    if(Finally!=null) Finally.MarkTail(false);
  }
  
  public override void Walk(IWalker w)
  { if(w.Walk(this))
    { Body.Walk(w);
      if(Excepts!=null)
        foreach(Except ex in Excepts)
        { if(ex.Types!=null) foreach(Node n in ex.Types) n.Walk(w);
          ex.Body.Walk(w);
        }
      if(Finally!=null) Finally.Walk(w);
    }
    w.PostWalk(this);
  }

  public readonly Node Body, Finally;
  public readonly Except[] Excepts;
  
  Label leaveLabel;
  Slot returnSlot;
}
#endregion

#region VariableNode
public sealed class VariableNode : Node
{ public VariableNode(string name) { Name = new Name(name); }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(Options.Debug)
    { cg.EmitGet(Name);
      cg.EmitString(Name.String);
      cg.EmitCall(typeof(Ops), "CheckVariable");
      etype = typeof(object);
    }
    else if(etype!=typeof(void))
    { cg.EmitGet(Name);
      etype = typeof(object);
    }
    TailReturn(cg);
  }

  public override object Evaluate()
  { if(Name.Depth==Name.Global) return TopLevel.Current.Get(Name.String);
    else
    { InterpreterEnvironment cur = InterpreterEnvironment.Current;
      return cur==null ? TopLevel.Current.Get(Name.String) : cur.Get(Name.String);
    }
  }

  public override Type GetNodeType() { return typeof(object); }

  public Name Name;
}
#endregion

#region VectorNode
public sealed class VectorNode : Node
{ public VectorNode(Node[] items) { Items=items; }

  public override void Emit(CodeGenerator cg, ref Type etype)
  { if(etype==typeof(void))
    { cg.MarkPosition(this);
      foreach(Node node in Items) if(!node.IsConstant) node.EmitVoid(cg);
    }
    else
    { if(IsConstant) cg.EmitConstantObject(Evaluate());
      else
      { cg.MarkPosition(this);
        cg.EmitObjectArray(Items);
      }
      etype = typeof(object[]);
    }
    TailReturn(cg);
  }

  public override object Evaluate() { return MakeObjectArray(Items); }

  public override Type GetNodeType() { return typeof(object[]); }

  public override void MarkTail(bool tail)
  { Tail=tail;
    foreach(Node node in Items) node.MarkTail(false);
  }

  public override void Optimize()
  { bool isconst=true;
    foreach(Node node in Items) if(!node.IsConstant) { isconst=false; break; }
    IsConstant = isconst;
  }

  public override void Walk(IWalker w)
  { if(w.Walk(this)) foreach(Node n in Items) n.Walk(w);
    w.PostWalk(this);
  }

  public readonly Node[] Items;
}
#endregion

} // namespace NetLisp.Backend
