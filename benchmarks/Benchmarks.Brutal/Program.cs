// Headless (no window) per-frame cost of SkiaRenderTarget2D Begin/draw/End on a BRUTAL Vulkan device.
// Usage: Benchmarks.Brutal [--warmup 30] [--frames 200] [--out results.md] [--direct]
// --direct instead compares SkiaRenderTarget2D plus its sampling pass with SkiaDirectCanvas at 1920x1080 (1 rect and 1000 rects).
// Per config it reports: CPU ms/frame (Stopwatch around Begin..End, i.e. what the render thread pays) and
// throughput ms/frame (the whole loop plus a device WaitIdle, i.e. what the GPU can sustain).
using System.Diagnostics;
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
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
    var features13 = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true }; // for --direct's sampling pass
    device = physicalDevice.CreateDevice(new Device.CreateInfo
    {
        Extensions = [],
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
                return;
            }
            var i = frameIndex++ & 1;
            device.WaitForFence(fences[i], -1);
            device.ResetFences([fences[i]]);
            textureTarget.Begin();
            draw(textureTarget.Canvas);
            textureTarget.End();

            var cb = cbs[i];
            cb.Reset();
            cb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
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
            cb.End();
            queue.Submit([], [], [cb], [], fences[i]);
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
