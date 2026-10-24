#nullable enable
using System;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Unity
{
    /// <summary>
    /// OpenGL on Windows: Skia draws on a second GL context that shares Unity's texture namespace, so
    /// Skia's raw GL calls never touch the state Unity caches for its own context.
    ///
    /// MAINTENANCE NOTES:
    /// - Created on the render thread, where Unity's context is current. The shared context is created
    ///   on the window of that context's device context, which is what <c>wglShareLists</c> needs (the
    ///   same pixel format).
    /// - Skia's writes reach Unity's context only after a flush on Skia's side and a rebind of the
    ///   texture on Unity's (<see cref="GlSkiaSurfaceFactory.RefreshSharedTexture"/>), which restores
    ///   the binding it replaces.
    /// </summary>
    internal sealed class OpenGlUnityBackend : SkiaUnityBackend
    {
        [DllImport("opengl32.dll")]
        static extern IntPtr wglGetCurrentDC();

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromDC(IntPtr hdc);

        readonly WglSharedContext _platform = new WglSharedContext();
        readonly GlFunctions _gl;
        readonly GRContext _grContext;

        internal OpenGlUnityBackend()
        {
            var window = WindowFromDC(wglGetCurrentDC());
            if (window == IntPtr.Zero)
                throw new InvalidOperationException("Unity's OpenGL context is not current on a window's device context.");

            _platform.CreateSharedContext(window);
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

        internal override void Draw(SkiaUnityRenderTarget.RenderState target, SKPicture picture)
        {
            _platform.MakeSkiaContextCurrent();
            try
            {
                _grContext.ResetContext();
                if (target.Surface == null)
                {
                    (target.Surface, target.BackendRenderTarget) = GlSkiaSurfaceFactory.CreateSurface(
                        _grContext, _gl, (int)target.NativeTexture, target.Width, target.Height, SKColorType.Rgba8888,
                        out var framebuffer);
                    target.TextureState = framebuffer;
                }
                else
                {
                    GlSkiaSurfaceFactory.BindForDrawing(_gl, (GlFramebufferState)target.TextureState!);
                }

                PlayBack(target.Surface, picture, target.Height);
                target.Surface.Flush();
                GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);
            }
            finally
            {
                _platform.RestoreHostContext();
            }
            // On Unity's context, now that it is current again.
            GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, (int)target.NativeTexture);
        }

        internal override void DisposeTarget(SkiaUnityRenderTarget.RenderState target)
        {
            var surface = target.Surface;
            var backendRenderTarget = target.BackendRenderTarget;
            var framebuffer = target.TextureState as GlFramebufferState;
            target.Surface = null;
            target.BackendRenderTarget = null;
            target.TextureState = null;
            if (surface == null && framebuffer == null)
                return;

            SkiaGlTeardown.Run(true, _grContext, _platform.MakeSkiaContextCurrent, _platform.RestoreHostContext, current =>
            {
                surface?.Dispose();
                backendRenderTarget?.Dispose();
                if (framebuffer != null && current)
                    GlSkiaSurfaceFactory.DisposeRenderState(_gl, framebuffer, keepColor: true);
            });
        }

        public override void Dispose()
        {
            SkiaGlTeardown.Run(true, _grContext, _platform.MakeSkiaContextCurrent, _platform.RestoreHostContext,
                _ => _grContext.Dispose());
            _platform.Dispose();
        }
    }
}
