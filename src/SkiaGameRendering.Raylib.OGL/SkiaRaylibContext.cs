using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Raylib.OGL
{
    /// <summary>
    /// Owns the Skia-dedicated GL context (see <see cref="IPlatformGlContext"/>: <see cref="Wgl"/>
    /// on Windows, <see cref="Glx"/> on Linux) for one raylib window, plus the
    /// <see cref="GRContext"/>/<see cref="GlFunctions"/> loaded against it. Callers are responsible
    /// for bracketing any Skia GL work with <see cref="BeginDraw"/>/<see cref="EndDraw"/> - the
    /// other members assume the Skia context is already current, mirroring how
    /// <c>SkiaGlBackend</c> splits context switching from the raw GL work in the MonoGame backend.
    /// </summary>
    internal sealed class SkiaRaylibContext : IDisposable
    {
        private readonly IPlatformGlContext _platform = CreatePlatformGlContext();
        private GRContext? _grContext;
        private GlFunctions? _gl;

        private static IPlatformGlContext CreatePlatformGlContext()
        {
            if (OperatingSystem.IsWindows())
                return new Wgl();
            if (OperatingSystem.IsLinux())
                return new Glx();

            throw new PlatformNotSupportedException(
                "SkiaGameRendering.Raylib.OGL requires Windows (WGL) or Linux (GLX). macOS is not implemented (see issue #10).");
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
                _gl = GlFunctions.Load(new PlatformGlFunctionLoader(_platform));
                _grContext = GRContext.CreateGl();
            }
            finally
            {
                _platform.MakeEngineContextCurrent();
            }
        }

        internal void BeginDraw()
        {
            _platform.MakeSkiaContextCurrent();
            GrContext.ResetContext();
        }

        internal void EndDraw()
        {
            _platform.MakeEngineContextCurrent();
        }

        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            int glTextureId, int width, int height, SKColorType colorType, out GlFramebufferState renderState)
        {
            // BottomLeft matches raylib/rlgl's own texture sampling convention (v=0 at the bottom),
            // so the resulting texture can be handed straight to Raylib.DrawTexture* with no
            // per-draw flip - unlike the MonoGame backend (TopLeft, the Core.OGL default), which
            // relies on MonoGame's own render-target sampling convention instead.
            return GlSkiaSurfaceFactory.CreateSurface(
                GrContext, Gl, glTextureId, width, height, colorType, out renderState, GRSurfaceOrigin.BottomLeft);
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
        }
    }
}
