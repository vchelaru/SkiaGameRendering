using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace SkiaGameRendering.Stride.VK
{
    /// <summary>
    /// A GPU surface that SkiaSharp renders directly into, sized to match whatever you intend to
    /// draw it onto. Vulkan analog of <c>SkiaGameRendering.Stride.D3D11</c>'s D3D11
    /// <c>SkiaStrideRenderTarget2D</c> - same Begin/Canvas/End shape, same <see cref="End"/>-needs-a-
    /// <see cref="GraphicsContext"/> reasoning (Stride's <see cref="SpriteBatch"/> composite needs
    /// one, normally <c>RenderDrawContext.GraphicsContext</c> from inside a
    /// <c>SceneRendererBase.DrawCore</c> - see <see cref="SkiaStrideVulkanSceneRenderer"/>).
    /// <code>
    /// var canvas = new SkiaStrideVulkanRenderTarget2D(graphicsDevice, 200, 200);
    /// canvas.Begin();
    /// canvas.Canvas.DrawCircle(100, 100, 100, paint);
    /// canvas.End(drawContext.GraphicsContext);
    /// </code>
    /// </summary>
    public sealed class SkiaStrideVulkanRenderTarget2D : IDisposable
    {
        readonly SkiaStrideVulkanContext _context;
        readonly GraphicsDevice _graphicsDevice;
        SkiaStrideVulkanTarget? _target;
        SpriteBatch? _spriteBatch;
        bool _hasBegun;

        public SkiaStrideVulkanRenderTarget2D(
            GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            _graphicsDevice = graphicsDevice;
            _context = SkiaStrideVulkanRenderer.EnsureInitialized(graphicsDevice);
            _target = new SkiaStrideVulkanTarget(_context, graphicsDevice, width, height, colorType);
        }

        public Texture Texture =>
            (_target ?? throw new ObjectDisposedException(nameof(SkiaStrideVulkanRenderTarget2D))).Texture;

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _target!.Surface.Canvas
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass: acquires Stride's Vulkan queue lock (see
        /// <see cref="SkiaStrideVulkanContext"/>) so Skia's and Stride's <c>vkQueueSubmit</c> calls
        /// stay externally synchronized. Throws if a previous <see cref="Begin"/> hasn't been closed
        /// with <see cref="End"/> yet.
        /// </summary>
        public void Begin(bool clear = true)
        {
            if (_target == null)
                throw new ObjectDisposedException(nameof(SkiaStrideVulkanRenderTarget2D));
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            _context.BeginDraw();
            _hasBegun = true;

            if (clear)
                _target.Surface.Canvas.Clear();
        }

        /// <summary>
        /// Ends the render pass started by <see cref="Begin"/>: flushes Skia's queued GPU work and
        /// submits it to the shared <c>VkQueue</c> (releasing the queue lock <see cref="Begin"/>
        /// acquired), then composites the whole surface at native size and the origin via a
        /// <see cref="SpriteBatch"/> - the same mechanism MonoGame's <c>SkiaRenderTarget2D.End</c>
        /// and the D3D11 Stride adapter's <c>SkiaStrideRenderTarget2D.End</c> use.
        /// <paramref name="graphicsContext"/> is normally <c>RenderDrawContext.GraphicsContext</c>,
        /// only available inside a <c>SceneRendererBase</c>'s <c>DrawCore</c> (see
        /// <see cref="SkiaStrideVulkanSceneRenderer"/>). Throws if <see cref="Begin"/> wasn't called
        /// first.
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
                _context.EndDraw();
                _hasBegun = false;
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
