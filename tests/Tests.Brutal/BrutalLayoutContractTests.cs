using Brutal.VulkanApi;
using SkiaGameRendering;
using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Tests.Shared;
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

    /// <summary>An app-owned image (what a swapchain image stands in for): UNDEFINED layout, COLOR_ATTACHMENT | TRANSFER_SRC | TRANSFER_DST | SAMPLED.</summary>
    public VkImageAllocation CreateAppImage(int width, int height, uint format, bool mutableFormat = false) =>
        VkImageAllocator.Create(_instance.VkHandle, PhysicalDevice.VkHandle, _device.VkHandle, width, height, format, mutableFormat);

    public void Destroy(VkImageAllocation image)
    {
        _device.WaitIdle();
        VkImageAllocator.Destroy(_device.VkHandle, image);
    }

    /// <summary>Whole-image readback (test-only) of an app-owned image in <paramref name="layout"/> (5 = SHADER_READ_ONLY_OPTIMAL, 2 = COLOR_ATTACHMENT_OPTIMAL).</summary>
    public byte[] ReadAppImage(VkImageAllocation image, int width, int height, uint layout = 5) =>
        PixelReadback.ReadRegion(_instance.VkHandle, PhysicalDevice.VkHandle, _device.VkHandle, Queue.VkHandle, (uint)Queue.Family,
            image.Image, 0, 0, width, height, layout);

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

    const uint Rgba8 = 37, Rgba8Srgb = 43, Bgra8 = 44, Undefined = 0, ColorAttachment = 2, ShaderRead = 5;

    [Fact]
    public void DirectCanvas_DrawsIntoAppImage_AndHandsBackTheNamedLayout()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            direct.Begin(image.Image, Rgba8, 16, 16, Undefined, clear: true);
            direct.Canvas.Clear(new SKColor(255, 0, 0));
            direct.End(finalLayout: ShaderRead, synchronous: true);

            // PixelReadback assumes SHADER_READ_ONLY_OPTIMAL as the old layout: the contract under test.
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, vk.ReadAppImage(image, 16, 16)[..4]);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void DirectCanvas_DefaultFinalLayoutIsTheOneItCameIn_AndDrawsOverPreviousContents()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            direct.Begin(image.Image, Rgba8, 16, 16, Undefined);
            direct.Canvas.Clear(new SKColor(0, 255, 0));
            direct.End(); // came in UNDEFINED: handed back as COLOR_ATTACHMENT_OPTIMAL

            direct.Begin(image.Image, Rgba8, 16, 16, ColorAttachment);
            using (var paint = new SKPaint { Color = new SKColor(0, 0, 255) })
                direct.Canvas.DrawRect(0, 0, 8, 8, paint);
            direct.End(synchronous: true); // came in COLOR_ATTACHMENT_OPTIMAL: goes back there

            var rgba = vk.ReadAppImage(image, 16, 16, ColorAttachment);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, rgba[..4]);
            var far = ((12 * 16) + 12) * 4;
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, rgba[far..(far + 4)]);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void DirectCanvas_SrgbImage_IsWrittenWithoutEncoding()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8Srgb, mutableFormat: true); // without MUTABLE_FORMAT the validation layer flags the UNORM wrap
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            direct.Begin(image.Image, Rgba8Srgb, 16, 16, Undefined);
            direct.Canvas.Clear(new SKColor(200, 100, 50));
            direct.End(finalLayout: ShaderRead, synchronous: true);

            Assert.Equal(new byte[] { 200, 100, 50, 255 }, vk.ReadAppImage(image, 16, 16)[..4]);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void DirectCanvas_BgraImage_SwapsChannelsInMemory()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Bgra8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            direct.Begin(image.Image, Bgra8, 16, 16, Undefined);
            direct.Canvas.Clear(new SKColor(255, 0, 0));
            direct.End(finalLayout: ShaderRead, synchronous: true);

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, vk.ReadAppImage(image, 16, 16)[..4]);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void DirectCanvas_RefusesUnsupportedTargetsUpFront_AndStaysUsable()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            Assert.Throws<NotSupportedException>(() => direct.Begin(image.Image, Rgba8, 16, 16, Undefined, sampleCount: 4));
            Assert.Throws<NotSupportedException>(() => direct.Begin(image.Image, 64 /* R16G16B16A16_UNORM: not 8-bit */, 16, 16, Undefined));
            Assert.Throws<ArgumentException>(() => direct.Begin(image.Image, Rgba8, 16, 16, Undefined, imageUsage: 0x4 /* SAMPLED only */));
            Assert.Throws<ArgumentException>(() => direct.Begin(0, Rgba8, 16, 16, Undefined));
            Assert.Throws<InvalidOperationException>(() => direct.Canvas);

            // A refused Begin changed nothing: a normal pass still works.
            direct.Begin(image.Image, Rgba8, 16, 16, Undefined);
            Assert.Throws<ArgumentException>(() => direct.End(finalLayout: Undefined));
            direct.End(finalLayout: ShaderRead, synchronous: true);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void DirectCanvas_MisuseThrows()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            Assert.Throws<InvalidOperationException>(() => direct.End());
            direct.Begin(image.Image, Rgba8, 16, 16, Undefined);
            Assert.Throws<InvalidOperationException>(() => direct.Begin(image.Image, Rgba8, 16, 16, Undefined));
            Assert.Throws<InvalidOperationException>(() => direct.Dispose());
            direct.End(synchronous: true);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void DirectCanvas_Scene_MatchesTheTexturePath()
    {
        using var vk = new BrutalDevice();
        using var texture = new SkiaRenderTarget2D(vk.Context, GoldenScene.Width, GoldenScene.Height);
        texture.Begin();
        GoldenScene.Draw(texture.Canvas);
        texture.End(synchronous: true);
        var viaTexture = vk.ReadImage(texture, appManagesLayout: false);

        var image = vk.CreateAppImage(GoldenScene.Width, GoldenScene.Height, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            // Twice: the second pass starts from the layout the first one handed back.
            for (var pass = 0; pass < 2; pass++)
            {
                direct.Begin(image.Image, Rgba8, GoldenScene.Width, GoldenScene.Height, pass == 0 ? Undefined : ShaderRead, clear: true);
                GoldenScene.Draw(direct.Canvas);
                direct.End(finalLayout: ShaderRead, synchronous: pass == 1);
            }
            var viaDirect = vk.ReadAppImage(image, GoldenScene.Width, GoldenScene.Height);

            var differing = 0;
            for (var p = 0; p < GoldenScene.Width * GoldenScene.Height; p++)
                for (var c = 0; c < 4; c++)
                    if (Math.Abs(viaTexture[p * 4 + c] - viaDirect[p * 4 + c]) > 4)
                    {
                        differing++;
                        break;
                    }
            Assert.True(differing < GoldenScene.Width * GoldenScene.Height * 0.005, $"{differing} pixels differ between the direct canvas and the texture path.");
        }
        finally { vk.Destroy(image); }
    }
}
