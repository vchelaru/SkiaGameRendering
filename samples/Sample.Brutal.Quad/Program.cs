// A BRUTAL Vulkan renderer that samples SkiaRenderTarget2D.VkImage on a textured quad every frame.
// Headless: the quad is drawn into an offscreen image (no window or swapchain), with dynamic rendering.
// --smoke-test renders 30 animated frames in both layout modes, reads ONE pixel of the last frame back
// through Vulkan (test-only) and exits 0 if it is right.
using Brutal.ShaderCApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using SkiaGameRendering;
using SkiaGameRendering.Core.VK;
using SkiaSharp;

const int Size = 256;
const int Frames = 30;
var smokeTest = args.Contains("--smoke-test");

var failures = 0;
foreach (var appManagesLayout in new[] { false, true })
{
    var (px, expected) = Run(appManagesLayout);
    var ok = px.SequenceEqual(expected);
    Console.WriteLine($"appManagesLayout={appManagesLayout}: pixel {string.Join(",", px)} expected {string.Join(",", expected)} {(ok ? "ok" : "MISMATCH")}");
    if (!ok)
        failures++;
}
Console.WriteLine(failures == 0 ? "PASS" : "FAIL");
return smokeTest ? (failures == 0 ? 0 : 1) : 0;

static unsafe (byte[] pixel, byte[] expected) Run(bool appManagesLayout)
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
    for (var frame = 0; frame < Frames; frame++)
    {
        // Skia: animated disc on a blue background.
        skia.Begin();
        skia.Canvas.Clear(new SKColor(30, 144, 255));
        using (var paint = new SKPaint { Color = new SKColor(255, 0, 0), IsAntialias = true })
        {
            var (cx, cy) = DiscCenter(frame);
            skia.Canvas.DrawCircle(cx, cy, 10, paint);
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
    }
    device.WaitIdle();

    var (lx, ly) = DiscCenter(Frames - 1);
    var pixel = PixelReadback.ReadPixel(instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)family, outAlloc.Image, (int)lx, (int)ly);

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
    return (pixel, [255, 0, 0, 255]);
}

static (float x, float y) DiscCenter(int frame)
{
    var a = frame * 0.2f;
    return (MathF.Round(128 + 80 * MathF.Cos(a)), MathF.Round(128 + 80 * MathF.Sin(a)));
}
