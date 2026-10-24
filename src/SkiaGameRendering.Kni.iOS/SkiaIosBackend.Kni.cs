using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    public partial class SkiaIosBackend
    {
        readonly KniBoundRenderTarget _boundRenderTarget = new();

        // A plain Texture2D has no shared-handle support in KNI: only a RenderTarget2D constructed with
        // shared:true exposes GetSharedHandle(). DepthFormat.None/0 samples because Skia manages its own
        // depth/stencil renderbuffer in GlSkiaSurfaceFactory.
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents, shared: true);
        }

        private protected override int CaptureGlTextureId(Texture2D texture) => (int)texture.GetSharedHandle();

        private protected override RenderTarget2D? GetBoundRenderTarget() => _boundRenderTarget.Get(GraphicsDevice);
    }
}
