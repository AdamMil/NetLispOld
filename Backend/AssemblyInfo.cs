using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("NetLisp Backend")]
[assembly: AssemblyDescription("The backend for the NetLisp language.")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif
[assembly: AssemblyProduct("NetLisp")]
[assembly: AssemblyCopyright("Copyright 2005, Adam Milazzo")]

[assembly: AssemblyVersion("0.1.*")]
