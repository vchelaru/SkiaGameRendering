// Headless (no window) per-frame cost of SkiaRenderTarget2D Begin/draw/End on a BRUTAL Vulkan device.
// Usage: Benchmarks.Brutal [--warmup 30] [--frames 200] [--out results.md]
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
