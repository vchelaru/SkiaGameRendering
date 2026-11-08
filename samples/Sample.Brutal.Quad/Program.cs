// A BRUTAL Vulkan renderer that samples SkiaRenderTarget2D.VkImage on a textured quad every frame.
// Headless: the quad is drawn into an offscreen image (no window or swapchain), with dynamic rendering.
// Each frame draws samples/Shared/Scene.cs (the scene every other sample draws) plus a small orbiting dot.
// --smoke-test renders 30 frames in both layout modes, reads the final image back through Vulkan (test-only) and checks
// the shared scene's first two cells (red circle, blue drop), the gradient cell and an empty corner, like the other samples.
// SKIAGAMERENDERING_BRUTAL_SCREENSHOTS=<dir> renders 61 frames and saves the quad output at frames 0, 30 and 60.
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using SkiaGameRendering;
using SkiaGameRendering.Core.VK;
using SkiaSharp;
using SharedScene = SampleShared.Scene;

const int Size = 800; // same as Sample.MonoGame.DesktopGL's 800x800 window; Scene cells are 400x400
var screenshotDir = Environment.GetEnvironmentVariable("SKIAGAMERENDERING_BRUTAL_SCREENSHOTS");
var frames = string.IsNullOrEmpty(screenshotDir) ? 30 : 61;
var smokeTest = args.Contains("--smoke-test");

var failures = 0;
foreach (var appManagesLayout in new[] { false, true })
{
    var final = Run(appManagesLayout, frames, screenshotDir);
    var circle = At(final, 200, 200);
    var drop = At(final, 600, 200);
    var gradient = At(final, 200, 600);
    var corner = At(final, 799, 799);
    var passed = circle[0] > 200 && circle[1] < 50 && circle[2] < 50
        && drop[0] < 100 && drop[2] > 150
        && gradient[0] > 100 && gradient[1] < 80 && gradient[2] > 100
        && corner[3] == 0;
    Console.WriteLine($"appManagesLayout={appManagesLayout}: circle={string.Join(",", circle)} drop={string.Join(",", drop)} gradient={string.Join(",", gradient)} corner={string.Join(",", corner)} {(passed ? "ok" : "MISMATCH")}");
    if (!passed)
        failures++;
}
Console.WriteLine(failures == 0 ? "PASS" : "FAIL");
return smokeTest ? (failures == 0 ? 0 : 1) : 0;

static byte[] At(byte[] rgba, int x, int y)
{
    var o = (y * Size + x) * 4;
    return [rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]];
}

