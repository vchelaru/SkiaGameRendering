using System.Runtime.Versioning;
using Raylib_cs;
using SkiaSharp;
using RaylibApi = Raylib_cs.Raylib;
using RaylibColor = Raylib_cs.Color;
using RaylibTexture2D = Raylib_cs.Texture2D;

namespace SkiaGameRendering;

/// <summary>
/// A GPU surface that SkiaSharp renders directly into, sized to match whatever you intend to
/// draw it onto. Same Begin/End shape as the desktop raylib package: <see cref="End"/> composites the whole
/// surface at native size and the origin via <c>Raylib.DrawTexture</c> - call it between
/// <c>Raylib.BeginDrawing()</c>/<c>EndDrawing()</c>, same as any other raylib draw call.
/// <code>
/// var canvas = new SkiaRenderTarget2D(Width, Height);
/// Raylib.BeginDrawing();
/// canvas.Begin();
/// canvas.Canvas.DrawCircle(100, 100, 100, paint);
/// canvas.End();
/// Raylib.EndDrawing();
/// </code>
/// <see cref="Texture"/> stores canvas row 0 in texel row 0, the way raylib draws any texture
/// loaded from an image, so callers never need to flip it themselves. Only <see cref="SKColorType.Rgba8888"/>
/// is supported.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class SkiaRenderTarget2D : IDisposable
{
    private readonly SkiaRaylibWebContext _context;
    private RaylibTexture2D _texture;
    private SKSurface? _surface;
    private GRBackendRenderTarget? _renderTarget;
    private int _framebufferId;
    private int _renderbufferHandle;
    private bool _hasBegun;
    private bool _isDisposed;

    public SkiaRenderTarget2D(int width, int height, SKColorType colorType = SKColorType.Rgba8888)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (colorType != SKColorType.Rgba8888)
            throw new NotSupportedException("SkiaRenderTarget2D supports SKColorType.Rgba8888 only on WebGL.");

        _context = SkiaRenderer.EnsureInitialized();

        uint textureId;
        unsafe
        {
            textureId = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR8G8B8A8, 1);
        }

        _texture = new RaylibTexture2D
        {
            Id = textureId,
            Width = width,
            Height = height,
            Mipmaps = 1,
            Format = PixelFormat.UncompressedR8G8B8A8,
        };
        RaylibApi.SetTextureFilter(_texture, TextureFilter.Point);

        try
        {
            var framebuffer = WebGlInterop.CreateTextureFramebuffer((int)textureId, width, height);
            _framebufferId = framebuffer[0];
            _renderbufferHandle = framebuffer[1];

            // Texel row 0 is the top of the texture to raylib, so canvas row 0 goes there.
            _context.BeginPass();
            try
            {
                _surface = _context.WrapFramebuffer(_framebufferId, width, height, 0, 8, GRSurfaceOrigin.TopLeft, out _renderTarget);
            }
            finally
            {
                _context.EndPass();
            }
        }
        catch
        {
            ReleaseGl();
            throw;
        }
    }

    public RaylibTexture2D Texture =>
        _isDisposed ? throw new ObjectDisposedException(nameof(SkiaRenderTarget2D)) : _texture;

    /// <summary>
    /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
    /// accessing it outside that window throws.
    /// </summary>
    public SKCanvas Canvas => _hasBegun
        ? _surface!.Canvas
        : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

    /// <summary>
    /// Begins a render pass: saves raylib's GL state and points Skia at this target's framebuffer.
    /// Throws if a previous <see cref="Begin"/> hasn't been closed with <see cref="End"/> yet.
    /// </summary>
    public void Begin(bool clear = true)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SkiaRenderTarget2D));
        if (_hasBegun)
            throw new InvalidOperationException("Begin cannot be called again until End has been called.");

        _context.BeginPass();
        _hasBegun = true;

        if (clear)
            _surface!.Canvas.Clear();
    }

    /// <summary>
    /// Ends the render pass started by <see cref="Begin"/>: flushes Skia's queued GPU work, gives raylib back its
    /// GL state, and composites the whole surface at native size and the origin via <c>Raylib.DrawTexture</c> -
    /// call between <c>Raylib.BeginDrawing()</c>/<c>EndDrawing()</c>, same as any other raylib draw call.
    /// Throws if <see cref="Begin"/> wasn't called first.
    /// </summary>
    public void End() => EndCore(composite: true);

    /// <summary>
    /// Same as <see cref="End"/>, but skips the composite - use this only when you need the raw
    /// <see cref="Texture"/> for something <see cref="End"/>'s single whole-surface blit can't
    /// express (e.g. drawing it more than once, or at a different size). You're then
    /// responsible for drawing <see cref="Texture"/> yourself.
    /// </summary>
    public void EndWithoutDrawing() => EndCore(composite: false);

    private void EndCore(bool composite)
    {
        if (!_hasBegun)
            throw new InvalidOperationException("Begin must be called before calling End.");

        try
        {
            _surface!.Flush();
        }
        finally
        {
            _hasBegun = false;
            _context.EndPass();
        }

        if (composite)
            RaylibApi.DrawTexture(_texture, 0, 0, RaylibColor.White);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        if (_hasBegun)
            throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

        _isDisposed = true;
        ReleaseGl();
    }

    private void ReleaseGl()
    {
        _surface?.Dispose();
        _surface = null;
        _renderTarget?.Dispose();
        _renderTarget = null;

        // Both live on raylib's context, which is gone with its window.
        if (_context.IsContextLost)
            return;

        if (_framebufferId != 0)
            WebGlInterop.DeleteFramebuffer(_framebufferId, _renderbufferHandle);
        _framebufferId = 0;
        if (_texture.Id != 0)
            Rlgl.UnloadTexture(_texture.Id);
        _texture.Id = 0;
    }
}
