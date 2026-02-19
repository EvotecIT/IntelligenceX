#if NETFRAMEWORK || NETSTANDARD2_0
namespace System.Runtime.CompilerServices;

/// <summary>
/// Backport shim for init-only setters on legacy target frameworks.
/// </summary>
internal static class IsExternalInit { }
#endif
