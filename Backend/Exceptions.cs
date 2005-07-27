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

namespace NetLisp.Backend
{

public class NameException : RuntimeException
{ public NameException(string message) : base(message) { }
}

public class CompileTimeException : NetLispException
{ public CompileTimeException(string message) : base(message) { }
}

public class ModuleLoadException : RuntimeException
{ public ModuleLoadException(string message) : base(message) { }
}

public class RuntimeException : NetLispException
{ public RuntimeException() { }
  public RuntimeException(string message) : base(message) { }
}

public class NetLispException : ApplicationException
{ public NetLispException() { }
  public NetLispException(string message) : base(message) { }
}

public class SyntaxErrorException : CompileTimeException
{ public SyntaxErrorException(string message) : base(message) { }
}

public class TypeErrorException : RuntimeException
{ public TypeErrorException(string message) : base(message) { }
}

public class ValueErrorException : RuntimeException
{ public ValueErrorException(string message) : base(message) { }
}

} // namespace NetLisp.Backend
