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
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

#region Namespace
public abstract class Namespace
{ public Namespace(Namespace parent, CodeGenerator cg)
  { Parent  = parent;
    codeGen = cg;
  }

  public Slot GetSlot(Name name) { return GetSlot(name, true); }
  public Slot GetSlot(Name name, bool makeIt)
  { if(name.Depth==Name.Global && Parent!=null) return Parent.GetSlot(name, true);
    Slot ret = (Slot)slots[name];
    if(ret==null)
    { if(Parent!=null) ret = Parent.GetSlot(name, false);
      if(ret==null && makeIt)
        slots[name] = ret = name.Depth==Name.Local ? codeGen.AllocLocalTemp(typeof(object)) : MakeSlot(name);
    }
    return ret;
  }

  public void RemoveSlot(Name name)
  { Slot slot = (Slot)slots[name];
    if(name.Depth==Name.Local) codeGen.FreeLocalTemp(slot);
    slots.Remove(name);
  }

  public Namespace Parent;

  protected abstract Slot MakeSlot(Name name);

  protected HybridDictionary slots = new HybridDictionary();
  protected CodeGenerator codeGen;
}
#endregion

#region LocalNamespace
public sealed class LocalNamespace : Namespace
{ public LocalNamespace(Namespace parent, CodeGenerator cg) : base(parent, cg) { }
  protected override Slot MakeSlot(Name name) { return new EnvironmentSlot(name.Depth, name.Index); }
}
#endregion

#region TopLevelNamespace
public sealed class TopLevelNamespace : Namespace
{ public TopLevelNamespace(CodeGenerator cg) : base(null, cg) { TopSlot = new TopLevelSlot(); }
  public TopLevelNamespace(CodeGenerator cg, Slot top) : base(null, cg) { TopSlot = top; }

  public readonly Slot TopSlot;

  protected override Slot MakeSlot(Name name) { return new NamedFrameSlot(TopSlot, name.String); }
}
#endregion

} // namespace NetLisp.Backend
