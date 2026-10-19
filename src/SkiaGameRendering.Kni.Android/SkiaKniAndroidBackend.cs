using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Kni.Android
{
    internal class KniAndroidTextureState
    {
        internal int TextureId;
    }

    /// <summary>
    /// SkiaBackend for KNI's Android (GLES) platform. Same design as the desktop GL backends: a second
    /// EGL context (<see cref="EglSharedContext"/>) shares KNI's, Skia gets it to leave in whatever
    /// state it likes, and the texture objects are visible from both. KNI caches GL state, which is
    /// why Skia must not draw on KNI's own context.
    /// <para>
    /// No reflection: KNI creates its context through Java's EGL10 but makes it current on the
    /// thread's native EGL state, which <see cref="EglSharedContext"/> reads with the native
    /// <c>eglGetCurrent*</c> calls, and <c>Texture2D.GetSharedHandle()</c> is public.
    /// </para>
    /// </summary>
    public class SkiaKniAndroidBackend : SkiaBackend
    {
        readonly EglSharedContext _egl = new();
        GRContext _grContext = null!;
        GlFunctions _gl = null!;

        public override GRContext GRContext => _grContext;

        /// <summary>The Skia context's GLES major version and whether it runs surfaceless; for diagnostics.</summary>
        public string Description => $"GLES {_egl.ClientVersion}, {(_egl.Surfaceless ? "surfaceless" : "host surface")}";

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            _egl.CreateSharedContext(graphicsDevice.PresentationParameters.DeviceWindowHandle);

            _egl.MakeSkiaContextCurrent();
            try
            {
                _gl = GlFunctions.Load(new SharedGlContextFunctionLoader(_egl));
                _grContext = GlGrContextFactory.Create(_gl);
            }
            finally
            {
                _egl.RestoreHostContext();
            }
        }

        internal override void BeginDraw()
        {
            _egl.MakeSkiaContextCurrent();
            _grContext.ResetContext();
        }

        internal override void EndDraw()
        {
            _egl.RestoreHostContext();
        }

        // A plain Texture2D has no shared-handle support in KNI - only a RenderTarget2D constructed
        // with shared:true exposes GetSharedHandle(). DepthFormat.None/0 samples because Skia
        // manages its own depth/stencil renderbuffer in GlSkiaSurfaceFactory.
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents, shared: true);
        }

        internal override object CaptureTextureHandle(Texture2D texture)
        {
            return new KniAndroidTextureState { TextureId = (int)texture.GetSharedHandle() };
        }

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var state = (KniAndroidTextureState)textureHandle;

            var result = GlSkiaSurfaceFactory.CreateSurface(
                _grContext, _gl, state.TextureId, width, height, colorType, out var framebufferState);

            renderState = framebufferState;
            return result;
        }

        internal override void BindForDrawing(object renderState)
        {
            GlSkiaSurfaceFactory.BindForDrawing(_gl, (GlFramebufferState)renderState);
        }

        internal override void UnbindAfterDrawing()
        {
            GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);
        }

        internal override void DisposeRenderState(object renderState)
        {
            GlSkiaSurfaceFactory.DisposeRenderState(_gl, (GlFramebufferState)renderState);
        }

        public override void Dispose()
        {
            _grContext?.Dispose();
            _egl.Dispose();
        }
    }
}
