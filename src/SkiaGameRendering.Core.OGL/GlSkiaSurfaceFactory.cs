using SkiaSharp;
using static SkiaGameRendering.Core.OGL.GlConstants;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// Wraps an existing raw GL texture in an FBO (with a depth/stencil renderbuffer) and hands
    /// back a Skia <see cref="SKSurface"/> that renders directly into that texture. Engine-agnostic:
    /// callers supply the raw GL texture id and loaded <see cref="GlFunctions"/>, nothing about how
    /// the texture or GL context were created.
    /// <para>
    /// <paramref name="origin"/> in <see cref="CreateSurface"/> controls whether the resulting
    /// texture, when sampled with plain OpenGL texture coordinates (v=0 at the bottom), matches
    /// what was drawn on the Skia canvas. Defaults to <see cref="GRSurfaceOrigin.TopLeft"/> to
    /// preserve existing callers (e.g. MonoGame, which flips elsewhere); callers that sample the
    /// texture directly with normal GL semantics (e.g. raylib) should pass
    /// <see cref="GRSurfaceOrigin.BottomLeft"/> instead to avoid a manual per-draw flip.
    /// </para>
    /// </summary>
    public static class GlSkiaSurfaceFactory
    {
        public static (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            GRContext grContext, GlFunctions gl, int glTextureId, int width, int height, SKColorType colorType,
            out GlFramebufferState framebufferState, GRSurfaceOrigin origin = GRSurfaceOrigin.TopLeft)
        {
            framebufferState = CreateFramebuffer(gl, glTextureId, width, height, out var samples);

            var maxSamples = grContext.GetMaxSurfaceSampleCount(colorType);
            if (samples > maxSamples)
                samples = maxSamples;

            var skiaFramebufferInfo = new GRGlFramebufferInfo((uint)framebufferState.FramebufferId, colorType.ToGlSizedFormat());
            var backendRenderTarget = new GRBackendRenderTarget(width, height, samples, 8, skiaFramebufferInfo);
            var surface = SKSurface.Create(grContext, backendRenderTarget, origin, colorType);

            return (surface, backendRenderTarget);
        }

        /// <summary>
        /// The pure-GL half of <see cref="CreateSurface"/>: everything up to and including a complete
        /// FBO, with no Skia object involved. Split out so the GL call sequence can be driven by a
        /// fake <see cref="IGlFunctionLoader"/> with no GL context and no GPU.
        /// </summary>
        internal static GlFramebufferState CreateFramebuffer(GlFunctions gl, int glTextureId, int width, int height,
            out int samples)
        {
            gl.GetInteger(GL_SAMPLES, out samples);

            gl.GenRenderbuffers(1, out var renderbufferId);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbufferId);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            gl.GenFramebuffers(1, out var framebufferId);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebufferId);

            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, glTextureId, 0);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, renderbufferId);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.StencilAttachment, RenderbufferTarget.Renderbuffer, renderbufferId);

            var framebufferStatus = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (framebufferStatus != FramebufferErrorCode.FramebufferComplete &&
                framebufferStatus != FramebufferErrorCode.FramebufferCompleteExt)
                throw new Exception("Skia framebuffer creation failed.");

            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            return new GlFramebufferState { FramebufferId = framebufferId, RenderbufferId = renderbufferId };
        }

        public static void BindForDrawing(GlFunctions gl, GlFramebufferState state)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, state.FramebufferId);
        }

        public static void UnbindAfterDrawing(GlFunctions gl)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public static void DisposeRenderState(GlFunctions gl, GlFramebufferState state)
        {
            if (state.FramebufferId > 0)
            {
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, state.FramebufferId);
                gl.InvalidateFramebuffer(FramebufferTarget.Framebuffer, 3, new[]
                {
                    FramebufferAttachment.ColorAttachment0,
                    FramebufferAttachment.DepthAttachment,
                    FramebufferAttachment.StencilAttachment,
                });
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, 0, 0);
                gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, 0);
                gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.StencilAttachment, RenderbufferTarget.Renderbuffer, 0);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                gl.DeleteFramebuffers(1, ref state.FramebufferId);
            }

            if (state.RenderbufferId > 0)
                gl.DeleteRenderbuffers(1, ref state.RenderbufferId);
        }
    }
}
