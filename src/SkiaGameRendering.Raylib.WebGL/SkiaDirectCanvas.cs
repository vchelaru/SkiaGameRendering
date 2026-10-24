using System.Runtime.Versioning;
using Raylib_cs;
using SkiaSharp;
using RaylibApi = Raylib_cs.Raylib;

namespace SkiaGameRendering;

/// <summary>
/// Draws with SkiaSharp straight into whatever raylib is rendering to when <see cref="Begin"/> is called:
/// the canvas's back buffer, or the <c>RenderTexture2D</c> bound with <c>BeginTextureMode</c>. Same
/// Begin/Canvas/End shape as <see cref="SkiaRenderTarget2D"/>, minus the size and the texture:
/// there is no intermediate texture and no <c>DrawTexture</c> composite.
/// <code>
/// var skia = new SkiaDirectCanvas();
/// Raylib.BeginDrawing();
/// Raylib.DrawRectangle(0, 0, 100, 100, Color.Green); // background
/// skia.Begin();
/// skia.Canvas.DrawCircle(100, 100, 50, paint);
/// skia.End();
/// Raylib.DrawText("on top", 10, 10, 20, Color.White); // foreground
/// Raylib.EndDrawing();
/// </code>
/// <see cref="Begin"/> does not clear; Skia draws on top of what is already there. Coordinates are target
/// pixels from the top left, the same as raylib's own 2D drawing; raylib's camera, viewport and scissor
/// are ignored. Skia shares raylib's WebGL context, so <see cref="Begin"/> and <see cref="End"/> save and restore
/// raylib's GL state around the pass.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class SkiaDirectCanvas : IDisposable
{
    private readonly SkiaRaylibWebContext _context;

    // The canvas's surface, reused across passes until its size, sample count or stencil bits change. A render
    // texture's is built for each pass instead: raylib gives no notice when one is unloaded, and the next
    // one it loads can get the same GL names, so a cached FBO could keep drawing into the unloaded texture.
    private SKSurface? _backBufferSurface;
    private GRBackendRenderTarget? _backBufferRenderTarget;
    private int _backBufferWidth;
    private int _backBufferHeight;
    private int _backBufferSamples;
    private int _backBufferStencil;

    private Pass? _pass;
    private bool _isDisposed;

    private sealed class Pass
    {
        internal SKSurface Surface = null!;
        internal int SaveCount;
        internal bool OwnsSurface;
        // Only for a render texture: the per-pass objects End frees.
        internal GRBackendRenderTarget? RenderTarget;
        internal int FramebufferId;
        internal int RenderbufferHandle;
    }

    /// <summary>Must be called after <c>Raylib.InitWindow</c>.</summary>
    public SkiaDirectCanvas()
    {
        _context = SkiaRenderer.EnsureInitialized();
    }

    /// <summary>
    /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
    /// accessing it outside that window throws.
    /// </summary>
    public SKCanvas Canvas => _pass?.Surface.Canvas
        ?? throw new InvalidOperationException("Begin must be called before accessing Canvas.");

    /// <summary>
    /// Begins a render pass into whatever raylib has bound right now: draws raylib has batched so far
    /// are submitted first, so Skia's land on top of them. Call it between <c>BeginDrawing</c> and
    /// <c>EndDrawing</c>, optionally inside <c>BeginTextureMode</c>/<c>EndTextureMode</c>. Throws if a
    /// previous <see cref="Begin"/> hasn't been closed with <see cref="End"/> yet. Does not clear.
    /// </summary>
    public void Begin()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SkiaDirectCanvas));
        if (_pass != null)
            throw new InvalidOperationException("Begin cannot be called again until End has been called.");

        // Still raylib's state: submit its batch so Skia's draws land on top of everything drawn so far.
        Rlgl.DrawRenderBatchActive();

        var info = _context.BeginPass();
        var pass = new Pass();
        try
        {
            if (info[0] == 0)
            {
                pass.Surface = GetBackBufferSurface(info[1], info[2], info[3], info[4]);
            }
            else
            {
                // BeginTextureMode sets these to the render texture's size.
                var width = Rlgl.GetFramebufferWidth();
                var height = Rlgl.GetFramebufferHeight();
                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException($"raylib's current target is {width}x{height}.");

                var framebuffer = WebGlInterop.CreateAttachmentFramebuffer(width, height);
                pass.FramebufferId = framebuffer[0];
                pass.RenderbufferHandle = framebuffer[1];
                // raylib draws into a render texture with the same top-down projection it uses for the canvas,
                // which puts its y = 0 in the last texel row, so the surface is BottomLeft like the back buffer.
                pass.Surface = _context.WrapFramebuffer(pass.FramebufferId, width, height, 0, 8,
                    GRSurfaceOrigin.BottomLeft, out var renderTarget);
                pass.RenderTarget = renderTarget;
                pass.OwnsSurface = true;
            }

            pass.SaveCount = pass.Surface.Canvas.Save();
        }
        catch
        {
            DisposePassObjects(pass);
            _context.EndPass();
            throw;
        }

        _pass = pass;
    }

    /// <summary>
    /// Ends the render pass started by <see cref="Begin"/> and hands the target back to raylib, so its
    /// later draws land on top of Skia's. Throws if <see cref="Begin"/> wasn't called first.
    /// </summary>
    public void End()
    {
        var pass = _pass ?? throw new InvalidOperationException("Begin must be called before calling End.");
        _pass = null;

        try
        {
            pass.Surface.Canvas.RestoreToCount(pass.SaveCount);
            pass.Surface.Flush();
        }
        finally
        {
            try
            {
                DisposePassObjects(pass);
            }
            finally
            {
                _context.EndPass();
            }
        }
    }

    private SKSurface GetBackBufferSurface(int width, int height, int samples, int stencilBits)
    {
        if (_backBufferSurface != null && _backBufferWidth == width && _backBufferHeight == height
            && _backBufferSamples == samples && _backBufferStencil == stencilBits)
            return _backBufferSurface;

        DisposeBackBufferSurface();
        // The canvas's framebuffer has its origin at the bottom left.
        _backBufferSurface = _context.WrapFramebuffer(0, width, height, samples, stencilBits,
            GRSurfaceOrigin.BottomLeft, out _backBufferRenderTarget);
        _backBufferWidth = width;
        _backBufferHeight = height;
        _backBufferSamples = samples;
        _backBufferStencil = stencilBits;
        return _backBufferSurface;
    }

    /// <summary>Frees a render texture pass's surface and framebuffer; raylib keeps the texture it drew into.</summary>
    private static void DisposePassObjects(Pass pass)
    {
        if (pass.OwnsSurface)
            pass.Surface.Dispose();
        pass.OwnsSurface = false;
        pass.RenderTarget?.Dispose();
        pass.RenderTarget = null;
        if (pass.FramebufferId != 0)
            WebGlInterop.DeleteFramebuffer(pass.FramebufferId, pass.RenderbufferHandle);
        pass.FramebufferId = 0;
    }

    private void DisposeBackBufferSurface()
    {
        _backBufferSurface?.Dispose();
        _backBufferSurface = null;
        _backBufferRenderTarget?.Dispose();
        _backBufferRenderTarget = null;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        if (_pass != null)
            throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

        _isDisposed = true;
        DisposeBackBufferSurface();
    }
}
