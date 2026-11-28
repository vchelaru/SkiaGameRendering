// Headless (no window) per-frame cost of SkiaRenderTarget2D Begin/draw/End on a BRUTAL Vulkan device.
// Usage: Benchmarks.Brutal [--warmup 30] [--frames 200] [--out results.md] [--direct]
// --direct instead compares SkiaRenderTarget2D plus its sampling pass with SkiaDirectCanvas at 1920x1080 (empty Begin/End,
// 1 rect, 100 circles, 1000 rects), adding per-phase CPU time, GPU time from timestamp queries, and the texture image's memory.
// Headless: no swapchain and no present, so vsync cannot mask cost. Runs on macOS (MoltenVK) too.
// Per config it reports: CPU ms/frame (Stopwatch around Begin..End, i.e. what the render thread pays) and
// throughput ms/frame (the whole loop plus a device WaitIdle, i.e. what the GPU can sustain).
using System.Diagnostics;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using SkiaGameRendering;
using SkiaGameRendering.Core.VK;
using SkiaSharp;

string Get(string name, string fallback)
{
    var i = Array.IndexOf(args, "--" + name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
var warmup = int.Parse(Get("warmup", "30"));
var frames = int.Parse(Get("frames", "200"));
var output = Get("out", "results.md");

var isMac = OperatingSystem.IsMacOS();
Instance instance;
unsafe
{
    // macOS: Skia draws through Metal, so the instance is created for MTLDevice and MTLCommandQueue export.
    var exportQueue = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalCommandQueue);
    var exportDevice = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalDevice, (IntPtr)(&exportQueue));
    instance = Vulkan.CreateInstance(new Instance.CreateInfo
    {
        AppInfo = new VkApplicationInfo { ApiVersion = 1 << 22 | 3 << 12 },
        Flags = isMac ? VkInstanceCreateFlags.EnumeratePortabilityBitKHR : VkInstanceCreateFlags.None,
        EnabledExtensions = isMac ? [VK.KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME] : [],
        EnabledLayers = [],
        Next = isMac ? &exportDevice : null,
    });
}
using var instanceScope = instance;
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
    // macOS with asynchronous submits: the timeline semaphore that orders Skia's Metal queue against this queue.
    var features12 = new VkPhysicalDeviceVulkan12Features { TimelineSemaphore = true };
    var features13 = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true, Next = &features12 }; // for --direct's sampling pass
    device = physicalDevice.CreateDevice(new Device.CreateInfo
    {
        Extensions = isMac ? ["VK_KHR_portability_subset", VkMetalObjectExport.ExtensionName] : [],
        Next = &features13,
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
unsafe
{
    // 1920x1080, one load per row. "Texture" is SkiaRenderTarget2D (library-managed layout, async End); "Direct" is
    // SkiaDirectCanvas into a plain app-owned image standing in for the swapchain image. The texture path also records and submits the pass that samples its image onto the app image (fullscreen triangle); the direct path has no such pass.
    // Each row is measured in two halves of the frames so a disagreement between halves shows.
    const int W = 1920, H = 1080;
    var directLoads = new (string name, Action<SKCanvas> draw)[]
    {
        ("empty", c => { }),
        ("1 rect", c => { c.Clear(SKColors.Black); paint.Color = rects[0].c; c.DrawRect(rects[0].x % (W - 130), rects[0].y % (H - 130), rects[0].w, rects[0].h, paint); }),
        ("100 circles", c =>
        {
            c.Clear(SKColors.Black);
            for (var i = 0; i < 100; i++) { paint.Color = rects[i].c; c.DrawCircle(rects[i].x % (W - 130) + 60, rects[i].y % (H - 130) + 60, rects[i].w / 2f + 5, paint); }
        }),
        ("1000 rects", c => loads[1].draw(c, W, H)),
    };
    var directLines = new List<string>
    {
        $"GPU: {gpu} ({driver}). OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. {W}x{H}, warmup {warmup}, frames {frames}.",
        "",
        "Texture = SkiaRenderTarget2D plus the app's pass sampling it onto the app image; Direct = SkiaDirectCanvas into the app image; Direct cached = the same with cacheSurfaces: true.",
        "",
        "Phases are CPU mean ms per half: Begin / draw calls / End, and for Texture the fence wait plus recording and submitting the sampling pass.",
        "GPU ms is the median gated frame (all of a frame's submits run back to back); Pass GPU ms is the sampling pass alone.",
        "",
        "| Load | Path | CPU mean ms | CPU median ms | Throughput ms/frame | Halves (CPU mean ms) | Phases, 1st half | Phases, 2nd half | GPU ms | Pass GPU ms |",
        "|---|---|---|---|---|---|---|---|---|---|",
    };
    using var textureTarget = new SkiaRenderTarget2D(context, W, H);
    using var directCanvas = new SkiaDirectCanvas(context);
    using var cachedCanvas = new SkiaDirectCanvas(context, cacheSurfaces: true);
    // macOS: created for Metal texture export, which SkiaDirectCanvas does every Begin.
    var exportTexture = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalTexture);
    var appImage = SkiaGameRendering.Core.VK.VkImageAllocator.Create(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, W, H, 37,
        pNext: isMac ? (IntPtr)(&exportTexture) : IntPtr.Zero);
    // Hand the image to the app's layout (SHADER_READ_ONLY_OPTIMAL), as a presented-then-reacquired image would be.
    directCanvas.Begin(appImage.Image, 37, W, H, 0);
    directCanvas.End(finalLayout: 5, synchronous: true);

    // The texture path's consumer pass: a fullscreen triangle whose fragment shader samples SkiaRenderTarget2D's image
    // onto the app-owned image (dynamic rendering). Both paths end with the app image in SHADER_READ_ONLY_OPTIMAL.
    var colorRange = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 };
    var appVkImage = new VkImage((nint)appImage.Image);
    var skiaView = device.CreateImageView(new VkImageViewCreateInfo
    {
        Image = new VkImage((nint)textureTarget.VkImage), ViewType = VkImageViewType._2D, Format = VkFormat.R8G8B8A8UNorm, SubresourceRange = colorRange,
    }, null);
    var appView = device.CreateImageView(new VkImageViewCreateInfo
    {
        Image = appVkImage, ViewType = VkImageViewType._2D, Format = VkFormat.R8G8B8A8UNorm, SubresourceRange = colorRange,
    }, null);
    var sampler = device.CreateSampler(new VkSamplerCreateInfo
    {
        MagFilter = VkFilter.Nearest, MinFilter = VkFilter.Nearest,
        AddressModeU = VkSamplerAddressMode.ClampToEdge, AddressModeV = VkSamplerAddressMode.ClampToEdge, AddressModeW = VkSamplerAddressMode.ClampToEdge,
    }, null);
    var binding = new VkDescriptorSetLayoutBinding
    {
        Binding = 0, DescriptorType = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit,
    };
    var setLayout = device.CreateDescriptorSetLayout(new VkDescriptorSetLayoutCreateInfo { BindingCount = 1, Bindings = &binding }, null);
    var poolSize = new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = 1 };
    var pool = device.CreateDescriptorPool(new VkDescriptorPoolCreateInfo { MaxSets = 1, PoolSizeCount = 1, PoolSizes = &poolSize }, null);
    var setLayoutLocal = setLayout;
    var sets = new VkDescriptorSet[1];
    device.AllocateDescriptorSets(new VkDescriptorSetAllocateInfo { DescriptorPool = pool, DescriptorSetCount = 1, SetLayouts = &setLayoutLocal }, sets);
    var imageInfo = new VkDescriptorImageInfo { Sampler = sampler, ImageView = skiaView, ImageLayout = VkImageLayout.ShaderReadOnlyOptimal };
    device.UpdateDescriptorSets(
        [new VkWriteDescriptorSet { DstSet = sets[0], DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.CombinedImageSampler, ImageInfo = &imageInfo }],
        []);
    using var compiler = ShaderC.CreateCompiler();
    using var options = ShaderC.CreateCompilerOptions();
    VkShaderModule Compile(ReadOnlySpan<byte> glsl, ShaderKind kind)
    {
        using var spirv = compiler.Compile(glsl, kind, "shader"u8, "main"u8, options);
        return ShaderModuleExtensions.FromSpirv(device, new ReadOnlySpan<byte>(spirv.GetBytes(), (int)spirv.GetLength()), null);
    }
    using var stages = new ShaderStages(device,
    [
        Presets.ShaderStage.Vertex(Compile("""
            #version 450
            layout(location = 0) out vec2 uv;
            void main() {
                vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                uv = p;
                gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
            }
            """u8, ShaderKind.VertexShader)),
        Presets.ShaderStage.Fragment(Compile("""
            #version 450
            layout(location = 0) in vec2 uv;
            layout(set = 0, binding = 0) uniform sampler2D tex;
            layout(location = 0) out vec4 outColor;
            void main() { outColor = texture(tex, uv); }
            """u8, ShaderKind.FragmentShader)),
    ], null);
    var vertexInput = new VertexInput(0, 0);
    var viewport = new VkViewport { Width = W, Height = H, MaxDepth = 1 };
    var scissor = new VkRect2D(new VkExtent2D(W, H));
    var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, Viewports = &viewport, ScissorCount = 1, Scissors = &scissor };
    var colorFormat = VkFormat.R8G8B8A8UNorm;
    var rendering = new VkPipelineRenderingCreateInfo { ColorAttachmentCount = 1, ColorAttachmentFormats = &colorFormat };
    var pipelineLayout = device.CreatePipelineLayout([setLayout], [], null);
    var pipeline = device.CreateGraphicsPipeline(VkPipelineCache.NullHandle, new VkGraphicsPipelineCreateInfo
    {
        Next = &rendering,
        StageCount = stages.Count,
        Stages = stages,
        VertexInputState = vertexInput,
        InputAssemblyState = Presets.InputAssembly.TriangleList,
        ViewportState = &viewportState,
        RasterizationState = Presets.Rasterization.Fill.CullNone,
        MultisampleState = Presets.Multisample.None,
        ColorBlendState = Presets.BlendState.BlendNone,
        Layout = pipelineLayout,
    }, null);
    var commandPool = device.CreateCommandPool(new VkCommandPoolCreateInfo { QueueFamilyIndex = family, Flags = VkCommandPoolCreateFlags.ResetCommandBufferBit }, null);
    // Two frames in flight, so the texture path does not wait on the frame it just submitted.
    var cbs = new[] { device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = commandPool, Level = VkCommandBufferLevel.Primary }),
                      device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = commandPool, Level = VkCommandBufferLevel.Primary }) };
    var fences = new[] { device.CreateFence(VkFenceCreateFlags.SignaledBit, null), device.CreateFence(VkFenceCreateFlags.SignaledBit, null) };
    var frameIndex = 0;

    void Barrier(CommandBuffer cb, VkImageLayout from, VkImageLayout to, VkPipelineStageFlags srcStage, VkPipelineStageFlags dstStage, VkAccessFlags srcAccess, VkAccessFlags dstAccess) =>
        cb.PipelineBarrier(srcStage, dstStage, VkDependencyFlags.None, [], [],
        [
            new VkImageMemoryBarrier
            {
                SrcAccessMask = srcAccess, DstAccessMask = dstAccess, OldLayout = from, NewLayout = to,
                SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED, DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED, Image = appVkImage, SubresourceRange = colorRange,
            },
        ]);

    // GPU time: a separate pass of gated frames. Each frame's first submit waits on a host-set event, so the GPU sees the
    // whole frame's submits back to back with no CPU gaps between them; a timestamp after the gate and one after the
    // frame's last submit bracket that work. The texture path also brackets its sampling pass on its own.
    var timestampPeriodNs = properties.Limits.TimestampPeriod;
    var queryPool = device.CreateQueryPool(new VkQueryPoolCreateInfo { QueryType = VkQueryType.Timestamp, QueryCount = 4 }, null);
    var gate = device.CreateEvent(new VkEventCreateInfo(), null);
    var gateCb = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = commandPool, Level = VkCommandBufferLevel.Primary });
    var endCb = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = commandPool, Level = VkCommandBufferLevel.Primary });
    var allToAll = new VkMemoryBarrier { SrcAccessMask = VkAccessFlags.MemoryWriteBit, DstAccessMask = VkAccessFlags.MemoryReadBit | VkAccessFlags.MemoryWriteBit };
    var queryBytes = new byte[4 * sizeof(ulong)];
    double QueryMs(int from, int to) =>
        (BitConverter.ToUInt64(queryBytes, to * 8) - BitConverter.ToUInt64(queryBytes, from * 8)) * timestampPeriodNs / 1e6;

    // Image memory: the texture path's own image (the app image is the same on both paths).
    var textureBytes = (long)device.GetImageMemoryRequirements(new VkImage((nint)textureTarget.VkImage)).Size;
    directLines.Insert(1, $"Texture path's SkiaRenderTarget2D image: {textureBytes / (1024.0 * 1024.0):F2} MiB device memory (vkGetImageMemoryRequirements); direct allocates none.");

    foreach (var (name, draw) in directLoads)
    foreach (var mode in new[] { 0, 1, 2 })
    {
        var isDirect = mode > 0;
        var dc = mode == 2 ? cachedCanvas : directCanvas;
        // CPU phases of the last frame: Begin, draw calls, End, then (texture only) fence wait plus the sampling pass.
        var phase = new double[4];
        void Frame(bool gpuTimed)
        {
            var t = Stopwatch.GetTimestamp();
            void Mark(int i) { var now = Stopwatch.GetTimestamp(); phase[i] = Stopwatch.GetElapsedTime(t, now).TotalMilliseconds; t = now; }
            if (isDirect)
            {
                dc.Begin(appImage.Image, 37, W, H, 5);
                Mark(0);
                draw(dc.Canvas);
                Mark(1);
                dc.End(finalLayout: 5);
                Mark(2);
                phase[3] = 0;
                return;
            }
            // Every load but "empty" clears itself; "empty" is a bare Begin/End on both paths.
            textureTarget.Begin(clear: false);
            Mark(0);
            draw(textureTarget.Canvas);
            Mark(1);
            textureTarget.End();
            Mark(2);

            var i = frameIndex++ & 1;
            device.WaitForFence(fences[i], -1);
            device.ResetFences([fences[i]]);
            var cb = cbs[i];
            cb.Reset();
            cb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            if (gpuTimed)
                cb.WriteTimestamp(VkPipelineStageFlags.AllCommandsBit, queryPool, 2);
            Barrier(cb, VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.ColorAttachmentOptimal,
                VkPipelineStageFlags.TopOfPipeBit | VkPipelineStageFlags.FragmentShaderBit, VkPipelineStageFlags.ColorAttachmentOutputBit,
                VkAccessFlags.None, VkAccessFlags.ColorAttachmentWriteBit);
            var color = new VkRenderingAttachmentInfo
            {
                ImageView = appView, ImageLayout = VkImageLayout.ColorAttachmentOptimal,
                LoadOp = VkAttachmentLoadOp.DontCare, StoreOp = VkAttachmentStoreOp.Store,
            };
            cb.BeginRendering(new VkRenderingInfo { RenderArea = new VkRect2D(new VkExtent2D(W, H)), LayerCount = 1, ColorAttachmentCount = 1, ColorAttachments = &color });
            cb.BindPipeline(VkPipelineBindPoint.Graphics, pipeline);
            cb.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, sets, []);
            cb.Draw(3, 1, 0, 0);
            cb.EndRendering();
            Barrier(cb, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                VkPipelineStageFlags.ColorAttachmentOutputBit, VkPipelineStageFlags.FragmentShaderBit,
                VkAccessFlags.ColorAttachmentWriteBit, VkAccessFlags.ShaderReadBit);
            if (gpuTimed)
                cb.WriteTimestamp(VkPipelineStageFlags.AllCommandsBit, queryPool, 3);
            cb.End();
            queue.Submit([], [], [cb], [], fences[i]);
            Mark(3);
        }
        for (var i = 0; i < warmup; i++)
            Frame(false);
        device.WaitIdle();
        var halfMs = new double[2];
        var phaseHalf = new double[2, 4];
        var frameMs = new double[frames];
        var total = Stopwatch.GetTimestamp();
        for (var i = 0; i < frames; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            Frame(false);
            frameMs[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            halfMs[i * 2 / frames] += frameMs[i];
            for (var p = 0; p < 4; p++)
                phaseHalf[i * 2 / frames, p] += phase[p];
        }
        Array.Sort(frameMs);
        var median = frames % 2 == 1 ? frameMs[frames / 2] : (frameMs[frames / 2 - 1] + frameMs[frames / 2]) / 2;
        device.WaitIdle();
        var throughput = Stopwatch.GetElapsedTime(total).TotalMilliseconds / frames;
        var perHalf = frames / 2;

        // Gated GPU frames, one at a time (WaitIdle after each), median of `frames`.
        var gpuMs = new double[frames];
        var passMs = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            gateCb.Reset();
            gateCb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            gateCb.ResetQueryPool(queryPool, 0, 4);
            gateCb.WaitEvents([gate], VkPipelineStageFlags.HostBit, VkPipelineStageFlags.AllCommandsBit, [], [], []);
            // Everything later on the queue waits for the gate: a barrier's second scope covers later submits too.
            gateCb.PipelineBarrier(VkPipelineStageFlags.AllCommandsBit, VkPipelineStageFlags.AllCommandsBit, VkDependencyFlags.None, [allToAll], [], []);
            gateCb.WriteTimestamp(VkPipelineStageFlags.AllCommandsBit, queryPool, 0);
            gateCb.End();
            queue.Submit([], [], [gateCb], [], VkFence.NullHandle);
            Frame(true);
            endCb.Reset();
            endCb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
            endCb.WriteTimestamp(VkPipelineStageFlags.AllCommandsBit, queryPool, 1);
            endCb.End();
            queue.Submit([], [], [endCb], [], VkFence.NullHandle);
            device.SetEvent(gate);
            device.WaitIdle();
            device.ResetEvent(gate);
            device.GetQueryPoolResults(queryPool, 0, isDirect ? 2 : 4, queryBytes, new Brutal.ByteSize64(sizeof(ulong)), VkQueryResultFlags._64Bit | VkQueryResultFlags.WaitBit);
            gpuMs[f] = QueryMs(0, 1);
            passMs[f] = isDirect ? 0 : QueryMs(2, 3);
        }
        Array.Sort(gpuMs);
        Array.Sort(passMs);

        string Phases(int h) => string.Join(" / ", Enumerable.Range(0, isDirect ? 3 : 4).Select(p => (phaseHalf[h, p] / (h == 0 ? perHalf : frames - perHalf)).ToString("F3")));
        var line = $"| {name} | {(mode == 2 ? "Direct cached" : isDirect ? "Direct" : "Texture")} | {(halfMs[0] + halfMs[1]) / frames:F3} | {median:F3} | {throughput:F3} | " +
            $"{halfMs[0] / perHalf:F3} / {halfMs[1] / (frames - perHalf):F3} | {Phases(0)} | {Phases(1)} | {gpuMs[frames / 2]:F3} | {(isDirect ? "-" : passMs[frames / 2].ToString("F3"))} |";
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
