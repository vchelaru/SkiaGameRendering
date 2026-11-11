using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Draws with SkiaSharp straight into the draw framebuffer the app has bound when <see cref="Begin"/> is
    /// called: the window's back buffer, or an FBO of its own. Same Begin/Canvas/End shape as
    /// <see cref="SkiaRenderTarget2D"/>, minus the size and the texture: there is no intermediate texture and no
    /// composite pass.
    /// <code>
    /// var skia = new SkiaDirectCanvas(context);
    /// // each frame, after the app's own drawing:
    /// skia.Begin();
    /// skia.Canvas.DrawCircle(100, 100, 50, paint);
    /// skia.End();
    /// // the app sets its GL state again, draws more, and swaps buffers
    /// </code>
    /// <para>
    /// <b>Contract.</b> <see cref="Begin"/> does not clear; Skia draws on top of what is there. The target's
    /// size is the current GL viewport, so set the viewport to the whole target first (a partial viewport is
    /// treated as a smaller target). Canvas coordinates are pixels from the top left as a window shows them; in
    /// an FBO texture that is the last texel row. The target must be RGBA8: the default framebuffer is assumed
    /// to be, and an FBO's color attachment must be a <c>GL_TEXTURE_2D</c> or a renderbuffer. A multisampled
    /// target is drawn with Skia's multisampling. <see cref="End"/> restores only the draw framebuffer binding;
    /// program, vertex array, buffers, viewport, scissor, blend, depth/stencil and pixel-store state are left
    /// as Skia had them, so the app sets them again.
    /// </para>
    /// </summary>
    public sealed class SkiaDirectCanvas : IDisposable
    {
        readonly SkiaGlContext _context;

        // The back buffer's surface, reused until the window's size changes. An FBO's is built for each pass:
        // the app can delete an FBO and the next one can get the same GL name.
        SKSurface? _backBufferSurface;
        GRBackendRenderTarget? _backBufferRenderTarget;
        int _backBufferWidth;
        int _backBufferHeight;

        Pass? _pass;
        bool _isDisposed;

        sealed class Pass
        {
            internal SKSurface Surface = null!;
            internal int SaveCount;
            internal int AppFramebuffer;
            // Only for an FBO: the per-pass objects End frees.
            internal GRBackendRenderTarget? RenderTarget;
            internal GlFramebufferState? Framebuffer;
        }

        public SkiaDirectCanvas(SkiaGlContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            _context = context;
        }

        /// <summary>The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public SKCanvas Canvas => _pass?.Surface.Canvas
            ?? throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Starts a Skia pass into the app's current draw framebuffer. Throws <see cref="NotSupportedException"/>
        /// for an FBO whose color attachment is not a texture or a renderbuffer. Does not clear.
        /// </summary>
        public void Begin()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_pass != null)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            var gl = _context.Gl;
            var target = GlSkiaSurfaceFactory.QueryDrawTarget(gl);
            var (width, height) = _context.Textures.GetViewportSize();
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"The GL viewport is {width}x{height}; set it to the draw target's size before Begin.");

            var grContext = _context.GrContext;
            // The app changed GL state since Skia's last pass.
            grContext.ResetContext();
            var pass = new Pass { AppFramebuffer = target.FramebufferId };
            try
            {
                if (target.IsDefaultFramebuffer)
                {
                    pass.Surface = GetBackBufferSurface(width, height);
                }
                else
                {
                    var (surface, renderTarget) = GlSkiaSurfaceFactory.CreateAttachmentSurface(
                        grContext, gl, target, width, height, SKColorType.Rgba8888, out var framebuffer, GRSurfaceOrigin.BottomLeft);
                    pass.Surface = surface;
                    pass.RenderTarget = renderTarget;
                    pass.Framebuffer = framebuffer;
                    GlSkiaSurfaceFactory.BindForDrawing(gl, framebuffer);
                }
                pass.SaveCount = pass.Surface.Canvas.Save();
            }
            catch
            {
                DisposePassObjects(pass);
                Rebind(pass.AppFramebuffer);
                throw;
            }

            _pass = pass;
        }

        /// <summary>Flushes Skia's work and gives the app its framebuffer back.</summary>
        public void End()
        {
            var pass = _pass ?? throw new InvalidOperationException("Begin must be called before calling End.");
            _pass = null;

            try
            {
                pass.Surface.Canvas.RestoreToCount(pass.SaveCount);
                pass.Surface.Flush();
            }
            finally
            {
                DisposePassObjects(pass);
                Rebind(pass.AppFramebuffer);
            }
        }

        SKSurface GetBackBufferSurface(int width, int height)
        {
            if (_backBufferSurface != null && _backBufferWidth == width && _backBufferHeight == height)
                return _backBufferSurface;

            DisposeBackBufferSurface();
            (_backBufferSurface, _backBufferRenderTarget) = GlSkiaSurfaceFactory.CreateDefaultFramebufferSurface(
                _context.GrContext, _context.Gl, width, height, SKColorType.Rgba8888);
            _backBufferWidth = width;
            _backBufferHeight = height;
            return _backBufferSurface;
        }

        void DisposePassObjects(Pass pass)
        {
            if (pass.RenderTarget == null)
                return;

            pass.Surface?.Dispose();
            pass.RenderTarget.Dispose();
            if (pass.Framebuffer != null)
                GlSkiaSurfaceFactory.DisposeRenderState(_context.Gl, pass.Framebuffer, keepColor: true);
            pass.RenderTarget = null;
        }

        void DisposeBackBufferSurface()
        {
            _backBufferSurface?.Dispose();
            _backBufferSurface = null;
            _backBufferRenderTarget?.Dispose();
            _backBufferRenderTarget = null;
        }

        void Rebind(int framebuffer) =>
            GlSkiaSurfaceFactory.BindForDrawing(_context.Gl, new GlFramebufferState { FramebufferId = framebuffer });

        /// <summary>Call it with the app's GL context current, after <see cref="End"/> and before <see cref="SkiaGlContext.Dispose"/>.</summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;
            if (_pass != null)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            _isDisposed = true;
            DisposeBackBufferSurface();
        }
    }
}
