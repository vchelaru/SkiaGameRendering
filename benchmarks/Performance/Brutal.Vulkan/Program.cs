// The performance suite on BRUTAL Vulkan: a GLFW window with a Vulkan swapchain, presenting without vsync (IMMEDIATE,
// else MAILBOX). Each frame draws the entry's scene into a window-sized SkiaRenderTarget2D (library-managed layout, as
// in samples/Sample.Brutal), blits it to the acquired swapchain image and presents. Same arguments and results file
// as the MonoGame and KNI apps (see PerfOptions).
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Performance;
using SkiaGameRendering;
using SkiaSharp;

var options = PerfOptions.Parse(args);
if (options.MultiSampleCount > 0)
{
    // TODO: pass options.MultiSampleCount to SkiaRenderTarget2D once BRUTAL Vulkan's takes a sample count.
    Console.Error.WriteLine("--msaa is not supported on BRUTAL Vulkan yet: its SkiaRenderTarget2D has no sample count.");
    return 2;
}

if (!Glfw.Init())
{
    Console.Error.WriteLine("glfwInit failed (no display?).");
    return 1;
}

Glfw.WindowHint(GlfwWindowHint.ClientApi, 0 /* GLFW_NO_API */);
Glfw.WindowHint(GlfwWindowHint.Resizable, 0);
var createInfo = new GlfwWindow.CreateInfo { Title = "BRUTAL Vulkan Skia", Size = new(PerfRunner.Width, PerfRunner.Height) };
using var window = Glfw.CreateWindow(in createInfo);

using var host = new BrutalVulkanHost(window, options.Antialias);
var runner = new PerfRunner("BRUTAL Vulkan Skia", options, host);
runner.Start(scene => SkiaEntries.For(scene, direct: false));
while (!runner.Finished && !window.ShouldClose)
{
    Glfw.PollEvents();
    runner.Frame();
    host.Present();
}
return 0;

sealed unsafe class BrutalVulkanHost : IPerfHost, IDisposable
{
    const int FramesInFlight = 2;
    const int W = PerfRunner.Width, H = PerfRunner.Height;

    readonly GlfwWindow _window;
    readonly Instance _instance;
    readonly PhysicalDevice _physicalDevice;
    readonly Device _device;
    readonly Queue _queue;
    readonly int _family;
    readonly VkSurfaceKHR _surface;
    readonly VkSwapchainKHR _swapchain;
    readonly VkImage[] _swapchainImages;
    readonly VkSemaphore[] _imageAvailable = new VkSemaphore[FramesInFlight];
    readonly VkSemaphore[] _renderDone;
    readonly VkFence[] _fences = new VkFence[FramesInFlight];
    readonly CommandBuffer[] _commandBuffers = new CommandBuffer[FramesInFlight];
    readonly VkCommandPool _commandPool;
    readonly SkiaVulkanContext _context;
    readonly SkiaRenderTarget2D _target;
    readonly SkiaSceneDrawer _drawer;
    readonly string _gpu;
    readonly VkImageSubresourceRange _colorRange = new() { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 };

    int _frame;
    int? _acquiredImage;

