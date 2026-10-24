using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Owns the Skia-dedicated GL context (see <see cref="ISharedGlContext"/>: <see cref="WglSharedContext"/>
    /// on Windows, <see cref="GlxSharedContext"/> on Linux, <see cref="CglSharedContext"/> on macOS) for one raylib window, plus the
    /// <see cref="GRContext"/>/<see cref="GlFunctions"/> loaded against it. Callers are responsible
    /// for bracketing any Skia GL work with <see cref="BeginDraw"/>/<see cref="EndDraw"/> - the
    /// other members assume the Skia context is already current, mirroring how
    /// <c>SkiaGlBackend</c> splits context switching from the raw GL work in the MonoGame backend.
    /// </summary>
    internal sealed class SkiaRaylibContext : IDisposable
    {
        private readonly ISharedGlContext _platform = CreatePlatformGlContext();
        private GRContext? _grContext;
        private GlFunctions? _gl;

        private static ISharedGlContext CreatePlatformGlContext()
        {
            if (OperatingSystem.IsWindows())
                return new WglSharedContext();
            if (OperatingSystem.IsLinux())
                return new GlxSharedContext();
            if (OperatingSystem.IsMacOS())
                return new CglSharedContext();

            throw new PlatformNotSupportedException(
                "SkiaGameRendering.Raylib.OGL requires Windows (WGL), Linux (GLX), or macOS (CGL).");
        }

        public void Initialize()
        {
            IntPtr windowHandle;
            unsafe
            {
                windowHandle = (IntPtr)Raylib_cs.Raylib.GetWindowHandle();
            }

            _platform.CreateSharedContext(windowHandle);

            _platform.MakeSkiaContextCurrent();
            try
            {
                _gl = GlFunctions.Load(new SharedGlContextFunctionLoader(_platform));
                _grContext = GlGrContextFactory.Create(_gl);
            }
            finally
            {
                _platform.RestoreHostContext();
            }
        }

        internal void BeginDraw()
        {
            _platform.MakeSkiaContextCurrent();
            GrContext.ResetContext();
        }

        internal void EndDraw()
        {
            _platform.RestoreHostContext();
        }

        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            int glTextureId, int width, int height, SKColorType colorType, out GlFramebufferState renderState)
        {
            // TopLeft (Core.OGL's default): Raylib.DrawTexture draws texel row 0 at the top, so Skia
            // must write canvas row 0 there. BottomLeft shows the texture upside down.
            return GlSkiaSurfaceFactory.CreateSurface(
                GrContext, Gl, glTextureId, width, height, colorType, out renderState);
        }

        // Skia rewrites the shared texture on its own context, and raylib's context only sees that when
        // the texture is bound again (see SkiaSharedGlBackend.RefreshEngineView). Run it on raylib's
        // context, after EndDraw.
        internal void RefreshEngineView(int glTextureId) =>
            GlSkiaSurfaceFactory.RefreshSharedTexture(Gl, glTextureId);

        internal void BindForDrawing(GlFramebufferState renderState) =>
            GlSkiaSurfaceFactory.BindForDrawing(Gl, renderState);

        internal void UnbindAfterDrawing() =>
            GlSkiaSurfaceFactory.UnbindAfterDrawing(Gl);

        /// <summary>
        /// Runs <paramref name="teardown"/> on the Skia context (see <see cref="SkiaGlTeardown"/>). Gets false,
        /// and must make no GL calls, once raylib's window is closed.
        /// </summary>
        internal void RunOnSkiaContext(Action<bool> teardown) =>
            SkiaGlTeardown.Run(Raylib_cs.Raylib.IsWindowReady(), _grContext,
                _platform.MakeSkiaContextCurrent, _platform.RestoreHostContext, teardown);

        internal void DisposeRenderState(GlFramebufferState renderState) =>
            GlSkiaSurfaceFactory.DisposeRenderState(Gl, renderState);

        /// <summary>Frees a direct-draw FBO while raylib keeps the texture it drew into.</summary>
        internal void DisposeAttachmentRenderState(GlFramebufferState renderState) =>
            GlSkiaSurfaceFactory.DisposeRenderState(Gl, renderState, keepColor: true);

        // --- SkiaDirectCanvas ---

        /// <summary>
        /// Whether Skia's context draws into raylib's window when it is current. The CGL context is never
        /// attached to a drawable, so on macOS it has no back buffer to draw into.
        /// </summary>
        internal static bool SkiaContextSharesWindow => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

        /// <summary>What raylib's context is drawing into. Call it on raylib's context.</summary>
        internal GlDrawTarget QueryDrawTarget() => GlSkiaSurfaceFactory.QueryDrawTarget(Gl);

        /// <summary>glFlush on the current context.</summary>
        internal void Flush() => GlSkiaSurfaceFactory.Flush(Gl);

        /// <summary>Wraps the window's back buffer. Call it on Skia's context.</summary>
        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateBackBufferSurface(int width, int height) =>
            GlSkiaSurfaceFactory.CreateDefaultFramebufferSurface(GrContext, Gl, width, height, SKColorType.Rgba8888);

        /// <summary>
        /// Wraps the color attachment of raylib's bound framebuffer in an FBO on Skia's context. raylib draws
        /// into a <c>RenderTexture2D</c> with the same top-down projection it uses for the window, which puts
        /// its y = 0 in the last texel row, so the surface is <see cref="GRSurfaceOrigin.BottomLeft"/> like
        /// the back buffer.
        /// </summary>
        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateAttachmentSurface(
            GlDrawTarget target, int width, int height, out GlFramebufferState renderState) =>
            GlSkiaSurfaceFactory.CreateAttachmentSurface(GrContext, Gl, target, width, height, SKColorType.Rgba8888,
                out renderState, GRSurfaceOrigin.BottomLeft);

        private GRContext GrContext => _grContext ?? throw new InvalidOperationException("SkiaRaylibContext.Initialize was not called.");
        private GlFunctions Gl => _gl ?? throw new InvalidOperationException("SkiaRaylibContext.Initialize was not called.");

        public void Dispose()
        {
            if (_grContext == null)
                return;

            var grContext = _grContext;
            _grContext = null;
            var windowOpen = Raylib_cs.Raylib.IsWindowReady();
            SkiaGlTeardown.Run(windowOpen, grContext,
                _platform.MakeSkiaContextCurrent, _platform.RestoreHostContext, _ => grContext.Dispose());

            // A closed window can take the native display a GLX context needs with it (GLFW closes the X
            // display on terminate), so the context then goes with the process.
            if (windowOpen)
                _platform.Dispose();
        }
    }
}
