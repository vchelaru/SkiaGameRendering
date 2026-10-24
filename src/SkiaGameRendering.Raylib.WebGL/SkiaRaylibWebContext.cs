using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using SkiaSharp;

namespace SkiaGameRendering;

/// <summary>
/// Skia's <see cref="GRContext"/> on raylib's own WebGL context. There is one context for both libraries (a
/// WebGL context cannot be shared), so every Skia pass runs between <see cref="BeginPass"/> and
/// <see cref="EndPass"/>: the first saves the GL state raylib relies on and tells Skia to forget what it
/// cached, the second puts raylib's state back.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class SkiaRaylibWebContext : IDisposable
{
    // GL_RGBA8: the only format a render target or the canvas has here.
    internal const uint Rgba8 = 0x8058;

    private GRGlInterface? _interface;
    private GRContext? _grContext;
    private bool _passOpen;

    internal void Initialize()
    {
        int[] info;
        try
        {
            info = WebGlInterop.Attach();
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException(
                "SkiaGameRendering could not find raylib's WebGL context. Call Raylib.InitWindow first, and build the " +
                $"app with the SkiaGameRendering.Raylib.WebGL build props. {exception.Message}", exception);
        }

        if (info[1] == 0)
            throw new PlatformNotSupportedException(
                "raylib created a WebGL 1 context. SkiaGameRendering needs WebGL 2 (OpenGL ES 3).");

        _interface = GRGlInterface.Create()
            ?? throw new InvalidOperationException("GRGlInterface.Create failed for raylib's WebGL context.");
        _grContext = GRContext.CreateGl(_interface)
            ?? throw new InvalidOperationException("GRContext.CreateGl failed for raylib's WebGL context.");
    }

    internal GRContext GrContext => _grContext ?? throw new ObjectDisposedException(nameof(SkiaRaylibWebContext));

    internal bool IsContextLost => WebGlInterop.IsContextLost();

    /// <summary>
    /// Starts Skia work on raylib's context. Returns [framebufferId, width, height, samples, stencilBits,
    /// attachmentKind] of what raylib is drawing into right now.
    /// </summary>
    internal int[] BeginPass()
    {
        if (_passOpen)
            throw new InvalidOperationException(
                "Another SkiaRenderTarget2D or SkiaDirectCanvas pass is open; end it before beginning this one.");
        if (WebGlInterop.IsContextLost())
            throw new InvalidOperationException("raylib's WebGL context is lost; Skia cannot draw until it is restored.");

        var info = WebGlInterop.BeginPass();
        _passOpen = true;
        GrContext.ResetContext();
        return info;
    }

    /// <summary>Flushes Skia's queued work and gives raylib back the GL state it had at <see cref="BeginPass"/>.</summary>
    internal void EndPass()
    {
        if (!_passOpen)
            return;

        try
        {
            if (!WebGlInterop.IsContextLost())
                GrContext.Flush();
        }
        finally
        {
            _passOpen = false;
            WebGlInterop.EndPass();
        }
    }

    internal SKSurface WrapFramebuffer(int framebufferId, int width, int height, int samples, int stencilBits,
        GRSurfaceOrigin origin, out GRBackendRenderTarget renderTarget)
    {
        var info = new GRGlFramebufferInfo((uint)framebufferId, Rgba8);
        renderTarget = new GRBackendRenderTarget(width, height, samples, stencilBits, info);
        var surface = SKSurface.Create(GrContext, renderTarget, origin, SKColorType.Rgba8888);
        if (surface == null)
        {
            renderTarget.Dispose();
            throw new InvalidOperationException(
                $"Skia could not wrap framebuffer {framebufferId} ({width}x{height}, {samples} samples, {stencilBits} stencil bits).");
        }

        return surface;
    }

    /// <summary>Diagnostics: GL errors Skia left behind that <see cref="EndPass"/> cleared so raylib never sees them.</summary>
    internal int GlErrorCount => WebGlInterop.GlErrorCount();

    public void Dispose()
    {
        if (_grContext == null)
            return;

        var grContext = _grContext;
        var glInterface = _interface;
        _grContext = null;
        _interface = null;

        // A closed window takes its context with it, and with it every GL object Skia owned.
        if (WebGlInterop.IsContextLost())
            grContext.AbandonContext();
        else
            grContext.ResetContext();

        grContext.Dispose();
        glInterface?.Dispose();
    }
}
