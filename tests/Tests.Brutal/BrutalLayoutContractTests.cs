using Brutal.VulkanApi;
using SkiaGameRendering;
using SkiaSharp;
using Xunit;

namespace Tests.Brutal;

/// <summary>Creates a BRUTAL Vulkan device with one graphics queue and a <see cref="SkiaVulkanContext"/> on it.</summary>
sealed unsafe class BrutalDevice : IDisposable
{
    readonly Instance _instance;
    readonly Device _device;
    public readonly PhysicalDevice PhysicalDevice;
    public readonly Queue Queue;
    public readonly SkiaVulkanContext Context;

    public BrutalDevice()
    {
        _instance = Vulkan.CreateInstance(new Instance.CreateInfo
        {
            AppInfo = new VkApplicationInfo { ApiVersion = 1 << 22 | 3 << 12 },
            EnabledExtensions = [],
            EnabledLayers = [],
        });
        PhysicalDevice = _instance.PhysicalDevices[0];
        var family = PhysicalDevice.GetFirstQueueFamilyWithFlag(VkQueueFlags.GraphicsBit);
        var priority = 1.0f;
        _device = PhysicalDevice.CreateDevice(new Device.CreateInfo
        {
            Extensions = [],
            QueueCreateInfos = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = family, QueueCount = 1, QueuePriorities = &priority }],
        });
        Queue = _device.GetQueue(family, 0);
        Context = new SkiaVulkanContext(_instance.VkHandle, PhysicalDevice.VkHandle, _device.VkHandle, Queue.VkHandle, (uint)family);
    }

    public byte[] ReadPixel(SkiaRenderTarget2D target, int x, int y) =>
        PixelReadback.ReadPixel(_instance.VkHandle, PhysicalDevice.VkHandle, _device.VkHandle, Queue.VkHandle, (uint)Queue.Family, target.VkImage, x, y);

    /// <summary>Whole-image readback (test-only). The image is in COLOR_ATTACHMENT_OPTIMAL when the app manages layout, else SHADER_READ_ONLY_OPTIMAL.</summary>
    public byte[] ReadImage(SkiaRenderTarget2D target, bool appManagesLayout) =>
        PixelReadback.ReadRegion(_instance.VkHandle, PhysicalDevice.VkHandle, _device.VkHandle, Queue.VkHandle, (uint)Queue.Family,
            target.VkImage, 0, 0, target.Width, target.Height, appManagesLayout ? 2u : 5u);

    public void Dispose()
    {
        _device.WaitIdle();
        Context.Dispose();
        _device.Dispose();
        _instance.Dispose();
    }
}

public sealed class BrutalLayoutContractTests
{
    [Fact]
    public void ImageIsSampleableAfterEnd_WithoutAppBarrier()
    {
        using var vk = new BrutalDevice();
        using var target = new SkiaRenderTarget2D(vk.Context, 16, 16);
        target.Begin();
        target.Canvas.Clear(new SKColor(255, 0, 0));
        target.End(synchronous: true);

        // PixelReadback assumes SHADER_READ_ONLY_OPTIMAL as the old layout: the contract under test.
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, vk.ReadPixel(target, 8, 8));
    }

    [Fact]
    public void SecondFrameDrawsAfterFirstFrameHandback()
    {
        using var vk = new BrutalDevice();
        using var target = new SkiaRenderTarget2D(vk.Context, 16, 16);
        target.Begin();
        target.Canvas.Clear(SKColors.Green);
        target.End();
        target.Begin();
        target.Canvas.Clear(new SKColor(0, 0, 255));
        target.End(synchronous: true);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, vk.ReadPixel(target, 3, 3));
    }

    [Fact]
    public void BeginWithoutClear_KeepsPreviousContents()
    {
        using var vk = new BrutalDevice();
        using var target = new SkiaRenderTarget2D(vk.Context, 16, 16);
        target.Begin();
        target.Canvas.Clear(new SKColor(0, 255, 0));
        target.End();
        target.Begin(clear: false);
        target.End(synchronous: true);

        Assert.Equal(new byte[] { 0, 255, 0, 255 }, vk.ReadPixel(target, 5, 5));
    }

    [Fact]
    public void BgraTarget_ReportsBgraFormatAndSwapsChannelsInMemory()
    {
        using var vk = new BrutalDevice();
        using var target = new SkiaRenderTarget2D(vk.Context, 16, 16, SKColorType.Bgra8888);
        Assert.Equal(44u, target.VkFormat);
        target.Begin();
        target.Canvas.Clear(new SKColor(255, 0, 0));
        target.End(synchronous: true);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, vk.ReadPixel(target, 1, 1));
    }

    [Fact]
    public void MisuseThrows()
    {
        using var vk = new BrutalDevice();
        using var target = new SkiaRenderTarget2D(vk.Context, 16, 16);
        Assert.Throws<InvalidOperationException>(() => target.Canvas);
        Assert.Throws<InvalidOperationException>(() => target.End());
        target.Begin();
        Assert.Throws<InvalidOperationException>(() => target.Begin());
        Assert.Throws<InvalidOperationException>(() => target.Dispose());
        target.End();
    }

    [Fact]
    public void UnsupportedColorTypeThrowsNotSupported()
    {
        using var vk = new BrutalDevice();
        Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(vk.Context, 16, 16, SKColorType.Rgba1010102));
    }
}
