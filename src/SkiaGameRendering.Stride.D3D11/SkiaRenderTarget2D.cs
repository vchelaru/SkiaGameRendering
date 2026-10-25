using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// A GPU surface that SkiaSharp renders directly into, sized to match whatever you intend to
    /// draw it onto. Mirrors the MonoGame/raylib backends' Begin/Canvas/End shape, with one
    /// difference: <see cref="End"/> takes a <see cref="GraphicsContext"/>, because Stride's
    /// <see cref="SpriteBatch"/> (which does the composite) needs one to begin a batch. Normally
    /// that's <c>RenderDrawContext.GraphicsContext</c> from inside a <c>SceneRendererBase</c>'s
    /// <c>DrawCore</c> - see <see cref="SkiaSceneRenderer"/>, which is also why Begin/End can't
    /// run from an arbitrary call site the way MonoGame's <c>Draw()</c> allows.
    /// <code>
    /// var canvas = new SkiaRenderTarget2D(graphicsDevice, 200, 200);
    /// canvas.Begin();
    /// canvas.Canvas.DrawCircle(100, 100, 100, paint);
    /// canvas.End(drawContext.GraphicsContext);
    /// </code>
    /// </summary>
    public sealed class SkiaRenderTarget2D : IDisposable
    {
        readonly SkiaStrideContext _context;
        readonly GraphicsDevice _graphicsDevice;
        SkiaStrideTarget? _target;
        SpriteBatch? _spriteBatch;
        bool _hasBegun;

        /// <param name="multiSampleCount">
        /// Samples per pixel, same parameter as on the other engines. Only 0 (the default) works on Stride; 2, 4 and 8
        /// throw <see cref="NotSupportedException"/> (it never falls back to 0), and so does any other value.
        /// Check <see cref="SkiaRenderer.SupportsMultiSampledTargets"/> to stay portable.
        /// </param>
        public SkiaRenderTarget2D(
            GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888, int multiSampleCount = 0)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            if (multiSampleCount is not (0 or 2 or 4 or 8))
                throw new NotSupportedException(
                    $"multiSampleCount must be 0, 2, 4 or 8, not {multiSampleCount}.");
            if (multiSampleCount != 0)
                throw new NotSupportedException(
                    $"SkiaRenderTarget2D on Stride does not support multisampled render targets (multiSampleCount {multiSampleCount}). " +
                    "It never falls back to 0.");

            _graphicsDevice = graphicsDevice;
            _context = SkiaRenderer.EnsureInitialized(graphicsDevice);
            _target = new SkiaStrideTarget(_context, graphicsDevice, width, height, colorType);
        }

        public Texture Texture =>
            (_target ?? throw new ObjectDisposedException(nameof(SkiaRenderTarget2D))).Texture;

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _target!.Surface.Canvas
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass: switches to the ANGLE GL context and binds this target's FBO.
        /// Throws if a previous <see cref="Begin"/> hasn't been closed with <see cref="End"/> yet.
        /// </summary>
        public void Begin(bool clear = true)
        {
            if (_target == null)
                throw new ObjectDisposedException(nameof(SkiaRenderTarget2D));
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
        /// switches back to Stride's own D3D11 state, and composites the whole surface at native
        /// size and the origin via a <see cref="SpriteBatch"/> - the same mechanism MonoGame's
        /// <c>SkiaRenderTarget2D.End</c> uses. <paramref name="graphicsContext"/> is normally
        /// <c>RenderDrawContext.GraphicsContext</c>, only available inside a <c>SceneRendererBase</c>'s
        /// <c>DrawCore</c> (see <see cref="SkiaSceneRenderer"/>). Throws if
        /// <see cref="Begin"/> wasn't called first.
        /// </summary>
        public void End(GraphicsContext graphicsContext)
        {
            ArgumentNullException.ThrowIfNull(graphicsContext);
            EndCore(graphicsContext);
        }

        /// <summary>
        /// Same as <see cref="End"/>, but skips the composite - use this only when you need the raw
        /// <see cref="Texture"/> for something the composite can't express (e.g. drawing it more than
        /// once, or at a different size), or when no <see cref="GraphicsContext"/> is available at the
        /// call site. You're then responsible for drawing <see cref="Texture"/> yourself.
        /// </summary>
        public void EndWithoutDrawing() => EndCore(graphicsContext: null);

        void EndCore(GraphicsContext? graphicsContext)
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

            if (graphicsContext != null)
            {
                _spriteBatch ??= new SpriteBatch(_graphicsDevice);
                _spriteBatch.Begin(graphicsContext);
                _spriteBatch.Draw(Texture, Vector2.Zero);
                _spriteBatch.End();
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
