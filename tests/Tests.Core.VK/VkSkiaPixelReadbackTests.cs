using System.Runtime.InteropServices;
using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using static Tests.CoreVK.VulkanTestNative;

namespace Tests.CoreVK;

/// <summary>
/// Draws through the full <c>Core.VK</c> pipeline - a real headless Vulkan instance/device/queue (see
/// <see cref="VulkanTestDevice"/>), <c>VkSkiaSurfaceFactory</c>'s texture wrapping and queue-lock
/// bracketing, Skia's Vulkan backend - into a real, host-allocated <c>VkImage</c>, then reads the
/// image back to CPU via a staging-buffer copy and checks the pixels. No window is ever shown and no
/// engine is involved: this plays the "host engine" role itself, the same shape
/// <c>AngleSkiaPixelReadbackTests</c> (WARP) and <c>WglSkiaPixelReadbackTests</c> (WGL + Mesa
/// llvmpipe) play for the other two Core.* backends - see the headless-gpu-testing skill.
/// <para>
/// This is the piece nothing else can cover: proof that Skia's Vulkan draw calls actually land in a
/// real, externally-owned <c>VkImage</c> reachable only through <see cref="VkSkiaSurfaceFactory"/>'s
/// raw-<see cref="IntPtr"/> entry point, not just that the P/Invoke declarations compile. On a dev box
/// with a real GPU this runs against the real driver; the CI-relevant path (no GPU) is Mesa lavapipe,
/// registered via the Windows registry (not <c>VK_ICD_FILENAMES</c>, which the Vulkan loader ignores
/// for elevated processes like GitHub's runner) - see <c>master.yml</c> and the headless-gpu-testing
/// skill.
/// </para>
/// <para>
/// The post-draw layout transition below (<c>VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL</c> -&gt;
/// <c>VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL</c>) is exactly the ASSUMED layout documented on
/// <see cref="VkSkiaSurfaceFactory.EndDraw"/> - this test is also, incidentally, the closest thing to
/// empirical evidence for that assumption this repo has: an incorrect <c>oldLayout</c> here can
/// produce corrupted pixels on a real GPU driver (unlike a software rasterizer, which mostly ignores
/// tiling), so a passing <see cref="Clear_WritesExpectedColor_ThroughRealVulkanDevice"/> on a real GPU
/// dev box is meaningful signal, not just a rubber stamp - though it is still not a substitute for
/// Vulkan validation layers, which this test does not enable.
/// </para>
/// </summary>
public sealed unsafe class VkSkiaPixelReadbackTests
{
    readonly ITestOutputHelper _output;

    public VkSkiaPixelReadbackTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Clear_WritesExpectedColor_ThroughRealVulkanDevice()
    {
        var expected = new SKColor(10, 20, 30, 255);

        var pixels = RenderAndReadBack(4, 4, canvas => canvas.Clear(expected));

        Assert.Equal([expected.Red, expected.Green, expected.Blue, expected.Alpha], pixels[..4]);
    }

    /// <summary>
    /// The same pipeline as above, but drawing <see cref="GoldenScene"/> - antialiased shapes, a
    /// gradient, overlapping translucent fills - and comparing the whole image against a checked-in
    /// reference instead of sampling one pixel. A solid-color clear can't tell a working Skia GPU
    /// pipeline from one that silently lost its blending or its antialiasing; this can.
    /// <para>
    /// Nothing here checks graphics state was handed back the way the ANGLE and OpenGL backends do.
    /// Vulkan has no persistent bound state to leak: everything is recorded into command buffers the
    /// host owns, so there is no equivalent promise for a test to hold this backend to.
    /// </para>
    /// </summary>
    [Fact]
    public void Scene_MatchesGolden_ThroughRealVulkanDevice()
    {
        var pixels = RenderAndReadBack(GoldenScene.Width, GoldenScene.Height, GoldenScene.Draw);

        GoldenImage.AssertSceneOrientation(pixels);
        if (GoldenImage.PinnedRasterizerInUse)
            GoldenImage.AssertMatchesGolden(pixels, "core-vk-scene.png");
        else
            _output.WriteLine("Golden comparison skipped: the golden was rendered by CI's pinned Mesa " +
                "lavapipe build and this run used whichever Vulkan driver this machine has. The render " +
                "itself still ran and was checked for orientation.");
    }

