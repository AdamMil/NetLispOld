using System;

namespace NetLisp.Backend
{

// TODO: don't duplicate exceptions that already exist (NotImplemented, IOError, etc...)
// TODO: implement python-like exception handling (with the throw type,value / except type,value form)
// TODO: make exceptions accept the same constructor arguments as python's

public class AttributeErrorException : RuntimeException
{ public AttributeErrorException(string message) : base(message) { }
}

public abstract class CompileTimeException : NetLispException
{ public CompileTimeException(string message) : base(message) { }
}

public class FloatingPointErrorException : RuntimeException
{ public FloatingPointErrorException(string message) : base(message) { }
}

public class ImportErrorException : RuntimeException
{ public ImportErrorException(string message) : base(message) { }
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
