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
    // texture's is kept in _framebufferSurfaces, keyed on raylib's framebuffer, its color attachment and the size.
    // raylib gives no notice when one is unloaded and the next one could get the same ids, so each hit attaches the
    // color again. At most MaxCachedSurfaces are kept; a new one past that frees them all first.
    private const int MaxCachedSurfaces = 8;
    private readonly Dictionary<(int framebuffer, int kind, int color, int width, int height), CachedSurface> _framebufferSurfaces = new();
    private SKSurface? _backBufferSurface;
    private GRBackendRenderTarget? _backBufferRenderTarget;
    private int _backBufferWidth;
    private int _backBufferHeight;
    private int _backBufferSamples;
    private int _backBufferStencil;

    // The pass in progress, or null. It is always _passState, reused so a game's Begin/End each frame allocates nothing.
    private Pass? _pass;
    private readonly Pass _passState = new();
    private bool _isDisposed;

    private sealed class Pass
    {
        internal SKSurface Surface = null!;
        internal int SaveCount;
    }

    private sealed class CachedSurface
    {
        internal SKSurface Surface = null!;
        internal GRBackendRenderTarget RenderTarget = null!;
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
        var pass = _passState;
        pass.SaveCount = 0;
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

                pass.Surface = GetFramebufferSurface((info[0], info[5], info[6], width, height));
            }

            pass.SaveCount = pass.Surface.Canvas.Save();
        }
        catch
        {
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
            _context.EndPass();
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

    private SKSurface GetFramebufferSurface((int framebuffer, int kind, int color, int width, int height) key)
    {
        if (_framebufferSurfaces.TryGetValue(key, out var cached))
        {
            WebGlInterop.AttachColor(cached.FramebufferId);
            return cached.Surface;
        }

        if (_framebufferSurfaces.Count >= MaxCachedSurfaces)
            ReleaseFramebufferSurfaces();

        var framebuffer = WebGlInterop.CreateAttachmentFramebuffer(key.width, key.height);
        var entry = new CachedSurface { FramebufferId = framebuffer[0], RenderbufferHandle = framebuffer[1] };
        try
        {
            // raylib draws into a render texture with the same top-down projection it uses for the canvas,
            // which puts its y = 0 in the last texel row, so the surface is BottomLeft like the back buffer.
            entry.Surface = _context.WrapFramebuffer(entry.FramebufferId, key.width, key.height, 0, 8,
                GRSurfaceOrigin.BottomLeft, out entry.RenderTarget);
        }
        catch
        {
            WebGlInterop.DeleteFramebuffer(entry.FramebufferId, entry.RenderbufferHandle);
            throw;
        }
        _framebufferSurfaces.Add(key, entry);
        return entry.Surface;
    }

    /// <summary>Frees the cached render texture surfaces and their framebuffers; raylib keeps the textures.</summary>
    private void ReleaseFramebufferSurfaces()
    {
        foreach (var entry in _framebufferSurfaces.Values)
        {
            entry.Surface.Dispose();
            entry.RenderTarget.Dispose();
            WebGlInterop.DeleteFramebuffer(entry.FramebufferId, entry.RenderbufferHandle);
        }
        _framebufferSurfaces.Clear();
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
        ReleaseFramebufferSurfaces();
    }
}
