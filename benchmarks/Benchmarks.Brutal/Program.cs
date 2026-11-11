// Headless (no window) per-frame cost of SkiaRenderTarget2D Begin/draw/End on a BRUTAL Vulkan device.
// Usage: Benchmarks.Brutal [--warmup 30] [--frames 200] [--out results.md] [--direct]
// --direct instead compares SkiaRenderTarget2D with SkiaDirectCanvas at 1920x1080 (1 rect and 1000 rects).
// Per config it reports: CPU ms/frame (Stopwatch around Begin..End, i.e. what the render thread pays) and
// throughput ms/frame (the whole loop plus a device WaitIdle, i.e. what the GPU can sustain).
using System.Diagnostics;
using Brutal.VulkanApi;
using SkiaGameRendering;
using SkiaSharp;

string Get(string name, string fallback)
{
    var i = Array.IndexOf(args, "--" + name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
var warmup = int.Parse(Get("warmup", "30"));
var frames = int.Parse(Get("frames", "200"));
var output = Get("out", "results.md");

using var instance = Vulkan.CreateInstance(new Instance.CreateInfo
{
    AppInfo = new VkApplicationInfo { ApiVersion = 1 << 22 | 3 << 12 },
    EnabledExtensions = [],
    EnabledLayers = [],
});
var physicalDevice = instance.PhysicalDevices[0];
var family = physicalDevice.GetFirstQueueFamilyWithFlag(VkQueueFlags.GraphicsBit);
var properties = physicalDevice.GetProperties();
string gpu;
unsafe { gpu = Brutal.Strings.Utf8.GetString(properties.DeviceName); }
var driver = $"driverVersion 0x{properties.DriverVersion:X}, API {properties.ApiVersion >> 22}.{(properties.ApiVersion >> 12) & 0x3FF}";
Device device;
unsafe
{
    var priority = 1.0f;
    device = physicalDevice.CreateDevice(new Device.CreateInfo
    {
        Extensions = [],
        QueueCreateInfos = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = family, QueueCount = 1, QueuePriorities = &priority }],
    });
}
using var _ = device;
var queue = device.GetQueue(family, 0);
using var context = new SkiaVulkanContext(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)family);

var rng = new Random(1);
var rects = Enumerable.Range(0, 1000).Select(_ => (x: rng.Next(0, 3700), y: rng.Next(0, 2000), w: rng.Next(10, 120), h: rng.Next(10, 120), c: new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)))).ToArray();
using var paint = new SKPaint { IsAntialias = true };
using var font = new SKFont(SKTypeface.Default, 18);
using var textPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
using var path = new SKPath();
path.MoveTo(0, 0); path.CubicTo(30, -20, 60, 40, 90, 0); path.LineTo(60, 50); path.Close();

var loads = new (string name, Action<SKCanvas, int, int> draw)[]
{
    ("clear only", (c, w, h) => c.Clear(SKColors.CornflowerBlue)),
    ("1000 rects", (c, w, h) =>
    {
        c.Clear(SKColors.Black);
        foreach (var r in rects) { paint.Color = r.c; c.DrawRect(r.x % (w - 130), r.y % (h - 130), r.w, r.h, paint); }
    }),
    ("1000 paths", (c, w, h) =>
    {
        c.Clear(SKColors.Black);
        foreach (var r in rects)
        {
            paint.Color = r.c;
            c.Save(); c.Translate(r.x % (w - 130), r.y % (h - 130)); c.DrawPath(path, paint); c.Restore();
        }
    }),
    ("text, 200 lines", (c, w, h) =>
    {
        c.Clear(SKColors.Black);
        for (var i = 0; i < 200; i++)
            c.DrawText($"Line {i}: The quick brown fox jumps over the lazy dog 0123456789", 10, 20 + i * (h - 40) / 200f, font, textPaint);
    }),
};

