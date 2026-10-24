#nullable enable
using System;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering
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
    /// - <see cref="DrawDirect"/> asks Unity's context what its draw framebuffer is, then draws on the
    ///   same window's default framebuffer, or on a Skia-side FBO around the bound FBO's color attachment
    ///   (textures and renderbuffers are shared, FBOs are not). Both are bottom-up in GL, so the picture
    ///   needs no flip. The FBO is built per pass: Unity gives no notice when it deletes a render texture,
    ///   and a new one can reuse the name.
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

        // The window's default framebuffer, reused until the screen's size changes.
        SKSurface? _backBufferSurface;
        GRBackendRenderTarget? _backBufferRenderTarget;
        int _backBufferWidth;
        int _backBufferHeight;

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

        internal override void Draw(SkiaRenderTarget2D.RenderState target, SKPicture picture)
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

        internal override void DrawDirect(SKPicture picture, int width, int height, bool flip, IntPtr renderBuffer)
        {
            // Still on Unity's context: what it is drawing into, and its draws so far submitted.
            var glTarget = GlSkiaSurfaceFactory.QueryDrawTarget(_gl);
            GlSkiaSurfaceFactory.Flush(_gl);
            _platform.MakeSkiaContextCurrent();
            try
            {
                _grContext.ResetContext();
                SKSurface surface;
                GRBackendRenderTarget? passRenderTarget = null;
                var framebuffer = new GlFramebufferState();
                if (glTarget.IsDefaultFramebuffer)
                {
                    surface = GetBackBufferSurface(width, height);
                }
                else
                {
                    (surface, passRenderTarget) = GlSkiaSurfaceFactory.CreateAttachmentSurface(
                        _grContext, _gl, glTarget, width, height, SKColorType.Rgba8888, out framebuffer,
                        GRSurfaceOrigin.BottomLeft);
                }

                try
                {
                    GlSkiaSurfaceFactory.BindForDrawing(_gl, framebuffer);
                    var canvas = surface.Canvas;
                    var saveCount = canvas.Save();
                    canvas.DrawPicture(picture);
                    canvas.RestoreToCount(saveCount);
                    surface.Flush();
                }
                finally
                {
                    GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);
                    if (passRenderTarget != null)
                    {
                        surface.Dispose();
                        passRenderTarget.Dispose();
                        GlSkiaSurfaceFactory.DisposeRenderState(_gl, framebuffer, keepColor: true);
                    }
                }
            }
            finally
            {
                _platform.RestoreHostContext();
            }
            // A multisampled texture is only read by Unity's resolve, never sampled, so there is no binding to refresh.
            if (glTarget.IsTexture && !glTarget.IsMultisampledTexture)
                GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, glTarget.AttachmentName);
        }

        SKSurface GetBackBufferSurface(int width, int height)
        {
            if (_backBufferSurface != null && _backBufferWidth == width && _backBufferHeight == height)
                return _backBufferSurface;

            _backBufferSurface?.Dispose();
            _backBufferRenderTarget?.Dispose();
            (_backBufferSurface, _backBufferRenderTarget) =
                GlSkiaSurfaceFactory.CreateDefaultFramebufferSurface(_grContext, _gl, width, height, SKColorType.Rgba8888);
            _backBufferWidth = width;
            _backBufferHeight = height;
            return _backBufferSurface;
        }

        internal override void DisposeTarget(SkiaRenderTarget2D.RenderState target)
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
                _ =>
                {
                    _backBufferSurface?.Dispose();
                    _backBufferRenderTarget?.Dispose();
                    _grContext.Dispose();
                });
            _platform.Dispose();
        }
    }
}
