using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// An OpenGL texture that SkiaSharp renders into on the GPU. Same shape as <c>SpriteBatch</c> and the other
    /// engines' <c>SkiaRenderTarget2D</c>:
    /// <code>
    /// target.Begin();
    /// target.Canvas.DrawCircle(100, 100, 100, paint);
    /// target.End();
    /// // bind target.GlTexture and draw with it
    /// </code>
    /// <para>
    /// <b>Contract.</b> <see cref="GlTexture"/> is a <c>GL_TEXTURE_2D</c> in <c>GL_RGBA8</c> with linear filtering
    /// and clamped edges, premultiplied alpha. Canvas row 0 is texel row 0 (v = 0), as when loading an image.
    /// Skia draws on the app's own GL context, so the constructor and <see cref="End"/> put back the GL state the
    /// app had (framebuffer, viewport, scissor, enables, blend, depth, stencil, color mask, program, vertex array,
    /// buffer, texture and sampler bindings, pixel-store modes) before handing control back. Vertex attributes of
    /// the default vertex array object are not restored.
    /// </para>
    /// </summary>
    public sealed class SkiaRenderTarget2D : IDisposable
    {
        readonly SkiaGlContext _context;
        readonly int _multiSampleCount;
        int _texture;
        GlFramebufferState? _framebuffer;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        readonly GlStateSnapshot _appState;
        bool _hasBegun;

        /// <param name="multiSampleCount">
        /// Samples per pixel. 0 (the default) is a plain surface; 2, 4 and 8 are the GPU's built-in edge smoothing:
        /// Skia draws into a multisampled buffer it owns and <see cref="End"/> resolves it into <see cref="GlTexture"/>
        /// on the GPU, which stays single-sample. Any other value, or a count the GPU lacks, throws
        /// <see cref="NotSupportedException"/>; it never falls back to 0.
        /// </param>
        public SkiaRenderTarget2D(SkiaGlContext context, int width, int height, SKColorType colorType = SKColorType.Rgba8888,
            int multiSampleCount = 0)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (colorType != SKColorType.Rgba8888)
                throw new NotSupportedException($"SkiaRenderTarget2D on BRUTAL OpenGL draws Rgba8888, not {colorType}.");
            if (multiSampleCount is not (0 or 2 or 4 or 8))
                throw new NotSupportedException($"multiSampleCount must be 0, 2, 4 or 8, not {multiSampleCount}.");

            _context = context;
            _multiSampleCount = multiSampleCount;
            Width = width;
            Height = height;
            _appState = context.CreateStateSnapshot();
            _appState.Save();
            try
            {
                _texture = context.Textures.CreateRgba8(width, height);
                if (multiSampleCount > 0)
                    _surface = GlSkiaSurfaceFactory.CreateMultiSampledSurface(
                        context.GrContext, _texture, width, height, colorType, multiSampleCount);
                else
                    (_surface, _renderTarget) = GlSkiaSurfaceFactory.CreateSurface(
                        context.GrContext, context.Gl, _texture, width, height, colorType, out _framebuffer);
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                // Creating the FBO and Skia's surface changes bindings; hand the app its own back.
                _appState.Restore();
            }
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>The <c>GL_TEXTURE_2D</c> name, valid until <see cref="Dispose"/>.</summary>
        public int GlTexture => _texture != 0 ? _texture : throw new ObjectDisposedException(nameof(SkiaRenderTarget2D));

        /// <summary>The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public SKCanvas Canvas => _hasBegun
            ? _surface!.Canvas
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>Binds the texture's framebuffer and starts a Skia pass. <paramref name="clear"/> clears to transparent.</summary>
        public void Begin(bool clear = true)
        {
            ObjectDisposedException.ThrowIf(_texture == 0, this);
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            _appState.Save();
            // The app changed GL state since Skia's last pass.
            _context.GrContext.ResetContext();
            // A multisampled surface has no FBO of ours; Skia binds its own.
            if (_multiSampleCount == 0)
                GlSkiaSurfaceFactory.BindForDrawing(_context.Gl, _framebuffer!);
            _hasBegun = true;
            if (clear)
                _surface!.Canvas.Clear();
        }

        /// <summary>Flushes Skia's work and puts back the app's GL state from <see cref="Begin"/>. The texture is ready to sample.</summary>
        public void End()
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");

            try
            {
                if (_multiSampleCount > 0)
                    GlSkiaSurfaceFactory.ResolveMultiSampled(_surface!);
                _surface!.Flush();
            }
            finally
            {
                _hasBegun = false;
                _appState.Restore();
            }
        }

        /// <summary>Call it with the app's GL context current and after <see cref="End"/>.</summary>
        public void Dispose()
        {
            if (_hasBegun)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            if (_surface == null && _framebuffer == null && _texture == 0)
                return;

            // Deleting the FBO binds framebuffers; put the app's back afterward.
            _appState.Save();
            try
            {
                _surface?.Dispose();
                _renderTarget?.Dispose();
                _surface = null;
                _renderTarget = null;
                if (_framebuffer != null)
                {
                    GlSkiaSurfaceFactory.DisposeRenderState(_context.Gl, _framebuffer);
                    _framebuffer = null;
                }
                if (_texture != 0)
                {
                    _context.Textures.Delete(_texture);
                    _texture = 0;
                }
            }
            finally
            {
                _appState.Restore();
            }
        }
    }
}
