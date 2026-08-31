namespace SkiaGameRendering.Core.OGL
{
    internal static class GlConstants
    {
        public const int GL_SAMPLES = 0x80a9;

        internal enum RenderbufferTarget
        {
            Renderbuffer = 0x8D41,
            RenderbufferExt = 0x8D41,
        }

        internal enum FramebufferTarget
        {
            Framebuffer = 0x8D40,
            FramebufferExt = 0x8D40,
            ReadFramebuffer = 0x8CA8,
        }

        internal enum RenderbufferStorage
        {
            Rgba8 = 0x8058,
            DepthComponent16 = 0x81a5,
            DepthComponent24 = 0x81a6,
            Depth24Stencil8 = 0x88F0,
            // GLES Values
            DepthComponent24Oes = 0x81A6,
            Depth24Stencil8Oes = 0x88F0,
            StencilIndex8 = 0x8D48,
        }

        internal enum FramebufferAttachment
        {
            ColorAttachment0 = 0x8CE0,
            ColorAttachment0Ext = 0x8CE0,
            DepthAttachment = 0x8D00,
            StencilAttachment = 0x8D20,
            ColorAttachmentExt = 0x1800,
            DepthAttachementExt = 0x1801,
            StencilAttachmentExt = 0x1802,
        }

        internal enum TextureTarget
        {
            Texture2D = 0x0DE1,
            Texture3D = 0x806F,
            TextureCubeMap = 0x8513,
            TextureCubeMapPositiveX = 0x8515,
            TextureCubeMapPositiveY = 0x8517,
            TextureCubeMapPositiveZ = 0x8519,
            TextureCubeMapNegativeX = 0x8516,
            TextureCubeMapNegativeY = 0x8518,
            TextureCubeMapNegativeZ = 0x851A,
        }

        internal enum FramebufferErrorCode
        {
            FramebufferUndefined = 0x8219,
            FramebufferComplete = 0x8CD5,
            FramebufferCompleteExt = 0x8CD5,
            FramebufferIncompleteAttachment = 0x8CD6,
            FramebufferIncompleteAttachmentExt = 0x8CD6,
            FramebufferIncompleteMissingAttachment = 0x8CD7,
            FramebufferIncompleteMissingAttachmentExt = 0x8CD7,
            FramebufferIncompleteDimensionsExt = 0x8CD9,
            FramebufferIncompleteFormatsExt = 0x8CDA,
            FramebufferIncompleteDrawBuffer = 0x8CDB,
            FramebufferIncompleteDrawBufferExt = 0x8CDB,
            FramebufferIncompleteReadBuffer = 0x8CDC,
            FramebufferIncompleteReadBufferExt = 0x8CDC,
            FramebufferUnsupported = 0x8CDD,
            FramebufferUnsupportedExt = 0x8CDD,
            FramebufferIncompleteMultisample = 0x8D56,
            FramebufferIncompleteLayerTargets = 0x8DA8,
            FramebufferIncompleteLayerCount = 0x8DA9,
        }
    }
}
