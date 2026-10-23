using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Draws with SkiaSharp straight into whatever the engine is rendering to when <see cref="Begin"/> is
    /// called: the back buffer, or the bound <see cref="RenderTarget2D"/> (multisampled ones included).
    /// Same Begin/Canvas/End shape as <see cref="SkiaRenderTarget2D"/>, minus the size and the texture: there
    /// is no intermediate texture and no SpriteBatch composite, so it costs less.
    /// <code>
    /// var skia = new SkiaDirectCanvas(graphicsDevice);
    /// spriteBatch.Begin(); /* background */ spriteBatch.End();
    /// skia.Begin();
    /// skia.Canvas.DrawCircle(100, 100, 50, paint);
    /// skia.End();
    /// spriteBatch.Begin(); /* foreground */ spriteBatch.End();
    /// </code>
    /// <see cref="Begin"/> does not clear; Skia draws on top of what is already there. Coordinates are target
    /// pixels with the origin at the top left; the engine's <see cref="GraphicsDevice.Viewport"/> and scissor
    /// rectangle are ignored. Only MonoGame DesktopGL and KNI DesktopGL support it (see
    /// <see cref="SkiaRenderer.SupportsDirectCanvas"/>), and only for the back buffer or one bound
    /// <see cref="SurfaceFormat.Color"/> target.
    /// </summary>
    public sealed class SkiaDirectCanvas : IDisposable
    {
        private readonly SkiaBackend _backend;
        private SKCanvas? _canvas;
        private object? _pass;
        private bool _hasBegun;
        private bool _isDisposed;

        /// <summary>
        /// Throws <see cref="NotSupportedException"/> on a platform where
        /// <see cref="SkiaRenderer.SupportsDirectCanvas"/> is false.
        /// </summary>
        public SkiaDirectCanvas(GraphicsDevice graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);

            _backend = SkiaRenderer.EnsureInitialized(graphicsDevice);
            if (!_backend.SupportsDirectCanvas)
                throw _backend.DirectCanvasNotSupported();
        }

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _canvas!
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass into whatever the engine has bound right now. Throws if a previous
        /// <see cref="Begin"/> hasn't been closed with <see cref="End"/> yet, the same guard as
        /// <see cref="SpriteBatch.Begin"/>. Does not clear.
        /// </summary>
        public void Begin()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaDirectCanvas));
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            // Only flip _hasBegun once the pass actually starts, so a failed Begin can be retried.
            _canvas = _backend.BeginDirectDraw(out _pass);
            _hasBegun = true;
        }

        /// <summary>
        /// Ends the render pass started by <see cref="Begin"/> and hands the target back to the engine, so its
        /// later draws land on top of Skia's. Throws if <see cref="Begin"/> wasn't called first.
        /// </summary>
        public void End()
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");

            try
            {
                _backend.EndDirectDraw(_pass!);
            }
            finally
            {
                // EndDirectDraw restores the engine's context even when it throws, so the pass is over.
                _hasBegun = false;
                _canvas = null;
                _pass = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            if (_hasBegun)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            _isDisposed = true;
        }
    }
}