    public BrutalVulkanHost(GlfwWindow window, bool antialias)
    {
        _window = window;

        var instanceExtensions = new HashSet<string>();
        VulkanHelpers.AddPlatformRequiredExtensions(instanceExtensions);
        // VK_KHR_surface plus this platform's surface extension, as GLFW needs them.
        byte** required = Glfw.GetRequiredInstanceExtensions(out uint requiredCount);
        for (int i = 0; i < requiredCount; i++)
            instanceExtensions.Add(Marshal.PtrToStringUTF8((nint)required[i])!);
        _instance = Vulkan.CreateInstance(new Instance.CreateInfo
        {
            AppInfo = new VkApplicationInfo { ApiVersion = 1 << 22 | 3 << 12 },
            Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
            EnabledExtensions = instanceExtensions,
            EnabledLayers = [],
        });
        ulong surface;
        int result = Glfw.CreateWindowSurface(_instance.VkHandle, window, out surface);
        if (result != 0)
            throw new InvalidOperationException($"glfwCreateWindowSurface failed: {result}.");
        _surface = new VkSurfaceKHR((nint)surface);

        _physicalDevice = _instance.PhysicalDevices[0];
        _family = _physicalDevice.GetFirstQueueFamilyWithFlag(VkQueueFlags.GraphicsBit);
        if (!_physicalDevice.GetSurfaceSupportKHR(_family, _surface))
            throw new NotSupportedException("The graphics queue family cannot present to the window.");
        var properties = _physicalDevice.GetProperties();
        _gpu = Brutal.Strings.Utf8.GetString(properties.DeviceName);

        var deviceExtensions = new HashSet<string>();
        VulkanHelpers.AddSwapchainExtensions(deviceExtensions);
        var priority = 1.0f;
        _device = _physicalDevice.CreateDevice(new Device.CreateInfo
        {
            Extensions = deviceExtensions,
            QueueCreateInfos = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = _family, QueueCount = 1, QueuePriorities = &priority }],
        });
        _queue = _device.GetQueue(_family, 0);

        var capabilities = _physicalDevice.GetSurfaceCapabilitiesKHR(_surface);
        var modes = new VkPresentModeKHR[_physicalDevice.GetSurfacePresentModesKHR_Count(_surface)];
        _physicalDevice.GetSurfacePresentModesKHR_Write(_surface, modes);
        // vsync off, like the other apps: IMMEDIATE tears but never waits; MAILBOX never waits either.
        var presentMode = modes.Contains(VkPresentModeKHR.ImmediateKHR) ? VkPresentModeKHR.ImmediateKHR
            : modes.Contains(VkPresentModeKHR.MailboxKHR) ? VkPresentModeKHR.MailboxKHR
            : VkPresentModeKHR.FifoKHR;
        long imageCount = Math.Max(capabilities.MinImageCount, 2);
        if (capabilities.MaxImageCount != 0)
            imageCount = Math.Min(imageCount, capabilities.MaxImageCount);
        _swapchain = _device.CreateSwapchainKHR(new VkSwapchainCreateInfoKHR
        {
            Surface = _surface,
            MinImageCount = (int)imageCount,
            ImageFormat = VkFormat.B8G8R8A8UNorm,
            ImageColorSpace = VkColorSpaceKHR.SRGBNonlinearKHR,
            ImageExtent = new VkExtent2D(W, H),
            ImageArrayLayers = 1,
            ImageUsage = VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = VkSharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = VkCompositeAlphaFlagsKHR.OpaqueBitKHR,
            PresentMode = presentMode,
            Clipped = true,
        }, null);
        _swapchainImages = new VkImage[_device.GetSwapchainImagesKHR_Count(_swapchain)];
        _device.GetSwapchainImagesKHR_Write(_swapchain, _swapchainImages);

        _renderDone = new VkSemaphore[_swapchainImages.Length];
        for (int i = 0; i < _renderDone.Length; i++)
            _renderDone[i] = _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
        _commandPool = _device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = _family, Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit }, null);
        for (int i = 0; i < FramesInFlight; i++)
        {
            _imageAvailable[i] = _device.CreateSemaphore(new VkSemaphoreCreateInfo(), null);
            _fences[i] = _device.CreateFence(VkFenceCreateFlags.SignaledBit, null);
            _commandBuffers[i] = _device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = _commandPool, Level = VkCommandBufferLevel.Primary });
        }

        _context = new SkiaVulkanContext(_instance.VkHandle, _physicalDevice.VkHandle, _device.VkHandle, _queue.VkHandle, (uint)_family);
        _target = new SkiaRenderTarget2D(_context, W, H);
        _drawer = new SkiaSceneDrawer(antialias);
    }

    public void PrepareEntry(PerfEntry entry) { }

    public void DrawEntry(PerfEntry entry, float t)
    {
        int slot = _frame % FramesInFlight;
        _device.WaitForFence(_fences[slot], -1);

        // Skia draws into its own image; the library queues the layout moves around it on the same queue.
        _target.Begin(clear: false);
        try
        {
            // The blit copies rather than blends, so black here matches the other apps' black clear behind the composite.
            _target.Canvas.Clear(SKColors.Black);
            // Uploads need Skia's GPU context current, which it only is between Begin and End.
            _drawer.Prepare((GRContext)_target.Canvas.Context!, entry.Scene);
            _drawer.Draw(_target.Canvas, entry.Scene, t, SkiaEntries.VariantOf(entry));
        }
        finally
        {
            _target.End();
        }

        var acquired = _device.AcquireNextImageKHR(_swapchain, -1, _imageAvailable[slot], VkFence.NullHandle, out int imageIndex);
        if (acquired != VkResult.Success && acquired != VkResult.SuboptimalKHR)
            throw new InvalidOperationException($"vkAcquireNextImageKHR: {acquired}.");
        _device.ResetFences([_fences[slot]]);

        var cb = _commandBuffers[slot];
        var skiaImage = new VkImage((nint)_target.VkImage);
        var swapImage = _swapchainImages[imageIndex];
        cb.Reset();
        cb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        // Skia's image: SHADER_READ_ONLY (where End left it, visible to fragment-shader reads) to TRANSFER_SRC.
        Barrier(cb, skiaImage, VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.TransferSrcOptimal,
            VkPipelineStageFlags.FragmentShaderBit, VkPipelineStageFlags.TransferBit, VkAccessFlags.None, VkAccessFlags.TransferReadBit);
        Barrier(cb, swapImage, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
            VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.TransferBit, VkAccessFlags.None, VkAccessFlags.TransferWriteBit);
        var region = new VkImageBlit
        {
            SrcSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
            DstSubresource = new VkImageSubresourceLayers { AspectMask = VkImageAspectFlags.ColorBit, LayerCount = 1 },
        };
        region.SrcOffsets[1] = new VkOffset3D(W, H, 1);
        region.DstOffsets[1] = new VkOffset3D(W, H, 1);
        cb.BlitImage(skiaImage, VkImageLayout.TransferSrcOptimal, swapImage, VkImageLayout.TransferDstOptimal, [region], VkFilter.Nearest);
        // Back where the library expects it; its next Begin waits on fragment-shader work, which this chains into.
        Barrier(cb, skiaImage, VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, VkAccessFlags.None, VkAccessFlags.ShaderReadBit);
        Barrier(cb, swapImage, VkImageLayout.TransferDstOptimal, VkImageLayout.PresentSrcKHR,
            VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.BottomOfPipeBit, VkAccessFlags.TransferWriteBit, VkAccessFlags.None);
        cb.End();
        _queue.Submit([_imageAvailable[slot]], [VkPipelineStageFlags.TransferBit], [cb], [_renderDone[imageIndex]], _fences[slot]);
        _acquiredImage = imageIndex;
    }

    void Barrier(CommandBuffer cb, VkImage image, VkImageLayout from, VkImageLayout to,
        VkPipelineStageFlags srcStage, VkPipelineStageFlags dstStage, VkAccessFlags srcAccess, VkAccessFlags dstAccess) =>
        cb.PipelineBarrier(srcStage, dstStage, VkDependencyFlags.None, [], [],
        [
            new VkImageMemoryBarrier
            {
                SrcAccessMask = srcAccess, DstAccessMask = dstAccess, OldLayout = from, NewLayout = to,
                SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED, DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED, Image = image, SubresourceRange = _colorRange,
            },
        ]);

    /// <summary>Presents the image the last <see cref="DrawEntry"/> drew, outside the measured draw time like the other apps' Present.</summary>
    public void Present()
    {
        if (_acquiredImage is not int imageIndex)
            return;
        _acquiredImage = null;
        var wait = _renderDone[imageIndex];
        var swapchain = _swapchain;
        var result = _queue.PresentKHR(new VkPresentInfoKHR
        {
            WaitSemaphoreCount = 1,
            WaitSemaphores = &wait,
            SwapchainCount = 1,
            Swapchains = &swapchain,
            ImageIndices = &imageIndex,
        });
        if (result != VkResult.Success && result != VkResult.SuboptimalKHR)
            throw new InvalidOperationException($"vkQueuePresentKHR: {result}.");
        _frame++;
    }

    public void SaveScreenshot(PerfEntry entry, string path)
    {
        _device.WaitIdle();
        var rgba = PixelReadback.ReadRegion(_instance.VkHandle, _physicalDevice.VkHandle, _device.VkHandle, _queue.VkHandle, (uint)_family,
            _target.VkImage, 0, 0, W, H);
        // The readback leaves the image in TRANSFER_SRC; hand it back to the library in SHADER_READ_ONLY.
        var cb = _commandBuffers[_frame % FramesInFlight];
        cb.Reset();
        cb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        Barrier(cb, new VkImage((nint)_target.VkImage), VkImageLayout.TransferSrcOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, VkAccessFlags.None, VkAccessFlags.ShaderReadBit);
        cb.End();
        _queue.Submit([], [], [cb], [], VkFence.NullHandle);
        _device.WaitIdle();

        using var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }

    public bool IsActive => _window.IsFocused;

    public void SetTitle(string title) => _window.Title = title;

    public string GpuDescription => _gpu;

    public string MsaaDescription => "off";

    public void Dispose()
    {
        _device.WaitIdle();
        _drawer.Dispose();
        _target.Dispose();
        _context.Dispose();
        foreach (var fence in _fences)
            _device.DestroyFence(fence, null);
        foreach (var semaphore in _imageAvailable.Concat(_renderDone))
            _device.DestroySemaphore(semaphore, null);
        _device.DestroyCommandPool(_commandPool, null);
        _device.DestroySwapchainKHR(_swapchain, null);
        _instance.DestroySurfaceKHR(_surface, null);
        _device.Dispose();
        _instance.Dispose();
    }
}