if (args.Contains("--direct"))
{
    // 1920x1080, one load per row. "Texture" is SkiaRenderTarget2D (library-managed layout, async End); "Direct" is
    // SkiaDirectCanvas into a plain app-owned image standing in for the swapchain image. Neither has a consumer pass.
    // Each row is measured in two halves of the frames so a disagreement between halves shows.
    const int W = 1920, H = 1080;
    var directLoads = new (string name, Action<SKCanvas> draw)[]
    {
        ("1 rect", c => { c.Clear(SKColors.Black); paint.Color = rects[0].c; c.DrawRect(rects[0].x % (W - 130), rects[0].y % (H - 130), rects[0].w, rects[0].h, paint); }),
        ("1000 rects", c => loads[1].draw(c, W, H)),
    };
    var directLines = new List<string>
    {
        $"GPU: {gpu} ({driver}). OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. {W}x{H}, warmup {warmup}, frames {frames}.",
        "",
        "| Load | Path | CPU ms/frame | Throughput ms/frame | Halves (CPU ms) |",
        "|---|---|---|---|---|",
    };
    using var textureTarget = new SkiaRenderTarget2D(context, W, H);
    using var directCanvas = new SkiaDirectCanvas(context);
    var appImage = SkiaGameRendering.Core.VK.VkImageAllocator.Create(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, W, H, 37);
    // Hand the image to the app's layout (SHADER_READ_ONLY_OPTIMAL), as a presented-then-reacquired image would be.
    directCanvas.Begin(appImage.Image, 37, W, H, 0);
    directCanvas.End(finalLayout: 5, synchronous: true);
    foreach (var (name, draw) in directLoads)
    foreach (var isDirect in new[] { false, true })
    {
        void Frame()
        {
            if (isDirect)
            {
                directCanvas.Begin(appImage.Image, 37, W, H, 5);
                draw(directCanvas.Canvas);
                directCanvas.End(finalLayout: 5);
            }
            else
            {
                textureTarget.Begin();
                draw(textureTarget.Canvas);
                textureTarget.End();
            }
        }
        for (var i = 0; i < warmup; i++)
            Frame();
        device.WaitIdle();
        var halfMs = new double[2];
        var total = Stopwatch.GetTimestamp();
        for (var i = 0; i < frames; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            Frame();
            halfMs[i * 2 / frames] += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        device.WaitIdle();
        var throughput = Stopwatch.GetElapsedTime(total).TotalMilliseconds / frames;
        var perHalf = frames / 2;
        var line = $"| {name} | {(isDirect ? "Direct" : "Texture")} | {(halfMs[0] + halfMs[1]) / frames:F3} | {throughput:F3} | {halfMs[0] / perHalf:F3} / {halfMs[1] / (frames - perHalf):F3} |";
        directLines.Add(line);
        Console.WriteLine(line);
    }
    device.WaitIdle();
    SkiaGameRendering.Core.VK.VkImageAllocator.Destroy(device.VkHandle, appImage);
    File.WriteAllLines(output, directLines);
    Console.WriteLine($"Wrote {output}");
    return;
}

var lines = new List<string>
{
    $"GPU: {gpu} ({driver}). OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. Warmup {warmup}, frames {frames}.",
    "",
    "| Size | Load | Layout | End | CPU ms/frame | Throughput ms/frame |",
    "|---|---|---|---|---|---|",
};
foreach (var (w, h) in new[] { (1920, 1080), (3840, 2160) })
foreach (var (name, draw) in loads)
foreach (var appManages in new[] { false, true })
foreach (var sync in new[] { false, true })
{
    using var target = new SkiaRenderTarget2D(context, w, h, SKColorType.Rgba8888, appManages);
    void Frame(bool timeIt, ref double cpu)
    {
        var t0 = Stopwatch.GetTimestamp();
        target.Begin();
        draw(target.Canvas, w, h);
        target.End(sync);
        if (timeIt)
            cpu += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }
    double dummy = 0;
    for (var i = 0; i < warmup; i++)
        Frame(false, ref dummy);
    device.WaitIdle();
    double cpuMs = 0;
    var total = Stopwatch.GetTimestamp();
    for (var i = 0; i < frames; i++)
        Frame(true, ref cpuMs);
    device.WaitIdle();
    var throughput = Stopwatch.GetElapsedTime(total).TotalMilliseconds / frames;
    var line = $"| {w}x{h} | {name} | {(appManages ? "app" : "library")} | {(sync ? "sync" : "async")} | {cpuMs / frames:F3} | {throughput:F3} |";
    lines.Add(line);
    Console.WriteLine(line);
}
File.WriteAllLines(output, lines);
Console.WriteLine($"Wrote {output}");
