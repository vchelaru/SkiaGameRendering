using System.Runtime.InteropServices;

namespace Tests.Shared;

/// <summary>
/// Makes a vendored <c>opengl32.dll</c> sitting next to the test binary win over the system one, so
/// a GPU-less runner still gets a real OpenGL driver (see the <c>headless-gpu-testing</c> skill for
/// where CI gets Mesa from). Anything that ends up on an OpenGL context calls this before GL is
/// touched at all - <c>WglContext</c> before its first GDI pixel-format call,
/// <see cref="OneFrameGame"/> before SDL loads its GL library.
/// </summary>
static class VendoredOpenGl
{
    /// <summary>
    /// A plain <c>DllImport("opengl32.dll")</c> is not enough by itself: GDI's own
    /// <c>ChoosePixelFormat</c>/<c>SetPixelFormat</c>, and SDL's own load of the GL library, resolve
    /// their reference to <c>opengl32.dll</c> independently of anything this assembly does, and on
    /// modern Windows that resolution is hardened to always come from System32 - so a vendored copy
    /// sitting next to the test binary loses to the real driver for their half even when a later
    /// direct <c>DllImport("opengl32.dll")</c> call from our own code would have found the local one.
    /// The one loophole: Windows always reuses an already-loaded module that matches by file name,
    /// regardless of where it was loaded from or which search rules would otherwise apply - the same
    /// mechanism DLL-proxying/hijacking exploits. Loading the vendored copy here, before anything
    /// else touches "opengl32.dll", makes those later resolutions reuse this module too.
    /// </summary>
    internal static void PreloadIfPresent()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "opengl32.dll");
        if (File.Exists(localPath))
            NativeLibrary.TryLoad(localPath, out _);
    }
}
