using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering
{
    internal sealed class GlTextureState
    {
        internal int TextureId;
    }

    /// <summary>
    /// The part every GL backend that draws on a second context sharing the engine's objects has in common:
    /// MonoGame DesktopGL, FNA, KNI DesktopGL and KNI Android. A subclass supplies how to create and switch
    /// between the two contexts and where the engine keeps a texture's GL name. Everything else, including
    /// making the engine's context look at a rewritten texture again, lives here once, so a GL fix reaches
    /// all of them. Linked as source into each of those projects, like <see cref="SkiaBackend"/>.
    /// </summary>
    public abstract class SkiaSharedGlBackend : SkiaBackend
    {
        private protected GRContext _grContext = null!;
        private protected GlFunctions _gl = null!;

        public override GRContext GRContext => _grContext;

        private protected abstract void MakeSkiaContextCurrent();
        private protected abstract void MakeEngineContextCurrent();

        /// <summary>The GL name of <paramref name="texture"/>, called inside a <see cref="SkiaBackend.BeginDraw"/>/<see cref="SkiaBackend.EndDraw"/> pair.</summary>
        private protected abstract int CaptureGlTextureId(Texture2D texture);

        internal override void BeginDraw()
        {
            MakeSkiaContextCurrent();
            _grContext.ResetContext();
        }

        internal override void EndDraw() => MakeEngineContextCurrent();

        internal override object CaptureTextureHandle(Texture2D texture) =>
            new GlTextureState { TextureId = CaptureGlTextureId(texture) };

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var result = GlSkiaSurfaceFactory.CreateSurface(
                _grContext, _gl, ((GlTextureState)textureHandle).TextureId, width, height, colorType, out var framebufferState);
            renderState = framebufferState;
            return result;
        }

        internal override void BindForDrawing(object renderState) =>
            GlSkiaSurfaceFactory.BindForDrawing(_gl, (GlFramebufferState)renderState);

        internal override void UnbindAfterDrawing() => GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);

        internal override void DisposeRenderState(object renderState) =>
            GlSkiaSurfaceFactory.DisposeRenderState(_gl, (GlFramebufferState)renderState);

        // Skia rewrites the shared texture on its own GL context while the engine's context keeps
        // sampling it, and a context only sees another context's writes to a shared texture when the
        // texture is bound again. An engine that caches its bindings (KNI skips glBindTexture for a
        // texture it thinks is already bound) never rebinds a texture reused frame after frame, and a
        // full-screen fill came out as blocky patches around whatever else was drawn. Rebinding here
        // costs about 13 microseconds a frame, so every GL backend does it instead of only the engines
        // known to need it.
        internal override void RefreshEngineView(object textureHandle) =>
            GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, ((GlTextureState)textureHandle).TextureId);
    }
}
