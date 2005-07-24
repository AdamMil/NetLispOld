using System;

namespace NetLisp.Backend
{

public class NameException : RuntimeException
{ public NameException(string message) : base(message) { }
}

public abstract class CompileTimeException : NetLispException
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
