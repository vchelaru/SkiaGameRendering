namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// A context's current draw target, from <see cref="GlSkiaSurfaceFactory.QueryDrawTarget"/>.
    /// <see cref="FramebufferId"/> 0 is the default framebuffer, and then the attachment fields are 0.
    /// Otherwise <see cref="AttachmentType"/> is GL_TEXTURE or GL_RENDERBUFFER and
    /// <see cref="AttachmentName"/> is that object's GL name.
    /// </summary>
    public readonly record struct GlDrawTarget(int FramebufferId, int AttachmentType, int AttachmentName)
    {
        public bool IsDefaultFramebuffer => FramebufferId == 0;
        public bool IsTexture => AttachmentType == GlConstants.GL_TEXTURE;
    }
}
