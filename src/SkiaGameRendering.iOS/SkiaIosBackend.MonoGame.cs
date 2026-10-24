using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    public partial class SkiaIosBackend
    {
        private const int GL_TEXTURE_BINDING_2D = 0x8069;

        [DllImport(EaglSharedContext.OpenGLESLibrary)]
        private static extern void glGetIntegerv(int pname, out int data);

        readonly MonoGameBoundRenderTarget _boundRenderTarget = new();

        // Called on MonoGame's context right after the texture was created, which leaves it bound.
        private protected override int CaptureGlTextureId(Texture2D texture)
        {
            glGetIntegerv(GL_TEXTURE_BINDING_2D, out var textureId);
            return textureId;
        }

        private protected override RenderTarget2D? GetBoundRenderTarget() => _boundRenderTarget.Get(GraphicsDevice);
    }
}