    /// <summary>
    /// Wraps a host-allocated <c>VkImage</c> with <see cref="VkSkiaSurfaceFactory"/>, runs
    /// <paramref name="draw"/> on the resulting canvas, and copies the result back to CPU as tightly
    /// packed RGBA8888. Shared by both tests above so the Vulkan scaffolding - image, staging buffer,
    /// command pool, layout barrier, fence - is written once.
    /// </summary>
    byte[] RenderAndReadBack(int width, int height, Action<SKCanvas> draw)
    {
        using var vk = new VulkanTestDevice();
        _output.WriteLine($"Vulkan graphics queue family index: {vk.GraphicsQueueFamilyIndex}");

        var image = IntPtr.Zero;
        var imageMemory = IntPtr.Zero;
        var stagingBuffer = IntPtr.Zero;
        var stagingMemory = IntPtr.Zero;
        var commandPool = IntPtr.Zero;
        var commandBuffer = IntPtr.Zero;
        var fence = IntPtr.Zero;

        try
        {
            // Skia's Vulkan backend (GrVkGpu.cpp's check_image_info) unconditionally requires BOTH
            // transfer bits on any wrapped image, regardless of whether the host itself needs them -
            // see the doc comment on VkSkiaSurfaceFactory.CreateTextureState's imageUsageFlags param.
            const uint usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            var imageCreateInfo = new VkImageCreateInfo
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
                imageType = VK_IMAGE_TYPE_2D,
                format = VK_FORMAT_R8G8B8A8_UNORM,
                extent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
                mipLevels = 1,
                arrayLayers = 1,
                samples = VK_SAMPLE_COUNT_1_BIT,
                tiling = VK_IMAGE_TILING_OPTIMAL,
                usage = usage,
                sharingMode = VK_SHARING_MODE_EXCLUSIVE,
                initialLayout = VK_IMAGE_LAYOUT_UNDEFINED,
            };
            int hr = vkCreateImage(vk.Device, &imageCreateInfo, IntPtr.Zero, out image);
            Assert.True(hr == VK_SUCCESS, $"vkCreateImage failed. VkResult: {hr}");

            vkGetImageMemoryRequirements(vk.Device, image, out var imageMemReqs);
            var imageMemTypeIndex = vk.FindMemoryTypeIndex(imageMemReqs.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
            var imageAllocInfo = new VkMemoryAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = imageMemReqs.size,
                memoryTypeIndex = imageMemTypeIndex,
            };
            hr = vkAllocateMemory(vk.Device, &imageAllocInfo, IntPtr.Zero, out imageMemory);
            Assert.True(hr == VK_SUCCESS, $"vkAllocateMemory (image) failed. VkResult: {hr}");
            hr = vkBindImageMemory(vk.Device, image, imageMemory, 0);
            Assert.True(hr == VK_SUCCESS, $"vkBindImageMemory failed. VkResult: {hr}");

            // --- The actual Core.VK round trip: wrap the host-owned VkImage, draw with Skia. ---
            using var factory = new VkSkiaSurfaceFactory();
            factory.InitializeFromNative(
                vk.Instance, vk.PhysicalDevice, vk.Device, vk.Queue,
                vk.GraphicsQueueFamilyIndex, vk.ApiVersion,
                instanceExtensions: [], deviceExtensions: []);

            var state = factory.CreateTextureState(
                vkImage: (ulong)image, format: VK_FORMAT_R8G8B8A8_UNORM,
                imageLayout: VK_IMAGE_LAYOUT_UNDEFINED, imageUsageFlags: usage,
                imageTiling: VK_IMAGE_TILING_OPTIMAL);

            factory.BeginDraw();
            var (surface, renderTarget) = factory.CreateSurface(state, width, height, SKColorType.Rgba8888);
            draw(surface.Canvas);
            surface.Flush();
            factory.EndDraw();
            renderTarget.Dispose();
            surface.Dispose();

            // --- Read the drawn image back to CPU via a staging buffer copy. ---
            const int pixelBytes = 4;
            ulong bufferSize = (ulong)(width * height * pixelBytes);
            var bufferCreateInfo = new VkBufferCreateInfo
            {
                sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                size = bufferSize,
                usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT,
                sharingMode = VK_SHARING_MODE_EXCLUSIVE,
            };
            hr = vkCreateBuffer(vk.Device, &bufferCreateInfo, IntPtr.Zero, out stagingBuffer);
            Assert.True(hr == VK_SUCCESS, $"vkCreateBuffer (staging) failed. VkResult: {hr}");

            vkGetBufferMemoryRequirements(vk.Device, stagingBuffer, out var bufferMemReqs);
            var stagingMemTypeIndex = vk.FindMemoryTypeIndex(
                bufferMemReqs.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
            var stagingAllocInfo = new VkMemoryAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = bufferMemReqs.size,
                memoryTypeIndex = stagingMemTypeIndex,
            };
            hr = vkAllocateMemory(vk.Device, &stagingAllocInfo, IntPtr.Zero, out stagingMemory);
            Assert.True(hr == VK_SUCCESS, $"vkAllocateMemory (staging) failed. VkResult: {hr}");
            hr = vkBindBufferMemory(vk.Device, stagingBuffer, stagingMemory, 0);
            Assert.True(hr == VK_SUCCESS, $"vkBindBufferMemory failed. VkResult: {hr}");

            var poolCreateInfo = new VkCommandPoolCreateInfo
            {
                sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
                queueFamilyIndex = vk.GraphicsQueueFamilyIndex,
            };
            hr = vkCreateCommandPool(vk.Device, &poolCreateInfo, IntPtr.Zero, out commandPool);
            Assert.True(hr == VK_SUCCESS, $"vkCreateCommandPool failed. VkResult: {hr}");

            var cmdAllocInfo = new VkCommandBufferAllocateInfo
            {
                sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
                commandPool = commandPool,
                level = VK_COMMAND_BUFFER_LEVEL_PRIMARY,
                commandBufferCount = 1,
            };
            hr = vkAllocateCommandBuffers(vk.Device, &cmdAllocInfo, out commandBuffer);
            Assert.True(hr == VK_SUCCESS, $"vkAllocateCommandBuffers failed. VkResult: {hr}");

            var beginInfo = new VkCommandBufferBeginInfo
            {
                sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
                flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,
            };
            hr = vkBeginCommandBuffer(commandBuffer, &beginInfo);
            Assert.True(hr == VK_SUCCESS, $"vkBeginCommandBuffer failed. VkResult: {hr}");

            // See VkSkiaSurfaceFactory.EndDraw's doc comment: SkiaSharp 3.119.4 gives no way to query
            // or steer the image's post-draw layout, so VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL is
            // the documented ASSUMPTION for a color-attachment-usage image after a Skia draw, not a
            // value read back from anywhere.
            var barrier = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT,
                dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT,
                oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                image = image,
                subresourceRange = new VkImageSubresourceRange
                {
                    aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    baseMipLevel = 0,
                    levelCount = 1,
                    baseArrayLayer = 0,
                    layerCount = 1,
                },
            };
            vkCmdPipelineBarrier(
                commandBuffer,
                VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT, 0,
                0, IntPtr.Zero, 0, IntPtr.Zero, 1, &barrier);

