namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// A context's current draw target, from <see cref="GlSkiaSurfaceFactory.QueryDrawTarget"/>.
    /// <see cref="FramebufferId"/> 0 is the default framebuffer, and then the attachment fields are 0.
    /// Otherwise <see cref="AttachmentType"/> is GL_TEXTURE or GL_RENDERBUFFER and
    /// <see cref="AttachmentName"/> is that object's GL name. <see cref="TextureSamples"/> is above 0 for a
    /// GL_TEXTURE_2D_MULTISAMPLE texture (a plain texture's is 0), which only attaches to an FBO as that target.
    /// </summary>
    public readonly record struct GlDrawTarget(int FramebufferId, int AttachmentType, int AttachmentName, int TextureSamples = 0)
    {
        public bool IsDefaultFramebuffer => FramebufferId == 0;
        public bool IsTexture => AttachmentType == GlConstants.GL_TEXTURE;
        public bool IsMultisampledTexture => IsTexture && TextureSamples > 0;
    }
}
