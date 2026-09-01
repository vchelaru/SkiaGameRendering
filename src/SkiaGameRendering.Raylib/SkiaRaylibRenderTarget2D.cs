using SkiaSharp;
using RaylibApi = Raylib_cs.Raylib;
using RaylibColor = Raylib_cs.Color;
using RaylibTexture2D = Raylib_cs.Texture2D;

namespace SkiaGameRendering.Raylib
{
    /// <summary>
    /// A GPU surface that SkiaSharp renders directly into, sized to match whatever you intend to
    /// draw it onto. Mirrors the MonoGame backend's <c>SkiaRenderTarget2D</c> Begin/End shape:
    /// <see cref="End"/> composites the whole surface at native size and the origin via
    /// <c>Raylib.DrawTexture</c> - call it between <c>Raylib.BeginDrawing()</c>/<c>EndDrawing()</c>,
    /// same as any other raylib draw call.
    /// <code>
    /// var canvas = new SkiaRaylibRenderTarget2D(Width, Height);
    /// Raylib.BeginDrawing();
    /// canvas.Begin();
    /// canvas.Canvas.DrawCircle(100, 100, 100, paint);
    /// canvas.End();
    /// Raylib.EndDrawing();
    /// </code>
    /// The underlying Skia surface is created with a bottom-left origin (see
    /// <see cref="SkiaRaylibContext.CreateSurface"/>) so <see cref="Texture"/> already matches
    /// raylib's own texture-sampling convention - callers never need to flip it themselves.
    /// </summary>
    public sealed class SkiaRaylibRenderTarget2D : IDisposable
    {
        private readonly SkiaRaylibContext _context;
        private SkiaRaylibTarget? _target;
        private bool _hasBegun;

        public SkiaRaylibRenderTarget2D(int width, int height, SKColorType colorType = SKColorType.Rgba8888)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            _context = SkiaRaylibRenderer.EnsureInitialized();
            _target = new SkiaRaylibTarget(_context, width, height, colorType);
        }

        public RaylibTexture2D Texture =>
            (_target ?? throw new ObjectDisposedException(nameof(SkiaRaylibRenderTarget2D))).Texture;

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _target!.Surface.Canvas
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass: switches to the Skia GL context and binds this target's FBO.
        /// Throws if a previous <see cref="Begin"/> hasn't been closed with <see cref="End"/> yet.
        /// </summary>
        public void Begin(bool clear = true)
        {
            if (_target == null)
                throw new ObjectDisposedException(nameof(SkiaRaylibRenderTarget2D));
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            _context.BeginDraw();
            try
            {
                _context.BindForDrawing(_target.RenderState);
            }
            catch
            {
                _context.EndDraw();
                throw;
            }

            // Only flip _hasBegun once binding actually succeeds - if it throws, the pass never
            // started, so a retry must still be allowed instead of being locked out forever.
            _hasBegun = true;

            if (clear)
                _target.Surface.Canvas.Clear();
        }

        /// <summary>
        /// Ends the render pass started by <see cref="Begin"/>: flushes Skia's queued GPU work,
        /// unbinds the target, switches back to raylib's own GL context, and composites the whole
        /// surface at native size and the origin via <c>Raylib.DrawTexture</c> - call between
        /// <c>Raylib.BeginDrawing()</c>/<c>EndDrawing()</c>, same as any other raylib draw call.
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
                _target!.Surface.Flush();
            }
            finally
            {
                try
                {
                    _context.UnbindAfterDrawing();
                }
                finally
                {
                    _context.EndDraw();
                    _hasBegun = false;
                }
            }

            if (composite)
                RaylibApi.DrawTexture(Texture, 0, 0, RaylibColor.White);
        }

        public void Dispose()
        {
            if (_target == null)
                return;
            if (_hasBegun)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            _target.Dispose();
            _target = null;
        }
    }
}
