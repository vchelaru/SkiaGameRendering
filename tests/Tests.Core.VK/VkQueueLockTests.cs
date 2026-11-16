using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Xunit;
using static Tests.CoreVK.VulkanTestNative;

namespace Tests.CoreVK;

/// <summary>
/// The host's queue lock covers Skia's submit and nothing else: a host thread sharing the queue (a texture
/// upload worker) must not wait while a frame is built on the canvas.
/// </summary>
public sealed class VkQueueLockTests
{
    [Fact]
    public void Lock_IsHeldForEndDrawsFlushAndDispose_NotWhileDrawing()
    {
        using var vk = new VulkanTestDevice();
        var allocation = VkImageAllocator.Create(vk.Instance, vk.PhysicalDevice, vk.Device, 16, 16, VK_FORMAT_R8G8B8A8_UNORM);
        var events = new List<string>();
        try
        {
            var factory = new VkSkiaSurfaceFactory();
            factory.InitializeFromNative(
                vk.Instance, vk.PhysicalDevice, vk.Device, vk.Queue, vk.GraphicsQueueFamilyIndex, vk.ApiVersion, [], [],
                acquireQueueLock: () =>
                {
                    events.Add("acquire");
                    return new Release(() => events.Add("release"));
                });
            var state = factory.CreateTextureState(
                allocation.Image, allocation.Format, VkConstants.ImageLayoutUndefined, allocation.UsageFlags, imageTiling: 0);

            factory.BeginDraw();
            events.Add("begin");
            var (surface, renderTarget) = factory.CreateSurface(state, 16, 16, SKColorType.Rgba8888);
            surface.Canvas.Clear(SKColors.Red);
            using (var bitmap = new SKBitmap(4, 4))
            {
                // A raster image drawn on a GPU canvas is uploaded as a texture.
                bitmap.Erase(SKColors.Blue);
                using var image = SKImage.FromBitmap(bitmap);
                surface.Canvas.DrawImage(image, 2, 2, SKSamplingOptions.Default);
            }
            using (surface.Snapshot()) { }
            events.Add("drawn");
            factory.EndDraw(synchronous: true);
            events.Add("ended");

            surface.Dispose();
            renderTarget.Dispose();
            factory.Dispose();
            events.Add("disposed");

            Assert.Equal(["begin", "drawn", "acquire", "release", "ended", "acquire", "release", "disposed"], events);
        }
        finally
        {
            vkDeviceWaitIdle(vk.Device);
            VkImageAllocator.Destroy(vk.Device, allocation);
        }
    }

    sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
