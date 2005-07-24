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
using System.Reflection;
using System.Reflection.Emit;

namespace NetLisp.Backend
{

public abstract class Snippet
{ public abstract object Run(LocalEnvironment env);
}

public sealed class SnippetMaker
{ private SnippetMaker() { }

  public static void DumpAssembly()
  { Assembly.Save();
    string bn = "snippets"+index.Next;
    Assembly = new AssemblyGenerator(bn, bn+".dll");
  }

  public static Snippet Generate(LambdaNode body) { return Assembly.GenerateSnippet(body); }
  public static Snippet Generate(LambdaNode body, string typeName)
  { return Assembly.GenerateSnippet(body, typeName);
  }

  public static AssemblyGenerator Assembly = new AssemblyGenerator("snippets", "snippets.dll");
  
  static Index index = new Index();
}

} // namespace NetLisp.Backend
