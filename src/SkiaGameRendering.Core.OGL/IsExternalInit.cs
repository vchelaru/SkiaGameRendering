#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    // Lets the netstandard2.1 build (for Unity) compile init accessors and records.
    internal static class IsExternalInit
    {
    }
}
#endif
