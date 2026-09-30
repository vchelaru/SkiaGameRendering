using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using static Tests.CoreVK.VulkanTestNative;

namespace Tests.CoreVK;

/// <summary>
/// Draws <see cref="GoldenScene"/> into one host-owned <c>VkImage</c> for hundreds of frames (lavapipe
/// in CI) and fails if the process grows per frame (see <see cref="FrameLeakCheck"/>). Covers both
/// frame shapes the adapters use: one long-lived surface flushed synchronously (Stride), and a
/// surface rewrapped every frame, flushed without waiting and handed back through
/// <see cref="VkImageLayoutTransitioner"/> (Godot).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed unsafe class VkPerFrameLeakTests(ITestOutputHelper output)
{
    const uint Usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;

    [Fact]
    public void LongLivedSurface_GrowsNothing_ThroughRealVulkanDevice() =>
        WithImage((vk, factory, image) =>
        {
            var state = factory.CreateTextureState((ulong)image, VK_FORMAT_R8G8B8A8_UNORM, VK_IMAGE_LAYOUT_UNDEFINED, Usage, VK_IMAGE_TILING_OPTIMAL);
            factory.BeginDraw();
            var (surface, renderTarget) = factory.CreateSurface(state, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888);
            factory.EndDraw();
            try
            {
                FrameLeakCheck.AssertNoPerFrameGrowth(() =>
                {
                    factory.BeginDraw();
                    GoldenScene.Draw(surface.Canvas);
                    surface.Flush();
                    factory.EndDraw();
                }, output);
            }
            finally
            {
                surface.Dispose();
                renderTarget.Dispose();
            }
        });

    [Fact]
    public void SurfaceRewrappedEachFrame_WithTransitions_GrowsNothing_ThroughRealVulkanDevice() =>
        WithImage((vk, factory, image) =>
        {
            const uint shaderReadOnly = 5; // VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
            using var transitioner = new VkImageLayoutTransitioner(vk.Device, vk.Queue, vk.GraphicsQueueFamilyIndex);
            // Starts the image where every later frame leaves it.
            transitioner.Transition((ulong)image, VK_IMAGE_LAYOUT_UNDEFINED, shaderReadOnly,
                VkConstants.PipelineStageColorAttachmentOutput, 0, VkConstants.PipelineStageFragmentShader, VkConstants.AccessShaderRead);

            SKSurface? surface = null;
            GRBackendRenderTarget? renderTarget = null;
            int frame = 0;
            try
            {
                FrameLeakCheck.AssertNoPerFrameGrowth(() =>
                {
                    factory.BeginDraw();
                    surface?.Dispose();
                    renderTarget?.Dispose();
                    var state = factory.CreateTextureState((ulong)image, VK_FORMAT_R8G8B8A8_UNORM, shaderReadOnly, Usage, VK_IMAGE_TILING_OPTIMAL);
                    (surface, renderTarget) = factory.CreateSurface(state, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888);
                    GoldenScene.Draw(surface.Canvas);
                    surface.Flush();
                    factory.EndDraw(synchronous: false);
                    transitioner.Transition((ulong)image, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, shaderReadOnly,
                        VkConstants.PipelineStageColorAttachmentOutput, VkConstants.AccessColorAttachmentWrite,
                        VkConstants.PipelineStageFragmentShader, VkConstants.AccessShaderRead);
                    // An engine never runs more than a couple of frames ahead of the GPU; without
                    // this the CPU would queue unboundedly many, which looks like growth but is not.
                    if (++frame % 2 == 0)
                        transitioner.WaitForCompletion();
                }, output);
                output.WriteLine($"Transition ring: {transitioner.SlotCount} slots.");
            }
            finally
            {
                transitioner.WaitForCompletion();
                factory.BeginDraw();
                surface?.Dispose();
                renderTarget?.Dispose();
                factory.EndDraw();
            }
        });

    /// <summary>Allocates a device-local, render-target-capable image and a factory for it, then runs <paramref name="body"/>.</summary>
    void WithImage(Action<VulkanTestDevice, VkSkiaSurfaceFactory, IntPtr> body)
    {
        using var vk = new VulkanTestDevice();
        var image = IntPtr.Zero;
        var imageMemory = IntPtr.Zero;
        try
        {
            var imageCreateInfo = new VkImageCreateInfo
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
                imageType = VK_IMAGE_TYPE_2D,
                format = VK_FORMAT_R8G8B8A8_UNORM,
                extent = new VkExtent3D { width = GoldenScene.Width, height = GoldenScene.Height, depth = 1 },
                mipLevels = 1,
                arrayLayers = 1,
                samples = VK_SAMPLE_COUNT_1_BIT,
                tiling = VK_IMAGE_TILING_OPTIMAL,
                usage = Usage,
                sharingMode = VK_SHARING_MODE_EXCLUSIVE,
                initialLayout = VK_IMAGE_LAYOUT_UNDEFINED,
            };
            int hr = vkCreateImage(vk.Device, &imageCreateInfo, IntPtr.Zero, out image);
            Assert.True(hr == VK_SUCCESS, $"vkCreateImage failed. VkResult: {hr}");

            vkGetImageMemoryRequirements(vk.Device, image, out var requirements);
            var allocateInfo = new VkMemoryAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = requirements.size,
                memoryTypeIndex = vk.FindMemoryTypeIndex(requirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT),
            };
            hr = vkAllocateMemory(vk.Device, &allocateInfo, IntPtr.Zero, out imageMemory);
            Assert.True(hr == VK_SUCCESS, $"vkAllocateMemory failed. VkResult: {hr}");
            hr = vkBindImageMemory(vk.Device, image, imageMemory, 0);
            Assert.True(hr == VK_SUCCESS, $"vkBindImageMemory failed. VkResult: {hr}");

            using var factory = new VkSkiaSurfaceFactory();
            factory.InitializeFromNative(
                vk.Instance, vk.PhysicalDevice, vk.Device, vk.Queue,
                vk.GraphicsQueueFamilyIndex, vk.ApiVersion,
                instanceExtensions: [], deviceExtensions: []);

            body(vk, factory, image);
            vkDeviceWaitIdle(vk.Device);
        }
        finally
        {
            if (imageMemory != IntPtr.Zero)
                vkFreeMemory(vk.Device, imageMemory, IntPtr.Zero);
            if (image != IntPtr.Zero)
                vkDestroyImage(vk.Device, image, IntPtr.Zero);
        }
    }
}
