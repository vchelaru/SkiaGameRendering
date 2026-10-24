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
    /// <paramref name="origin"/> in <see cref="CreateSurface"/> picks which texel row canvas row 0
    /// lands in. The default, <see cref="GRSurfaceOrigin.TopLeft"/>, writes it to texel row 0, which
    /// is what every engine here (MonoGame, raylib, Godot) draws as the top of a texture.
    /// <see cref="GRSurfaceOrigin.BottomLeft"/> only suits a host that draws texel row 0 at the bottom.
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

        /// <summary>
        /// What the current context is drawing into: the default framebuffer, or the color attachment
        /// of the bound FBO. Call it on the engine's context. FBOs are not shared between contexts but
        /// textures and renderbuffers are, so this is what lets a second context draw into the same target.
        /// </summary>
        public static GlDrawTarget QueryDrawTarget(GlFunctions gl)
        {
            gl.GetInteger(GL_DRAW_FRAMEBUFFER_BINDING, out var framebufferId);
            if (framebufferId == 0)
                return default;

            // GL_FRAMEBUFFER means the draw framebuffer here, and unlike GL_DRAW_FRAMEBUFFER it is valid on GLES 2.
            var type = gl.GetFramebufferAttachmentParameter(
                (int)FramebufferTarget.Framebuffer, GL_COLOR_ATTACHMENT0, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE);
            var name = gl.GetFramebufferAttachmentParameter(
                (int)FramebufferTarget.Framebuffer, GL_COLOR_ATTACHMENT0, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME);
            return new GlDrawTarget(framebufferId, type, name);
        }

        /// <summary>
        /// Wraps the default framebuffer (the window's back buffer) of the current context. Its sample
        /// count and stencil bits come from the window's pixel format, and row 0 is the bottom, so the
        /// surface uses <see cref="GRSurfaceOrigin.BottomLeft"/>.
        /// </summary>
        public static (SKSurface surface, GRBackendRenderTarget renderTarget) CreateDefaultFramebufferSurface(
            GRContext grContext, GlFunctions gl, int width, int height, SKColorType colorType)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.GetInteger(GL_SAMPLES, out var samples);
            // Not valid on a core-profile context, where it leaves the value at 0. Skia then avoids stencil.
            gl.GetInteger(GL_STENCIL_BITS, out var stencilBits);

            var framebufferInfo = new GRGlFramebufferInfo(0, colorType.ToGlSizedFormat());
            var backendRenderTarget = new GRBackendRenderTarget(width, height, samples, stencilBits, framebufferInfo);
            var surface = SKSurface.Create(grContext, backendRenderTarget, GRSurfaceOrigin.BottomLeft, colorType);
            if (surface == null)
            {
                backendRenderTarget.Dispose();
                throw new InvalidOperationException("Skia could not wrap the default framebuffer.");
            }

            return (surface, backendRenderTarget);
        }

        /// <summary>
        /// Builds an FBO on the current (Skia) context around <paramref name="target"/>'s color
        /// attachment, a texture or a multisampled renderbuffer owned by another context, plus a
        /// depth/stencil renderbuffer with the same sample count, and wraps it in a Skia surface.
        /// Rows run top-down by default, the same as <see cref="CreateSurface"/>; an engine that keeps its
        /// back buffer in an FBO renders that one bottom-up, like a window, so it passes
        /// <see cref="GRSurfaceOrigin.BottomLeft"/>.
        /// </summary>
        public static (SKSurface surface, GRBackendRenderTarget renderTarget) CreateAttachmentSurface(
            GRContext grContext, GlFunctions gl, GlDrawTarget target, int width, int height, SKColorType colorType,
            out GlFramebufferState framebufferState, GRSurfaceOrigin origin = GRSurfaceOrigin.TopLeft)
        {
            framebufferState = CreateAttachmentFramebuffer(gl, target, width, height, out var samples);

            var framebufferInfo = new GRGlFramebufferInfo((uint)framebufferState.FramebufferId, colorType.ToGlSizedFormat());
            var backendRenderTarget = new GRBackendRenderTarget(width, height, samples, 8, framebufferInfo);
            var surface = SKSurface.Create(grContext, backendRenderTarget, origin, colorType);
            if (surface == null)
            {
                backendRenderTarget.Dispose();
                DisposeRenderState(gl, framebufferState);
                throw new InvalidOperationException("Skia could not wrap the render target's framebuffer.");
            }

            return (surface, backendRenderTarget);
        }

        internal static GlFramebufferState CreateAttachmentFramebuffer(GlFunctions gl, GlDrawTarget target,
            int width, int height, out int samples)
        {
            samples = 0;
            if (target.AttachmentType == GL_RENDERBUFFER)
            {
                gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, target.AttachmentName);
                samples = gl.GetRenderbufferParameter(GL_RENDERBUFFER_SAMPLES);
                gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            }
            else if (target.AttachmentType != GL_TEXTURE)
            {
                throw new NotSupportedException(
                    $"The bound framebuffer's color attachment is GL object type 0x{target.AttachmentType:X4}, " +
                    "not a texture or renderbuffer.");
            }

            gl.GenRenderbuffers(1, out var renderbufferId);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbufferId);
            if (samples > 0)
            {
                var storageMultisample = gl.RenderbufferStorageMultisample
                    ?? throw new NotSupportedException("The GL driver lacks glRenderbufferStorageMultisample.");
                storageMultisample(RenderbufferTarget.Renderbuffer, samples, RenderbufferStorage.Depth24Stencil8, width, height);
            }
            else
            {
                gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);
            }
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            gl.GenFramebuffers(1, out var framebufferId);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebufferId);

            if (target.AttachmentType == GL_RENDERBUFFER)
                gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, target.AttachmentName);
            else
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.AttachmentName, 0);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, renderbufferId);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.StencilAttachment, RenderbufferTarget.Renderbuffer, renderbufferId);

            var state = new GlFramebufferState { FramebufferId = framebufferId, RenderbufferId = renderbufferId };
            var framebufferStatus = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            if (framebufferStatus != FramebufferErrorCode.FramebufferComplete)
            {
                DisposeRenderState(gl, state);
                throw new InvalidOperationException(
                    $"Skia framebuffer creation failed for the render target (status 0x{(int)framebufferStatus:X4}).");
            }

            return state;
        }

        /// <summary>
        /// Attaches <paramref name="target"/>'s color attachment to the FBO in <paramref name="state"/> again,
        /// on the current (Skia) context, so the FBO picks up a new object the engine made under the same name.
        /// </summary>
        public static void AttachColor(GlFunctions gl, GlFramebufferState state, GlDrawTarget target)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, state.FramebufferId);
            if (target.AttachmentType == GL_RENDERBUFFER)
                gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, target.AttachmentName);
            else
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.AttachmentName, 0);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        /// <summary>glFlush on the current context.</summary>
        public static void Flush(GlFunctions gl) => gl.Flush();

        public static void BindForDrawing(GlFunctions gl, GlFramebufferState state)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, state.FramebufferId);
        }

        /// <summary>
        /// Also flushes: the engine samples the texture from its own (shared) GL context, and GL only
        /// guarantees another context sees this one's writes after a flush. macOS shows a blank texture
        /// without it; SKSurface.Flush alone does not issue one.
        /// </summary>
        public static void UnbindAfterDrawing(GlFunctions gl)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            gl.Flush();
        }

        /// <summary>
        /// Binds the shared texture again on the engine's context, then puts back whatever was bound, so
        /// the engine's own binding cache stays right. Binding nothing first matters: a bind of the
        /// texture that is already bound is a no-op, and a bind is what makes the driver show the
        /// engine's context what Skia's context wrote.
        /// </summary>
        public static void RefreshSharedTexture(GlFunctions gl, int textureId)
        {
            gl.GetInteger(0x8069 /* GL_TEXTURE_BINDING_2D */, out int previous);
            gl.BindTexture(0x0DE1 /* GL_TEXTURE_2D */, 0);
            gl.BindTexture(0x0DE1, textureId);
            gl.BindTexture(0x0DE1, previous);
        }

        /// <summary>
        /// Deletes the FBO and renderbuffer. Call it on Skia's context, through <see cref="SkiaGlTeardown.Run"/>.
        /// The color attachment's contents are discarded too unless <paramref name="keepColor"/> is set, which a
        /// caller needs when the engine keeps using that texture after Skia's FBO is gone.
        /// </summary>
        public static void DisposeRenderState(GlFunctions gl, GlFramebufferState state, bool keepColor = false)
        {
            if (state.FramebufferId > 0)
            {
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, state.FramebufferId);
                gl.InvalidateFramebuffer?.Invoke(FramebufferTarget.Framebuffer, keepColor ? 2 : 3, keepColor
                    ? new[] { FramebufferAttachment.DepthAttachment, FramebufferAttachment.StencilAttachment }
                    : new[]
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
