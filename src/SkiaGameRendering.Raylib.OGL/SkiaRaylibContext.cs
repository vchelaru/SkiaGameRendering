using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Raylib.OGL
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

        internal void BindForDrawing(GlFramebufferState renderState) =>
            GlSkiaSurfaceFactory.BindForDrawing(Gl, renderState);

        internal void UnbindAfterDrawing() =>
            GlSkiaSurfaceFactory.UnbindAfterDrawing(Gl);

        internal void DisposeRenderState(GlFramebufferState renderState) =>
            GlSkiaSurfaceFactory.DisposeRenderState(Gl, renderState);

        private GRContext GrContext => _grContext ?? throw new InvalidOperationException("SkiaRaylibContext.Initialize was not called.");
        private GlFunctions Gl => _gl ?? throw new InvalidOperationException("SkiaRaylibContext.Initialize was not called.");

        public void Dispose()
        {
            if (_grContext == null)
                return;

            BeginDraw();
            try
            {
                _grContext.Dispose();
                _grContext = null;
            }
            finally
            {
                EndDraw();
            }
            _platform.Dispose();
        }
    }
}
