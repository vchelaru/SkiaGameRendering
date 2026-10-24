using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// Draws with SkiaSharp straight into whatever Stride is rendering to when <see cref="Begin"/> is called:
    /// the back buffer, or the render target bound on the <see cref="GraphicsContext"/>'s command list. Same
    /// shape as <see cref="SkiaRenderTarget2D"/>, minus the size and the texture: there is no
    /// intermediate texture and no <see cref="SpriteBatch"/> composite. <see cref="Begin"/> takes the
    /// <see cref="GraphicsContext"/> because that is where Stride keeps its current target; normally it is
    /// <c>RenderDrawContext.GraphicsContext</c> inside a <c>SceneRendererBase</c>'s <c>DrawCore</c> (see
    /// <see cref="SkiaSceneRenderer.DirectCanvas"/>).
    /// <code>
    /// var skia = new SkiaDirectCanvas(graphicsDevice);
    /// skia.Begin(drawContext.GraphicsContext);
    /// skia.Canvas.DrawCircle(100, 100, 50, paint);
    /// skia.End();
    /// </code>
    /// <see cref="Begin"/> does not clear; Skia draws on top of what is already there. Coordinates are target
    /// pixels with the origin at the top left; the command list's viewport and scissor rectangle are ignored.
    /// Only one bound, single-sample 8-bit RGBA or BGRA target is supported. An sRGB-format target, which is
    /// what Stride's back buffer is in its default Linear color space, is drawn without encoding, so an
    /// <see cref="SKColor"/> lands on screen as that color.
    /// <para>
    /// <b>Cost.</b> <see cref="Begin"/> flushes the command list (submits what Stride recorded so far and starts
    /// a new one), because Skia's work reaches the GPU as soon as <see cref="End"/> runs and would otherwise land
    /// under Stride's earlier draws. Use it once per frame, not once per widget.
    /// </para>
    /// </summary>
    public sealed class SkiaDirectCanvas : IDisposable
    {
        readonly SkiaStrideVulkanContext _context;
        SKCanvas? _canvas;
        SkiaStrideVulkanContext.DirectPass? _pass;
        bool _hasBegun;
        bool _isDisposed;

        public SkiaDirectCanvas(GraphicsDevice graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            _context = SkiaRenderer.EnsureInitialized(graphicsDevice);
        }

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _canvas!
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass into the target bound on <paramref name="graphicsContext"/>'s command list.
        /// Throws if a previous <see cref="Begin"/> hasn't been closed with <see cref="End"/> yet, if nothing
        /// is bound, and <see cref="NotSupportedException"/> for several bound targets or one Skia cannot draw
        /// into (multisampled, or not 8-bit RGBA or BGRA). A refused <see cref="Begin"/> changes nothing.
        /// </summary>
        public void Begin(GraphicsContext graphicsContext)
        {
            ArgumentNullException.ThrowIfNull(graphicsContext);
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaDirectCanvas));
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            var commandList = graphicsContext.CommandList;
            if (commandList.RenderTargetCount == 0)
                throw new InvalidOperationException("The command list has no render target bound.");
            if (commandList.RenderTargetCount > 1)
                throw new NotSupportedException(
                    $"SkiaDirectCanvas needs a single bound render target, not {commandList.RenderTargetCount}.");

            _canvas = _context.BeginDirectDraw(commandList, commandList.RenderTarget, out var pass);
            _pass = pass;
            // Only flip _hasBegun once the pass actually starts, so a failed Begin can be retried.
            _hasBegun = true;
        }

        /// <summary>
        /// Ends the render pass started by <see cref="Begin"/> and hands the target back to Stride, so its
        /// later draws land on top of Skia's. Throws if <see cref="Begin"/> wasn't called first.
        /// </summary>
        public void End()
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");

            try
            {
                _context.EndDirectDraw(_pass!);
            }
            finally
            {
                // EndDirectDraw hands the target back even when it throws, so the pass is over.
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
