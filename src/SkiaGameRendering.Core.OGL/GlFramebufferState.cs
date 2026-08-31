namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// The FBO and depth/stencil renderbuffer <see cref="GlSkiaSurfaceFactory"/> created to let
    /// Skia render into a texture owned by the host engine. Round-trip this back into
    /// <see cref="GlSkiaSurfaceFactory"/>'s Bind/Unbind/Dispose calls for that same target.
    /// </summary>
    public sealed class GlFramebufferState
    {
        // Plain fields (not properties) so glDelete*'s ref-int signature can target them directly.
        public int FramebufferId;
        public int RenderbufferId;
    }
}