            // bufferRowLength and bufferImageHeight of 0 mean "tightly packed to imageExtent", so the
            // staging buffer comes back with no row padding to unwind on the CPU side.
            var copyRegion = new VkBufferImageCopy
            {
                bufferOffset = 0,
                bufferRowLength = 0,
                bufferImageHeight = 0,
                imageSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    mipLevel = 0,
                    baseArrayLayer = 0,
                    layerCount = 1,
                },
                imageOffset = new VkOffset3D { x = 0, y = 0, z = 0 },
                imageExtent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
            };
            vkCmdCopyImageToBuffer(commandBuffer, image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, stagingBuffer, 1, &copyRegion);

            hr = vkEndCommandBuffer(commandBuffer);
            Assert.True(hr == VK_SUCCESS, $"vkEndCommandBuffer failed. VkResult: {hr}");

            var fenceCreateInfo = new VkFenceCreateInfo { sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO };
            hr = vkCreateFence(vk.Device, &fenceCreateInfo, IntPtr.Zero, out fence);
            Assert.True(hr == VK_SUCCESS, $"vkCreateFence failed. VkResult: {hr}");

            var localCommandBuffer = commandBuffer;
            var submitInfo = new VkSubmitInfo
            {
                sType = VK_STRUCTURE_TYPE_SUBMIT_INFO,
                commandBufferCount = 1,
                pCommandBuffers = (IntPtr)(&localCommandBuffer),
            };
            hr = vkQueueSubmit(vk.Queue, 1, &submitInfo, fence);
            Assert.True(hr == VK_SUCCESS, $"vkQueueSubmit (readback copy) failed. VkResult: {hr}");

            var fenceHandle = fence;
            hr = vkWaitForFences(vk.Device, 1, ref fenceHandle, true, UINT64_MAX);
            Assert.True(hr == VK_SUCCESS, $"vkWaitForFences failed. VkResult: {hr}");

            hr = vkMapMemory(vk.Device, stagingMemory, 0, bufferSize, 0, out var mapped);
            Assert.True(hr == VK_SUCCESS, $"vkMapMemory failed. VkResult: {hr}");
            var pixels = new byte[width * height * pixelBytes];
            Marshal.Copy(mapped, pixels, 0, pixels.Length);
            vkUnmapMemory(vk.Device, stagingMemory);

            return pixels;
        }
        finally
        {
            if (fence != IntPtr.Zero)
                vkDestroyFence(vk.Device, fence, IntPtr.Zero);
            if (commandBuffer != IntPtr.Zero)
            {
                var localCommandBuffer = commandBuffer;
                vkFreeCommandBuffers(vk.Device, commandPool, 1, ref localCommandBuffer);
            }
            if (commandPool != IntPtr.Zero)
                vkDestroyCommandPool(vk.Device, commandPool, IntPtr.Zero);
            if (stagingMemory != IntPtr.Zero)
                vkFreeMemory(vk.Device, stagingMemory, IntPtr.Zero);
            if (stagingBuffer != IntPtr.Zero)
                vkDestroyBuffer(vk.Device, stagingBuffer, IntPtr.Zero);
            if (imageMemory != IntPtr.Zero)
                vkFreeMemory(vk.Device, imageMemory, IntPtr.Zero);
            if (image != IntPtr.Zero)
                vkDestroyImage(vk.Device, image, IntPtr.Zero);
        }
    }
}
