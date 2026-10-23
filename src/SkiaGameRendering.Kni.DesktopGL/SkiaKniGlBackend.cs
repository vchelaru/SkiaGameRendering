using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Kni.DesktopGL
{
    public class SkiaKniGlBackend : SkiaSharedGlBackend
    {
        IntPtr _windowId;
        IntPtr _engineContextId;
        IntPtr _skiaContextId;

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

        private protected override void MakeSkiaContextCurrent() => KniGlWrapper.MakeCurrent(_windowId, _skiaContextId);
        private protected override void MakeEngineContextCurrent() => KniGlWrapper.MakeCurrent(_windowId, _engineContextId);

        // A plain Texture2D has no shared-handle support in KNI - only RenderTarget2D constructed
        // with shared:true exposes GetSharedHandle(). DepthFormat.None/0 samples because Skia
        // manages its own depth/stencil renderbuffer in GlSkiaSurfaceFactory.
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents, shared: true);
        }

        private protected override int CaptureGlTextureId(Texture2D texture) => (int)texture.GetSharedHandle();

        private readonly KniBoundRenderTarget _boundRenderTarget = new();

        private protected override bool CanDrawDirect => true;

        private protected override RenderTarget2D? GetBoundRenderTarget() => _boundRenderTarget.Get(GraphicsDevice);

        public override void Dispose()
        {
            if (_grContext != null)
                DisposeDirectTargets();
            _grContext?.Dispose();
        }
    }
}
