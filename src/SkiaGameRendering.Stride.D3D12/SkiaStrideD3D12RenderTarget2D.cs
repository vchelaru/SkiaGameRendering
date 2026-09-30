using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace SkiaGameRendering.Stride.D3D12
{
    /// <summary>
    /// A GPU surface that SkiaSharp renders directly into, sized to match whatever you intend to
    /// draw it onto. D3D12 analog of <c>SkiaGameRendering.Stride.D3D11</c>'s
    /// <c>SkiaStrideRenderTarget2D</c>, with the same Begin/Canvas/End shape:
    /// <code>
    /// var canvas = new SkiaStrideD3D12RenderTarget2D(graphicsDevice, 200, 200);
    /// canvas.Begin();
    /// canvas.Canvas.DrawCircle(100, 100, 100, paint);
    /// canvas.End(drawContext.GraphicsContext);
    /// </code>
    /// Call <see cref="Begin"/>/<see cref="End"/> at most once per frame per target, before anything in
    /// that frame samples <see cref="Texture"/>: Skia's work reaches the GPU as soon as <see cref="End"/>
    /// runs, ahead of whatever Stride's current command list recorded earlier (see
    /// <see cref="SkiaStrideD3D12Context"/>).
    /// </summary>
    public sealed class SkiaStrideD3D12RenderTarget2D : IDisposable
    {
        readonly SkiaStrideD3D12Context _context;
        readonly GraphicsDevice _graphicsDevice;
        SkiaStrideD3D12Target? _target;
        SpriteBatch? _spriteBatch;
        bool _hasBegun;

        public SkiaStrideD3D12RenderTarget2D(
            GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            _graphicsDevice = graphicsDevice;
            _context = SkiaStrideD3D12Renderer.EnsureInitialized(graphicsDevice);
            _target = new SkiaStrideD3D12Target(_context, graphicsDevice, width, height, colorType);
        }

        public Texture Texture =>
            (_target ?? throw new ObjectDisposedException(nameof(SkiaStrideD3D12RenderTarget2D))).Texture;

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _target!.Surface.Canvas
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass: takes Stride's D3D12 queue lock and moves the texture into the
        /// <c>RENDER_TARGET</c> state Skia draws in, from whatever layout Stride left it in. Throws if a previous <see cref="Begin"/> hasn't
        /// been closed with <see cref="End"/> yet.
        /// </summary>
        public void Begin(bool clear = true)
        {
            if (_target == null)
                throw new ObjectDisposedException(nameof(SkiaStrideD3D12RenderTarget2D));
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            _target.BeginDraw();
            _hasBegun = true;

            if (clear)
                _target.Surface.Canvas.Clear();
        }

        /// <summary>
        /// Ends the render pass started by <see cref="Begin"/>: submits Skia's work to Stride's queue
        /// (releasing the queue lock), then composites the whole surface at native size and the
        /// origin via a <see cref="SpriteBatch"/>. <paramref name="graphicsContext"/> is normally
        /// <c>RenderDrawContext.GraphicsContext</c>, available inside a <c>SceneRendererBase</c>'s
        /// <c>DrawCore</c> (see <see cref="SkiaStrideD3D12SceneRenderer"/>).
        /// </summary>
        public void End(GraphicsContext graphicsContext)
        {
            ArgumentNullException.ThrowIfNull(graphicsContext);
            EndCore(graphicsContext);
        }

        /// <summary>
        /// Same as <see cref="End"/>, but skips the composite - use this when you draw
        /// <see cref="Texture"/> yourself. <see cref="Texture"/> is left in Stride's <c>Common</c>
        /// layout, so before sampling it call <c>CommandList.ResourceBarrierTransition(Texture,
        /// BarrierLayout.ShaderResource)</c>, as any Stride D3D12 render target needs.
        /// </summary>
        public void EndWithoutDrawing() => EndCore(graphicsContext: null);

        void EndCore(GraphicsContext? graphicsContext)
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");

            try
            {
                _target!.EndDraw();
            }
            finally
            {
                _hasBegun = false;
            }

            if (graphicsContext != null)
            {
                // Stride's D3D12 SpriteBatch does not move what it samples into a shader-readable
                // layout; its callers do (Stride.Rendering's ImageEffect, RenderTextureSceneRenderer).
                graphicsContext.CommandList.ResourceBarrierTransition(Texture, BarrierLayout.ShaderResource);
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
