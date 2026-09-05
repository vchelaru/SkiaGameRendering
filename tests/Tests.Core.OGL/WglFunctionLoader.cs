using System.Runtime.InteropServices;
using SkiaGameRendering.Core.OGL;
using static Tests.CoreOgl.WglNative;

namespace Tests.CoreOgl;

/// <summary>
/// Resolves real GL entry points against a live WGL context (see <see cref="WglContext"/>) - the
/// standard WGL loader pattern. <c>wglGetProcAddress</c> only returns non-null for functions beyond
/// OpenGL 1.1 (everything <see cref="SkiaGameRendering.Core.OGL.GlFunctions"/> needs - FBOs and
/// renderbuffers are 3.0+); legacy core functions (the raw texture calls this test project adds
/// itself, e.g. glGenTextures) resolve only through <c>GetProcAddress</c> against the loaded
/// <c>opengl32.dll</c> module instead.
/// <para>
/// <c>wglGetProcAddress</c>'s documented failure return is not just <c>NULL</c> - it can also hand
/// back 1, 2, 3, or -1 for an unsupported extension - so every one of those sentinels routes to the
/// <c>GetProcAddress</c> fallback, not just a zero check.
/// </para>
/// </summary>
internal sealed class WglFunctionLoader : IGlFunctionLoader
{
    static readonly HashSet<long> FailureSentinels = new() { 0, 1, 2, 3, -1 };

    readonly IntPtr _opengl32Module;

    public WglFunctionLoader()
    {
        _opengl32Module = GetModuleHandleW("opengl32.dll");
        if (_opengl32Module == IntPtr.Zero)
            throw new InvalidOperationException("opengl32.dll is not loaded in this process.");
    }

    public T Load<T>(string nativeName) where T : Delegate
    {
        var address = wglGetProcAddress(nativeName);
        if (FailureSentinels.Contains(address.ToInt64()))
            address = GetProcAddress(_opengl32Module, nativeName);

        if (address == IntPtr.Zero)
            throw new EntryPointNotFoundException($"Could not resolve GL entry point '{nativeName}'.");

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
}
