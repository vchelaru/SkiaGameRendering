using SkiaGameRendering.Core.VK;
using Xunit;
using static Tests.CoreVK.VulkanTestNative;

namespace Tests.CoreVK;

/// <summary>
/// <see cref="VkImageAllocator"/> on the headless test device (a real GPU on a dev box, Mesa lavapipe
/// on CI). It is what the MonoGame DesktopVK backend uses to make the image it shares with MonoGame,
/// so the image must come back usable the way Skia requires: bound to memory, with the transfer bits.
/// </summary>
public sealed class VkImageAllocatorTests
{
    [Fact]
    public void Create_ReturnsBoundImageSkiaCanWrap_AndLeavesItTransitionable()
    {
        using var vk = new VulkanTestDevice();

        var allocation = VkImageAllocator.Create(
            vk.Instance, vk.PhysicalDevice, vk.Device, 64, 32, VK_FORMAT_R8G8B8A8_UNORM);
        try
        {
            Assert.NotEqual(0UL, allocation.Image);
            Assert.NotEqual(0UL, allocation.Memory);
            Assert.Equal(VkImageAllocator.ImageUsageFlags, allocation.UsageFlags);

            // CreateTextureState rejects an image missing either transfer bit, so this fails if the allocator's flags regress.
            using var factory = new VkSkiaSurfaceFactory();
            factory.InitializeFromNative(
                vk.Instance, vk.PhysicalDevice, vk.Device, vk.Queue, vk.GraphicsQueueFamilyIndex, vk.ApiVersion, [], []);
            var state = factory.CreateTextureState(
                allocation.Image, allocation.Format, VkConstants.ImageLayoutUndefined, allocation.UsageFlags, imageTiling: 0);
            Assert.Equal(allocation.Image, state.VkImage);

            using var transitioner = new VkImageLayoutTransitioner(vk.Device, vk.Queue, vk.GraphicsQueueFamilyIndex);
            transitioner.Transition(
                allocation.Image,
                oldLayout: VkConstants.ImageLayoutUndefined,
                newLayout: VkConstants.ImageLayoutShaderReadOnlyOptimal,
                srcStageMask: VkConstants.PipelineStageTopOfPipe,
                srcAccessMask: 0,
                dstStageMask: VkConstants.PipelineStageFragmentShader,
                dstAccessMask: VkConstants.AccessShaderRead);
            transitioner.WaitForCompletion();
        }
        finally
        {
            VkImageAllocator.Destroy(vk.Device, allocation);
        }
    }

    [Fact]
    public void Create_ZeroSize_Throws()
    {
        using var vk = new VulkanTestDevice();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VkImageAllocator.Create(vk.Instance, vk.PhysicalDevice, vk.Device, 0, 32, VK_FORMAT_R8G8B8A8_UNORM));
    }
}
