using System.Runtime.Versioning;

namespace SkiaGameRendering;

/// <summary>
/// Holds the Skia GL context for raylib's WebGL context. Most code never calls this directly: constructing a
/// <see cref="SkiaRenderTarget2D"/> or <see cref="SkiaDirectCanvas"/> initializes it. Call <see cref="Initialize"/>
/// explicitly only to make initialization (and any failure) happen at a known point.
/// <para>
/// Must be called after <c>Raylib.InitWindow</c>. raylib supports a single window, so this holds a single
/// context. Unlike on desktop, Skia does not get a context of its own: WebGL contexts cannot share objects, so
/// Skia and raylib drive the one context raylib created, and every Skia pass saves and restores raylib's GL state.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
public static class SkiaRenderer
{
    private static SkiaRaylibWebContext? _context;

    public static bool IsInitialized => _context != null;

    /// <summary>
    /// Always false: <see cref="SkiaRenderTarget2D"/>'s <c>multiSampleCount</c> exists for a portable API, but only 0
    /// works here and 2, 4 and 8 throw <see cref="NotSupportedException"/>. Needs no initialization to answer.
    /// </summary>
    public static bool SupportsMultiSampledTargets => false;

    /// <summary>Always true: <see cref="SkiaDirectCanvas"/> draws on the context raylib renders with.</summary>
    public static bool SupportsDirectCanvas => true;

    public static void Initialize()
    {
        if (_context != null)
            return;

        var context = new SkiaRaylibWebContext();
        context.Initialize();
        _context = context;
    }

    internal static SkiaRaylibWebContext EnsureInitialized()
    {
        if (_context == null)
            Initialize();

        return _context!;
    }

    /// <summary>
    /// Disposes the Skia context. Dispose any live <see cref="SkiaRenderTarget2D"/> and
    /// <see cref="SkiaDirectCanvas"/> instances first; this does not track or dispose them for you.
    /// </summary>
    public static void Dispose()
    {
        if (_context == null)
            return;

        var context = _context;
        _context = null;
        context.Dispose();
    }
}
