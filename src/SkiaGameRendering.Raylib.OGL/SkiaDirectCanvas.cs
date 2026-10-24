using Raylib_cs;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using RaylibApi = Raylib_cs.Raylib;

namespace SkiaGameRendering
{
    /// <summary>
    /// Draws with SkiaSharp straight into whatever raylib is rendering to when <see cref="Begin"/> is called:
    /// the window's back buffer, or the <c>RenderTexture2D</c> bound with <c>BeginTextureMode</c>. Same
    /// Begin/Canvas/End shape as <see cref="SkiaRenderTarget2D"/>, minus the size and the texture:
    /// there is no intermediate texture and no <c>DrawTexture</c> composite.
    /// <code>
    /// var skia = new SkiaDirectCanvas();
    /// Raylib.BeginDrawing();
    /// Raylib.DrawRectangle(0, 0, 100, 100, Color.Green); // background
    /// skia.Begin();
    /// skia.Canvas.DrawCircle(100, 100, 50, paint);
    /// skia.End();
    /// Raylib.DrawText("on top", 10, 10, 20, Color.White); // foreground
    /// Raylib.EndDrawing();
    /// </code>
    /// <see cref="Begin"/> does not clear; Skia draws on top of what is already there. Coordinates are target
    /// pixels from the top left, the same as raylib's own 2D drawing; raylib's camera, viewport and scissor
    /// are ignored. Not supported on macOS (see <see cref="SkiaRenderer.SupportsDirectCanvas"/>).
    /// </summary>
    public sealed class SkiaDirectCanvas : IDisposable
    {
        private readonly SkiaRaylibContext _context;

        // The window's back buffer surface, reused across passes until the window's size changes. A render
        // texture's is built for each pass instead: raylib gives no notice when one is unloaded, and the next
        // one it loads can get the same GL names, so a cached FBO could keep drawing into the unloaded texture.
        private SKSurface? _backBufferSurface;
        private GRBackendRenderTarget? _backBufferRenderTarget;
        private int _backBufferWidth;
        private int _backBufferHeight;

        private Pass? _pass;
        private bool _isDisposed;

        private sealed class Pass
        {
            internal SKSurface Surface = null!;
            internal int SaveCount;
            internal GlDrawTarget Gl;
            // Only for a render texture: the per-pass objects End frees.
            internal GRBackendRenderTarget? RenderTarget;
            internal GlFramebufferState? Framebuffer;
        }

        /// <summary>
        /// Must be called after <c>Raylib.InitWindow</c>. Throws <see cref="NotSupportedException"/> where
        /// <see cref="SkiaRenderer.SupportsDirectCanvas"/> is false.
        /// </summary>
        public SkiaDirectCanvas()
        {
            if (!SkiaRenderer.SupportsDirectCanvas)
                throw new NotSupportedException(
                    "SkiaDirectCanvas needs Skia's GL context on raylib's window, which it only has on " +
                    "Windows and Linux. Use SkiaRenderTarget2D instead.");

            _context = SkiaRenderer.EnsureInitialized();
        }

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _pass?.Surface.Canvas
            ?? throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Begins a render pass into whatever raylib has bound right now: draws raylib has batched so far
        /// are submitted first, so Skia's land on top of them. Call it between <c>BeginDrawing</c> and
        /// <c>EndDrawing</c>, optionally inside <c>BeginTextureMode</c>/<c>EndTextureMode</c>. Throws if a
        /// previous <see cref="Begin"/> hasn't been closed with <see cref="End"/> yet. Does not clear.
        /// </summary>
        public void Begin()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaDirectCanvas));
            if (_pass != null)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            // Still on raylib's context: submit its batch, then see what it is drawing into.
            Rlgl.DrawRenderBatchActive();
            var glTarget = _context.QueryDrawTarget();
            int width, height;
            if (glTarget.IsDefaultFramebuffer)
            {
                width = RaylibApi.GetRenderWidth();
                height = RaylibApi.GetRenderHeight();
            }
            else
            {
                // BeginTextureMode sets these to the render texture's size.
                width = Rlgl.GetFramebufferWidth();
                height = Rlgl.GetFramebufferHeight();
            }
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"raylib's current target is {width}x{height}.");

            _context.Flush();

            _context.BeginDraw();
            var pass = new Pass { Gl = glTarget };
            try
            {
                var framebuffer = new GlFramebufferState();
                if (glTarget.IsDefaultFramebuffer)
                {
                    pass.Surface = GetBackBufferSurface(width, height);
                }
                else
                {
                    var (surface, renderTarget) = _context.CreateAttachmentSurface(glTarget, width, height, out framebuffer);
                    pass.Surface = surface;
                    pass.RenderTarget = renderTarget;
                    pass.Framebuffer = framebuffer;
                }

                _context.BindForDrawing(framebuffer);
                pass.SaveCount = pass.Surface.Canvas.Save();
            }
            catch
            {
                DisposePassObjects(pass);
                _context.UnbindAfterDrawing();
                _context.EndDraw();
                throw;
            }

            _pass = pass;
        }

        /// <summary>
        /// Ends the render pass started by <see cref="Begin"/> and hands the target back to raylib, so its
        /// later draws land on top of Skia's. Throws if <see cref="Begin"/> wasn't called first.
        /// </summary>
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
                try
                {
                    DisposePassObjects(pass);
                    // Also a glFlush, so raylib's later draws land on top of Skia's.
                    _context.UnbindAfterDrawing();
                }
                finally
                {
                    _context.EndDraw();
                }
            }

            // raylib's context only sees Skia's writes to the render texture once it binds it again.
            if (pass.Gl.IsTexture)
                _context.RefreshEngineView(pass.Gl.AttachmentName);
        }

        private SKSurface GetBackBufferSurface(int width, int height)
        {
            if (_backBufferSurface != null && _backBufferWidth == width && _backBufferHeight == height)
                return _backBufferSurface;

            DisposeBackBufferSurface();
            (_backBufferSurface, _backBufferRenderTarget) = _context.CreateBackBufferSurface(width, height);
            _backBufferWidth = width;
            _backBufferHeight = height;
            return _backBufferSurface;
        }

        /// <summary>Frees a render texture pass's surface and FBO. Skia's context must be current.</summary>
        private void DisposePassObjects(Pass pass)
        {
            if (pass.RenderTarget == null)
                return;

            pass.Surface?.Dispose();
            pass.RenderTarget.Dispose();
            if (pass.Framebuffer != null)
                _context.DisposeAttachmentRenderState(pass.Framebuffer);
        }

        private void DisposeBackBufferSurface()
        {
            _backBufferSurface?.Dispose();
            _backBufferSurface = null;
            _backBufferRenderTarget?.Dispose();
            _backBufferRenderTarget = null;
        }

        /// <summary>Call before <see cref="SkiaRenderer.Dispose"/>.</summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;
            if (_pass != null)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");

            _isDisposed = true;
            if (_backBufferSurface != null)
                _context.RunOnSkiaContext(_ => DisposeBackBufferSurface());
        }
    }
}
