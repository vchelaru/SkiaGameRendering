using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// A fixed-size GPU texture that SkiaSharp renders directly into. Mirrors
    /// <see cref="RenderTarget2D"/>'s lifecycle (construct once, fixed size, dispose when done) and
    /// <see cref="SpriteBatch"/>'s Begin/End shape for driving a render pass:
    /// <code>
    /// var canvas = new SkiaRenderTarget2D(graphicsDevice, 200, 200);
    /// canvas.Begin();
    /// canvas.Canvas.DrawCircle(100, 100, 100, paint);
    /// canvas.End();
    /// spriteBatch.Draw(canvas.Texture, position, Color.White);
    /// </code>
    /// </summary>
    public sealed class SkiaRenderTarget2D : IDisposable
    {
        private readonly SkiaBackend _backend;
        private SkiaTarget? _target;
        private SKCanvas? _canvas;
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
        /// Ends the render pass started by <see cref="Begin"/>. Throws if <see cref="Begin"/>
        /// wasn't called first.
        /// </summary>
        public void End()
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");

            try
            {
                _backend.EndRender(_target!);
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

            _target.Dispose();
            _target = null;
        }
    }
}
