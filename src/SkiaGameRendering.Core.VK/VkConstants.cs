namespace SkiaGameRendering.Core.VK
{
    /// <summary>
    /// Raw <c>VkImageLayout</c>, pipeline-stage, access, usage and aspect values host adapters need
    /// when describing a wrapped image to <see cref="VkSkiaSurfaceFactory.CreateTextureState"/> or
    /// driving <see cref="VkImageLayoutTransitioner"/>. Named here so adapters do not each redeclare
    /// the same magic numbers; Core.VK itself still speaks raw <c>uint</c>s (see
    /// <see cref="VulkanNative"/> for why there is no Vulkan binding on either side).
    /// </summary>
    public static class VkConstants
    {
        public const uint ImageLayoutUndefined = 0;
        public const uint ImageLayoutGeneral = 1;
        public const uint ImageLayoutColorAttachmentOptimal = 2;
        public const uint ImageLayoutShaderReadOnlyOptimal = 5;
        public const uint ImageLayoutTransferSrcOptimal = 6;
        public const uint ImageLayoutTransferDstOptimal = 7;

        public const uint PipelineStageTopOfPipe = 0x1;
        public const uint PipelineStageFragmentShader = 0x80;
        public const uint PipelineStageColorAttachmentOutput = 0x400;
        public const uint PipelineStageTransfer = 0x1000;
        public const uint PipelineStageBottomOfPipe = 0x2000;

        public const uint AccessShaderRead = 0x20;
        public const uint AccessColorAttachmentWrite = 0x100;
        public const uint AccessTransferRead = 0x800;
        public const uint AccessTransferWrite = 0x1000;

        public const uint ImageAspectColor = 0x1;

        public const uint ImageUsageTransferSrc = 0x1;
        public const uint ImageUsageTransferDst = 0x2;
        public const uint ImageUsageSampled = 0x4;
        public const uint ImageUsageColorAttachment = 0x10;

        public const uint QueueGraphics = 0x1;
        public const uint QueueCompute = 0x2;
        public const uint QueueTransfer = 0x4;

        public const uint QueueFamilyIgnored = 0xFFFFFFFF;

        /// <summary><c>VK_MAKE_API_VERSION(0, major, minor, 0)</c>.</summary>
        public static uint MakeApiVersion(uint major, uint minor) => (major << 22) | (minor << 12);
    }
}
