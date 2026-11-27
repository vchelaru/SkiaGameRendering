// BRUTAL + SkiaGameRendering.Brutal. Creates a BRUTAL Vulkan device with no window, draws with Skia into a
// Vulkan image, and with --smoke-test reads ONE pixel back through Vulkan to check it (exit 0 = pass).
// The readback is test-only verification; the library has no CPU readback path.
// --screenshot <path> instead draws the shared Scene on its background at 800x800 and saves the Skia target to a PNG.
// BRUTAL has no engine frame, so the Skia target is the whole output.
// --smoke-test --direct-screenshot <path> also saves the Scene SkiaDirectCanvas drew into an app-owned image.
// --race-test draws the Scene for many frames while the app reads each frame on the GPU with no CPU wait, checks every
// read saw its own frame, and prints the per-frame cost (see RaceTest.cs). --async-via-layer-settings turns MoltenVK's
// synchronous submits off through VK_EXT_layer_settings (the env var route is MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS=0).
// On macOS (MoltenVK) Skia draws through Metal, which needs VK_EXT_metal_objects on the device and the Metal export
// structs on the instance (see docs/brutal/quickstart.md).
using System.Runtime.InteropServices;
using Brutal.VulkanApi;
using SampleShared;
using SkiaGameRendering;
using SkiaGameRendering.Core.VK;
using SkiaSharp;

const int Size = 64;
var shotAt = Array.IndexOf(args, "--screenshot");
var shotPath = shotAt >= 0 && shotAt + 1 < args.Length ? args[shotAt + 1] : null;

var extensions = new HashSet<string>();
VulkanHelpers_AddPlatformExtensions(extensions);

var isMac = OperatingSystem.IsMacOS();
var asyncViaLayerSettings = args.Contains("--async-via-layer-settings");
if (asyncViaLayerSettings)
    extensions.Add(MoltenVkLayerSettings.ExtensionName);
Instance instance;
unsafe
{
    // macOS: Skia exports the MTLDevice, which the instance must be created for.
    var exportDevice = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalDevice);
    instance = Vulkan.CreateInstance(new Instance.CreateInfo
    {
        AppInfo = new VkApplicationInfo { ApiVersion = 1 << 22 | 3 << 12 },
        Flags = isMac ? VkInstanceCreateFlags.EnumeratePortabilityBitKHR : VkInstanceCreateFlags.None,
        EnabledExtensions = extensions,
        EnabledLayers = [],
        Next = !isMac ? null : asyncViaLayerSettings ? MoltenVkLayerSettings.SynchronousQueueSubmitsOff(&exportDevice) : (void*)&exportDevice,
    });
}
using var instanceScope = instance;

var physicalDevice = instance.PhysicalDevices[0];
var queueFamily = physicalDevice.GetFirstQueueFamilyWithFlag(VkQueueFlags.GraphicsBit);
var properties = physicalDevice.GetProperties();
unsafe { Console.WriteLine($"GPU: {Brutal.Strings.Utf8.GetString(properties.DeviceName)}"); }