static unsafe byte[] Run(bool appManagesLayout, int frames, string? screenshotDir)
{
    var instanceExtensions = new HashSet<string>();
    VulkanHelpers.AddPlatformRequiredExtensions(instanceExtensions);
    using var instance = Vulkan.CreateInstance(new Instance.CreateInfo
    {
        AppInfo = new VkApplicationInfo { ApiVersion = (int)VulkanHelpers.Api.VERSION_1_3 },
        Flags = VulkanHelpers.GetPlatformRequiredInstanceCreateFlags(),
        EnabledExtensions = instanceExtensions,
        EnabledLayers = [],
    });
    var physicalDevice = instance.PhysicalDevices[0];
    var family = physicalDevice.GetFirstQueueFamilyWithFlag(VkQueueFlags.GraphicsBit);
    var deviceExtensions = new HashSet<string>();
    VulkanHelpers.AddMandatoryIfSupportedDeviceExtensions(deviceExtensions, physicalDevice);
    var priority = 1.0f;
    var features13 = new VkPhysicalDeviceVulkan13Features { DynamicRendering = true };
    using var device = physicalDevice.CreateDevice(new DeviceEx.CreateInfo
    {
        Extensions = deviceExtensions,
        QueueCreateInfos = [new VkDeviceQueueCreateInfo { QueueFamilyIndex = family, QueueCount = 1, QueuePriorities = &priority }],
        Next = &features13,
    });
    var queue = device.GetQueue(family, 0);

    using var context = new SkiaVulkanContext(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)family);
    using var skia = new SkiaRenderTarget2D(context, Size, Size, SKColorType.Rgba8888, appManagesLayout);

    // Offscreen output image: Core.VK's allocator gives COLOR_ATTACHMENT | TRANSFER_SRC | SAMPLED, device-local.
    var outAlloc = VkImageAllocator.Create(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, Size, Size, 37);
    var outImage = new VkImage((nint)outAlloc.Image);
    var colorRange = new VkImageSubresourceRange { AspectMask = VkImageAspectFlags.ColorBit, LevelCount = 1, LayerCount = 1 };
    var skiaView = device.CreateImageView(new VkImageViewCreateInfo
    {
        Image = new VkImage((nint)skia.VkImage), ViewType = VkImageViewType._2D, Format = VkFormat.R8G8B8A8UNorm, SubresourceRange = colorRange,
    }, null);
    var outView = device.CreateImageView(new VkImageViewCreateInfo
    {
        Image = outImage, ViewType = VkImageViewType._2D, Format = VkFormat.R8G8B8A8UNorm, SubresourceRange = colorRange,
    }, null);
    var sampler = device.CreateSampler(new VkSamplerCreateInfo
    {
        MagFilter = VkFilter.Nearest, MinFilter = VkFilter.Nearest,
        AddressModeU = VkSamplerAddressMode.ClampToEdge, AddressModeV = VkSamplerAddressMode.ClampToEdge, AddressModeW = VkSamplerAddressMode.ClampToEdge,
    }, null);

    // Descriptor: one combined image sampler, the Skia image.
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
    var viewport = new VkViewport { Width = Size, Height = Size, MaxDepth = 1 };
    var scissor = new VkRect2D(new VkExtent2D(Size, Size));
    var viewportState = new VkPipelineViewportStateCreateInfo { ViewportCount = 1, Viewports = &viewport, ScissorCount = 1, Scissors = &scissor };
    var format = VkFormat.R8G8B8A8UNorm;
    var rendering = new VkPipelineRenderingCreateInfo { ColorAttachmentCount = 1, ColorAttachmentFormats = &format };
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
    var cb = device.AllocateCommandBuffer(new VkCommandBufferAllocateInfo { CommandPool = commandPool, Level = VkCommandBufferLevel.Primary });
    var fence = device.CreateFence(VkFenceCreateFlags.SignaledBit, null);

    void Barrier(VkImage image, VkImageLayout from, VkImageLayout to, VkPipelineStageFlags srcStage, VkPipelineStageFlags dstStage, VkAccessFlags srcAccess, VkAccessFlags dstAccess) =>
        cb.PipelineBarrier(srcStage, dstStage, VkDependencyFlags.None, [], [],
        [
            new VkImageMemoryBarrier
            {
                SrcAccessMask = srcAccess, DstAccessMask = dstAccess, OldLayout = from, NewLayout = to,
                SrcQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED, DstQueueFamilyIndex = VK.QUEUE_FAMILY_IGNORED, Image = image, SubresourceRange = colorRange,
            },
        ]);

    var skiaImage = new VkImage((nint)skia.VkImage);
    var outLayout = VkImageLayout.Undefined;
    for (var frame = 0; frame < frames; frame++)
    {
        // Skia: the shared Scene, plus a dot orbiting in the empty bottom-right cell so frames differ.
        skia.Begin();
        SharedScene.Draw(skia.Canvas, Size, Size);
        using (var paint = new SKPaint { Color = SKColors.Orange, IsAntialias = true })
        {
            var angle = frame * 0.1f;
            skia.Canvas.DrawCircle(600 + 120 * MathF.Cos(angle), 600 + 120 * MathF.Sin(angle), 14, paint);
        }
        skia.End();

        device.WaitForFence(fence, -1);
        device.ResetFences([fence]);
        cb.Reset();
        cb.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        if (appManagesLayout)
            Barrier(skiaImage, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                VkPipelineStageFlags.ColorAttachmentOutputBit, VkPipelineStageFlags.FragmentShaderBit,
                VkAccessFlags.ColorAttachmentWriteBit, VkAccessFlags.ShaderReadBit);
        Barrier(outImage, outLayout, VkImageLayout.ColorAttachmentOptimal,
            VkPipelineStageFlags.TopOfPipeBit | VkPipelineStageFlags.FragmentShaderBit, VkPipelineStageFlags.ColorAttachmentOutputBit,
            VkAccessFlags.None, VkAccessFlags.ColorAttachmentWriteBit);
        var color = new VkRenderingAttachmentInfo
        {
            ImageView = outView, ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearColorValue { Float32 = new Brutal.Numerics.float4(0, 0, 0, 1) },
        };
        cb.BeginRendering(new VkRenderingInfo { RenderArea = new VkRect2D(new VkExtent2D(Size, Size)), LayerCount = 1, ColorAttachmentCount = 1, ColorAttachments = &color });
        cb.BindPipeline(VkPipelineBindPoint.Graphics, pipeline);
        cb.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipelineLayout, 0, sets, []);
        cb.Draw(3, 1, 0, 0);
        cb.EndRendering();
        // The readback below expects the output in SHADER_READ_ONLY_OPTIMAL.
        Barrier(outImage, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags.ColorAttachmentOutputBit, VkPipelineStageFlags.FragmentShaderBit,
            VkAccessFlags.ColorAttachmentWriteBit, VkAccessFlags.ShaderReadBit);
        outLayout = VkImageLayout.ShaderReadOnlyOptimal;
        if (appManagesLayout)
            Barrier(skiaImage, VkImageLayout.ShaderReadOnlyOptimal, VkImageLayout.ColorAttachmentOptimal,
                VkPipelineStageFlags.FragmentShaderBit, VkPipelineStageFlags.ColorAttachmentOutputBit,
                VkAccessFlags.None, VkAccessFlags.ColorAttachmentWriteBit);
        cb.End();
        queue.Submit([], [], [cb], [], fence);
        if (!string.IsNullOrEmpty(screenshotDir) && !appManagesLayout && frame is 0 or 30 or 60)
        {
            device.WaitIdle();
            var shot = PixelReadback.ReadRegion(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)family, outAlloc.Image, 0, 0, Size, Size);
            Directory.CreateDirectory(screenshotDir);
            Save(shot, Path.Combine(screenshotDir, $"quad-frame-{frame:D2}.png"));
            outLayout = VkImageLayout.TransferSrcOptimal; // the readback left it here
        }
    }
    device.WaitIdle();

    var final = PixelReadback.ReadRegion(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)family, outAlloc.Image, 0, 0, Size, Size);

    device.DestroyFence(fence, null);
    device.DestroyCommandPool(commandPool, null);
    device.DestroyPipeline(pipeline, null);
    device.DestroyPipelineLayout(pipelineLayout, null);
    device.DestroyDescriptorPool(pool, null);
    device.DestroyDescriptorSetLayout(setLayout, null);
    device.DestroySampler(sampler, null);
    device.DestroyImageView(outView, null);
    device.DestroyImageView(skiaView, null);
    VkImageAllocator.Destroy(device.VkHandle, outAlloc);
    return final;
}

static void Save(byte[] rgba, string path)
{
    using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
    using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(path, data.ToArray());
}
