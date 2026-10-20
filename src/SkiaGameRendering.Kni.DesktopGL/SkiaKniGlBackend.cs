using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Kni.DesktopGL
{
    internal class KniGlTextureState
    {
        internal int TextureId;
    }

    public class SkiaKniGlBackend : SkiaBackend
    {
        IntPtr _windowId;
        IntPtr _engineContextId;
        IntPtr _skiaContextId;
        GRContext _grContext = null!;
        GlFunctions _gl = null!;

        public override GRContext GRContext => _grContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            _windowId = graphicsDevice.PresentationParameters.DeviceWindowHandle;
            _engineContextId = KniGlWrapper.GetCurrentContext();

            KniGlWrapper.ShareWithCurrentContext();
            _skiaContextId = KniGlWrapper.CreateGLContext(_windowId);
            if (_skiaContextId == IntPtr.Zero)
                throw new Exception("Sdl.GL.CreateGLContext failed.");

            MakeSkiaContextCurrent();
            _gl = GlFunctions.Load(new KniGlFunctionLoader());
            _grContext = GlGrContextFactory.Create(_gl);
            MakeEngineContextCurrent();
        }

        void MakeSkiaContextCurrent() => KniGlWrapper.MakeCurrent(_windowId, _skiaContextId);
        void MakeEngineContextCurrent() => KniGlWrapper.MakeCurrent(_windowId, _engineContextId);

        internal override void BeginDraw()
        {
            MakeSkiaContextCurrent();
            _grContext.ResetContext();
        }

        internal override void EndDraw()
        {
            MakeEngineContextCurrent();
        }

        // A plain Texture2D has no shared-handle support in KNI - only RenderTarget2D constructed
        // with shared:true exposes GetSharedHandle(). DepthFormat.None/0 samples because Skia
        // manages its own depth/stencil renderbuffer in GlSkiaSurfaceFactory.
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents, shared: true);
        }

        internal override object CaptureTextureHandle(Texture2D texture)
        {
            return new KniGlTextureState { TextureId = (int)texture.GetSharedHandle() };
        }

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var state = (KniGlTextureState)textureHandle;

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

        // Skia rewrites the shared texture on its own GL context. KNI's texture binding cache skips
        // glBindTexture when the same texture is already bound on the unit, and the driver only
        // refreshes the engine context's view of a shared texture when it is bound again, so a
        // texture reused frame after frame keeps showing most of its first contents (a full-screen
        // fill came out as blocky patches around whatever else was drawn). Binding the texture again
        // directly costs about 13 microseconds a frame; drawing a different texture first so that KNI
        // rebinds it worked too but cost about 200.
        internal override void RefreshEngineView(Texture2D texture)
        {
            GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, (int)texture.GetSharedHandle());
        }

        internal override void DisposeRenderState(object renderState)
        {
            GlSkiaSurfaceFactory.DisposeRenderState(_gl, (GlFramebufferState)renderState);
        }

        public override void Dispose()
        {
            _grContext?.Dispose();
        }
    }
}
