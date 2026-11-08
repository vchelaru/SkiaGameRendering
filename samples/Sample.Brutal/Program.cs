// BRUTAL + SkiaGameRendering.Brutal. Creates a BRUTAL Vulkan device with no window, draws with Skia into a
// Vulkan image, and with --smoke-test reads ONE pixel back through Vulkan to check it (exit 0 = pass).
// The readback is test-only verification; the library has no CPU readback path.
using Brutal.VulkanApi;
using SkiaGameRendering;
using SkiaSharp;

const int Size = 64;

var extensions = new HashSet<string>();
VulkanHelpers_AddPlatformExtensions(extensions);

using var instance = Vulkan.CreateInstance(new Instance.CreateInfo
{
    AppInfo = new VkApplicationInfo { ApiVersion = 1 << 22 | 3 << 12 },
    Flags = VkInstanceCreateFlags.None,
    EnabledExtensions = extensions,
    EnabledLayers = [],
});

var physicalDevice = instance.PhysicalDevices[0];
var queueFamily = physicalDevice.GetFirstQueueFamilyWithFlag(VkQueueFlags.GraphicsBit);
var properties = physicalDevice.GetProperties();
unsafe { Console.WriteLine($"GPU: {Brutal.Strings.Utf8.GetString(properties.DeviceName)}"); }

int exitCode;
unsafe
{
    var priority = 1.0f;
    using var device = physicalDevice.CreateDevice(new Device.CreateInfo
    {
        Extensions = [],
        QueueCreateInfos =
        [
            new VkDeviceQueueCreateInfo { QueueFamilyIndex = queueFamily, QueueCount = 1, QueuePriorities = &priority },
        ],
    });
    var queue = device.GetQueue(queueFamily, 0);

    using var context = new SkiaVulkanContext(
        instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queueFamily);
    using var target = new SkiaRenderTarget2D(context, Size, Size);

    // Two frames: the second checks the layout round trip (app -> Skia -> app -> Skia -> app).
    target.Begin();
    target.Canvas.Clear(SKColors.Green);
    target.End();

    target.Begin();
    target.Canvas.Clear(new SKColor(30, 144, 255));
    using (var paint = new SKPaint { Color = new SKColor(255, 0, 0), IsAntialias = true })
        target.Canvas.DrawRect(16, 16, 32, 32, paint);
    target.End(synchronous: true);

    var px = PixelReadback.ReadPixel(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queueFamily,
        target.VkImage, Size / 2, Size / 2);
    Console.WriteLine($"pixel ({Size / 2},{Size / 2}) = {px[0]},{px[1]},{px[2]},{px[3]} (expected 255,0,0,255)");
    exitCode = px[0] == 255 && px[1] == 0 && px[2] == 0 && px[3] == 255 ? 0 : 1;
    Console.WriteLine(exitCode == 0 ? "PASS" : "FAIL");
    device.WaitIdle();
}
return exitCode;

static void VulkanHelpers_AddPlatformExtensions(HashSet<string> extensions)
{
    if (OperatingSystem.IsMacOS())
        extensions.Add(VK.KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME);
}

