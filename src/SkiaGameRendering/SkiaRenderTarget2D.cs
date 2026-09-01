using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// A GPU surface that SkiaSharp renders directly into, sized to match whatever you intend to
    /// draw it onto (typically the back buffer or a <see cref="RenderTarget2D"/> the same size as
    /// the viewport). Works like <see cref="SpriteBatch"/>'s own Begin/End: place individual shapes
    /// with their own coordinates via Skia's drawing API, and <see cref="End"/> composites the whole
    /// result onto whatever render target is currently bound - no separate
    /// <c>spriteBatch.Draw(canvas.Texture, ...)</c> step needed, the same way <c>SpriteBatch.End()</c>
    /// needs no separate step to show its queued sprite draws.
    /// <code>
    /// var canvas = new SkiaRenderTarget2D(graphicsDevice, viewportWidth, viewportHeight);
    /// canvas.Begin();
    /// canvas.Canvas.DrawCircle(100, 100, 100, paint); // position is the DrawCircle call's job, not End's
    /// canvas.End();
    /// </code>
    /// </summary>
    public sealed class SkiaRenderTarget2D : IDisposable
    {
        private readonly SkiaBackend _backend;
        private SkiaTarget? _target;
        private SKCanvas? _canvas;
        private SpriteBatch? _spriteBatch;
        private bool _hasBegun;

        public SkiaRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height,
            SKColorType colorType = SKColorType.Rgba8888)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            _backend = SkiaRenderer.EnsureInitialized(graphicsDevice);
            _target = _backend.CreateTarget(width, height, colorType);
        }

        public Texture2D Texture =>
            (_target ?? throw new ObjectDisposedException(nameof(SkiaRenderTarget2D))).Texture;

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _canvas!
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass. Throws if a previous <see cref="Begin"/> hasn't been closed with
        /// <see cref="End"/> yet — mirrors <see cref="SpriteBatch.Begin()"/>'s own guard.
        /// </summary>
        public void Begin(bool clear = true)
        {
            if (_target == null)
                throw new ObjectDisposedException(nameof(SkiaRenderTarget2D));
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            // Only flip _hasBegun once BeginRender actually succeeds - if it throws, the pass
            // never started, so a retry must still be allowed instead of being locked out forever.
            _canvas = _backend.BeginRender(_target, clear);
            _hasBegun = true;
        }

        /// <summary>
        /// Ends the render pass started by <see cref="Begin"/> and immediately composites the whole
        /// surface, at its native size and the origin, onto whatever render target is currently
        /// bound - the same "wherever the engine is currently pointed" semantics as
        /// <see cref="SpriteBatch.End"/>. Set a render target before calling <see cref="Begin"/> if
        /// you want the composite to land somewhere other than the back buffer. Positioning
        /// individual content is the job of the drawing calls you make on <see cref="Canvas"/>
        /// between <see cref="Begin"/> and <see cref="End"/>, not of <see cref="End"/> itself. Throws
        /// if <see cref="Begin"/> wasn't called first.
        /// </summary>
        public void End() => EndCore(composite: true);

        /// <summary>
        /// Same as <see cref="End"/>, but skips the composite - use this only when you need the raw
        /// <see cref="Texture"/> for something <see cref="End"/>'s single whole-surface blit can't
        /// express (e.g. drawing it more than once, at a different size, or sampling it in a
        /// shader). You're then responsible for drawing <see cref="Texture"/> yourself.
        /// </summary>
        public void EndWithoutDrawing() => EndCore(composite: false);

        private void EndCore(bool composite)
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");

            try
            {
                _backend.EndRender(_target!);
                if (composite)
                {
                    _spriteBatch ??= new SpriteBatch(_backend.GraphicsDevice);
                    _spriteBatch.Begin();
                    _spriteBatch.Draw(Texture, Vector2.Zero, Color.White);
                    _spriteBatch.End();
                }
            }
            finally
            {
                // Backends guarantee their own context-restore runs even if EndRender throws
                // (see SkiaBackend.EndRender's try/finally), so the pass is always over by here -
                // always clear the flag so a failed End() doesn't also lock out future Begin()s.
                _hasBegun = false;
                _canvas = null;
            }
        }

        public void Dispose()
        {
            if (_target == null)
                return;
            if (_hasBegun)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            _spriteBatch?.Dispose();
            _spriteBatch = null;
            _target.Dispose();
            _target = null;
        }
    }
}