var smokeTest = args.Contains("--smoke-test");
int exitCode = 0;
unsafe
{
    var priority = 1.0f;
    // macOS: the timeline semaphore that orders Skia's Metal queue against this queue.
    var features12 = new VkPhysicalDeviceVulkan12Features { TimelineSemaphore = true };
    using var device = physicalDevice.CreateDevice(new Device.CreateInfo
    {
        // MoltenVK is a portability driver, and Skia's Metal path exports objects through VK_EXT_metal_objects.
        Extensions = isMac ? ["VK_KHR_portability_subset", VkMetalObjectExport.ExtensionName] : [],
        Next = isMac ? &features12 : null,
        QueueCreateInfos =
        [
            new VkDeviceQueueCreateInfo { QueueFamilyIndex = queueFamily, QueueCount = 1, QueuePriorities = &priority },
        ],
    });
    var queue = device.GetQueue(queueFamily, 0);

    using var context = new SkiaVulkanContext(
        instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queueFamily);
    if (args.Contains("--race-test"))
    {
        exitCode = RaceTest.Run(physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queueFamily, context);
        device.WaitIdle();
        return exitCode;
    }
    if (shotPath != null)
    {
        using var scene = new SkiaRenderTarget2D(context, Scene.Width, Scene.Height);
        scene.Begin();
        scene.Canvas.Clear(Scene.Background);
        Scene.Draw(scene.Canvas);
        scene.End(synchronous: true);
        var rgba = PixelReadback.ReadRegion(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queueFamily,
            scene.VkImage, 0, 0, Scene.Width, Scene.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(shotPath, png.ToArray());
        device.WaitIdle();
        return 0;
    }

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
    if (!smokeTest)
        Console.WriteLine("Pass --smoke-test to assert it.");
    if (smokeTest)
    {
        exitCode = px[0] == 255 && px[1] == 0 && px[2] == 0 && px[3] == 255 ? 0 : 1;
        Console.WriteLine(exitCode == 0 ? "PASS" : "FAIL");
    }

    if (smokeTest && exitCode == 0)
    {
        // The same Scene through the texture and straight into an app-owned image (what a swapchain image is) must agree.
        using var scene = new SkiaRenderTarget2D(context, Scene.Width, Scene.Height);
        scene.Begin();
        scene.Canvas.Clear(Scene.Background);
        Scene.Draw(scene.Canvas);
        scene.End(synchronous: true);
        var viaTexture = PixelReadback.ReadRegion(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queueFamily,
            scene.VkImage, 0, 0, Scene.Width, Scene.Height);

        // macOS: created for Metal texture export, which SkiaDirectCanvas does every Begin (a swapchain image can't carry this).
        var exportTexture = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalTexture);
        var appImage = SkiaGameRendering.Core.VK.VkImageAllocator.Create(
            instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, Scene.Width, Scene.Height, 37 /* R8G8B8A8_UNORM */,
            pNext: isMac ? (IntPtr)(&exportTexture) : IntPtr.Zero);
        using var direct = new SkiaDirectCanvas(context);
        // Twice: the second pass starts from the layout the first one handed back.
        for (var pass = 0; pass < 2; pass++)
        {
            direct.Begin(appImage.Image, 37, Scene.Width, Scene.Height, pass == 0 ? 0u : 5u);
            direct.Canvas.Clear(Scene.Background);
            Scene.Draw(direct.Canvas);
            direct.End(finalLayout: 5, synchronous: true);
        }
        var viaDirect = PixelReadback.ReadRegion(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queueFamily,
            appImage.Image, 0, 0, Scene.Width, Scene.Height);
        device.WaitIdle();
        SkiaGameRendering.Core.VK.VkImageAllocator.Destroy(device.VkHandle, appImage);
        var directShotAt = Array.IndexOf(args, "--direct-screenshot");
        if (directShotAt >= 0 && directShotAt + 1 < args.Length)
        {
            using var directBitmap = new SKBitmap(new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(viaDirect, 0, directBitmap.GetPixels(), viaDirect.Length);
            using var directPng = SKImage.FromBitmap(directBitmap).Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(args[directShotAt + 1], directPng.ToArray());
        }

        var differing = 0;
        for (var p = 0; p < Scene.Width * Scene.Height; p++)
            for (var c = 0; c < 4; c++)
                if (Math.Abs(viaTexture[p * 4 + c] - viaDirect[p * 4 + c]) > 4)
                {
                    differing++;
                    break;
                }
        var fraction = differing / (double)(Scene.Width * Scene.Height);
        var directPass = fraction < 0.005;
        Console.WriteLine($"direct canvas vs texture: {differing} pixels differ ({fraction:P3}) -> {(directPass ? "PASS" : "FAIL")}");
        if (!directPass)
            exitCode = 1;
    }
    device.WaitIdle();
}
return exitCode;

static void VulkanHelpers_AddPlatformExtensions(HashSet<string> extensions)
{
    if (OperatingSystem.IsMacOS())
        extensions.Add(VK.KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME);
}

