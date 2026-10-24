using System.Reflection;
using System.Threading;
using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Stride.Graphics;
using Vortice.Vulkan;

namespace SkiaGameRendering
{
    /// <summary>
    /// Stride-specific adapter over <see cref="VkSkiaSurfaceFactory"/> (see that class for how the
    /// Vulkan interop itself works). This class's only job is pulling Stride's Vulkan
    /// instance/physical-device/device/queue handles and handing them to the shared factory, and
    /// wiring Stride's own queue lock through so Skia's and Stride's <c>vkQueueSubmit</c> calls stay
    /// externally synchronized on the shared <c>VkQueue</c>, per the Vulkan spec.
    ///
    /// MAINTENANCE NOTES:
    /// - Targets Stride 4.4.1+, built with <c>StrideGraphicsApi=Vulkan</c> downstream (see
    ///   SkiaGameRendering.Stride.VK.csproj's TFM comment). Unlike
    ///   <c>SkiaGameRendering.Stride.D3D11</c>'s D3D11 adapter, EVERY Vulkan handle this class needs -
    ///   <see cref="GraphicsDevice"/>'s <c>NativeInstance</c>/<c>NativePhysicalDevice</c>/
    ///   <c>NativeDevice</c> properties, its <c>NativeCommandQueue</c>/<c>QueueLock</c> fields, and
    ///   <see cref="Texture"/>'s <c>NativeImage</c>/<c>NativeLayout</c>/<c>NativeFormat</c> fields -
    ///   is <c>internal</c> in Stride.Graphics's <c>GraphicsDevice.Vulkan.cs</c>/<c>Texture.Vulkan.cs</c>,
    ///   confirmed directly by reflecting the actual <c>StrideGraphicsApi=Vulkan</c>-built
    ///   <c>Stride.Graphics.dll</c> (Stride source at commit <c>34c7f40f</c>, matching the
    ///   $(StrideVersion) pinned in eng/Versions.props), not assumed from source alone. Every one is
    ///   pinned by <c>tests/Tests.Stride.VK/StrideVulkanReflectionTests.cs</c>.
    /// - Stride's Vulkan handle structs (<c>VkInstance</c>, <c>VkPhysicalDevice</c>, <c>VkDevice</c>,
    ///   <c>VkQueue</c>) are dispatchable handles - thin <c>IntPtr</c> wrappers with a public implicit
    ///   conversion operator - and <c>VkImage</c> is a non-dispatchable handle wrapping a
    ///   <c>ulong</c>, also with a public implicit conversion. Reflection is only needed to read the
    ///   internal member itself; the value, once read, converts to the raw handle type
    ///   <see cref="VkSkiaSurfaceFactory"/> wants via that public operator, no further reflection
    ///   needed.
    /// - Stride always creates its single graphics queue from queue family index 0 (see
    ///   <c>GraphicsDevice.Vulkan.cs</c>'s <c>InitializePlatformDevice</c>: the queue-create-info's
    ///   <c>queueFamilyIndex</c> is a literal <c>0</c>, and <c>vkGetDeviceQueue</c> is called with a
    ///   literal <c>0</c> too) - there is no separate "family index" member to reflect, unlike the
    ///   instance/device/queue handles themselves.
    /// - Stride hard-throws during device creation unless the physical device supports Vulkan 1.3
    ///   (dynamic rendering + synchronization2) and the 1.2 timeline-semaphore feature (see
    ///   <c>GraphicsDevice.Vulkan.cs</c>), so <see cref="VkSkiaSurfaceFactory.InitializeFromNative"/>'s
    ///   <c>apiVersion</c> is hardcoded to Vulkan 1.3 here rather than reflected from Stride's
    ///   adapter - it is always a safe floor to report to Skia.
    /// - <b>The TRANSFER_SRC/TRANSFER_DST image-usage-flags landmine Core.VK's own doc comment warns
    ///   about (repo issue #23/#54) is a non-issue for Stride.</b> Skia's Vulkan backend requires
    ///   both bits on any wrapped image; Stride's <c>Texture.Vulkan.cs</c> <c>CreateImage</c> sets
    ///   <c>createInfo.usage |= VkImageUsageFlags.TransferSrc | VkImageUsageFlags.TransferDst</c>
    ///   UNCONDITIONALLY on every image it creates, texture or render target alike, so no extra flags
    ///   need to be requested at the Stride API level - <see cref="SkiaStrideVulkanTarget"/> creates
    ///   its texture via a plain <c>Texture.New2D(..., TextureFlags.RenderTarget |
    ///   TextureFlags.ShaderResource)</c>, identical to the D3D11 adapter's <c>SkiaStrideTarget</c>.
    ///   <see cref="ComputeImageUsageFlags"/> mirrors that same unconditional-plus-flags computation
    ///   to report the resulting value back to <see cref="VkSkiaSurfaceFactory.CreateTextureState"/>
    ///   (Stride does not expose the actual flags used back on <see cref="Texture"/> itself).
    /// - <c>instanceExtensions</c>/<c>deviceExtensions</c> are passed as empty arrays rather than
    ///   reflecting Stride's actual enabled set (which Stride does not expose as a queryable list
    ///   after device creation - only the raw create-info locals used once during
    ///   <c>InitializePlatformDevice</c>). This is deliberately the same minimal surface
    ///   <c>tests/Tests.Core.VK/VulkanTestDevice.cs</c> already proves works end-to-end headlessly:
    ///   Skia's 2D draw path never presents anything itself (Stride owns presentation), so it has no
    ///   need for the swapchain/surface extensions Stride enables for ITS OWN presentation.
    /// </summary>
    internal sealed class SkiaStrideVulkanContext : IDisposable
    {
        const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        readonly VkSkiaSurfaceFactory _factory = new();
        CommandList? _handoffCommandList;
        GraphicsDevice? _graphicsDevice;

        static PropertyInfo? _nativeInstanceProperty;
        static PropertyInfo? _nativePhysicalDeviceProperty;
        static PropertyInfo? _nativeDeviceProperty;
        static FieldInfo? _nativeCommandQueueField;
        static FieldInfo? _queueLockField;
        static FieldInfo? _nativeImageField;
        static FieldInfo? _nativeLayoutField;
        static FieldInfo? _nativeFormatField;
        static PropertyInfo? _parentTextureProperty;
        static FieldInfo? _activePipelineField;
        static Action<CommandList, bool>? _flushInternal;

        internal GRContext GRContext => _factory.GRContext;

        internal void Initialize(GraphicsDevice graphicsDevice)
        {
            _nativeInstanceProperty ??= typeof(GraphicsDevice).GetProperty("NativeInstance", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.NativeInstance.");
            _nativePhysicalDeviceProperty ??= typeof(GraphicsDevice).GetProperty("NativePhysicalDevice", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.NativePhysicalDevice.");
            _nativeDeviceProperty ??= typeof(GraphicsDevice).GetProperty("NativeDevice", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.NativeDevice.");
            _nativeCommandQueueField ??= typeof(GraphicsDevice).GetField("NativeCommandQueue", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.NativeCommandQueue.");
            _queueLockField ??= typeof(GraphicsDevice).GetField("QueueLock", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.QueueLock.");

            var instance = (VkInstance)_nativeInstanceProperty.GetValue(graphicsDevice)!;
            var physicalDevice = (VkPhysicalDevice)_nativePhysicalDeviceProperty.GetValue(graphicsDevice)!;
            var device = (VkDevice)_nativeDeviceProperty.GetValue(graphicsDevice)!;
            var queue = (VkQueue)_nativeCommandQueueField.GetValue(graphicsDevice)!;
            var queueLock = _queueLockField.GetValue(graphicsDevice)
                ?? throw new Exception("Stride GraphicsDevice.QueueLock is null.");

            if ((IntPtr)instance == IntPtr.Zero)
                throw new Exception("Stride GraphicsDevice.NativeInstance is null.");
            if ((IntPtr)physicalDevice == IntPtr.Zero)
                throw new Exception("Stride GraphicsDevice.NativePhysicalDevice is null.");
            if ((IntPtr)device == IntPtr.Zero)
                throw new Exception("Stride GraphicsDevice.NativeDevice is null.");
            if ((IntPtr)queue == IntPtr.Zero)
                throw new Exception("Stride GraphicsDevice.NativeCommandQueue is null.");

            _factory.InitializeFromNative(
                (IntPtr)instance, (IntPtr)physicalDevice, (IntPtr)device, (IntPtr)queue,
                graphicsQueueFamilyIndex: 0,
                apiVersion: VkConstants.MakeApiVersion(1, 3), // see this class's doc comment
                instanceExtensions: [],
                deviceExtensions: [],
                acquireQueueLock: () => AcquireQueueLock(queueLock));

            _graphicsDevice = graphicsDevice;
            _handoffCommandList = CommandList.New(graphicsDevice);
        }

        static IDisposable AcquireQueueLock(object queueLock)
        {
            Monitor.Enter(queueLock);
            return new QueueLockRelease(queueLock);
        }

        sealed class QueueLockRelease(object queueLock) : IDisposable
        {
            public void Dispose() => Monitor.Exit(queueLock);
        }

        internal void BeginDraw() => _factory.BeginDraw();

        internal void EndDraw() => _factory.EndDraw();

        /// <summary>
        /// Moves <paramref name="texture"/> back to <c>COLOR_ATTACHMENT_OPTIMAL</c> through Stride's own
        /// barrier, so Stride's layout tracking and Skia's belief agree before Skia draws. Skia keeps
        /// believing the image is in the layout it left it in (<c>COLOR_ATTACHMENT_OPTIMAL</c>, see
        /// <see cref="VkSkiaSurfaceFactory.EndDraw(bool)"/>), while the composite moves it to
        /// <c>SHADER_READ_ONLY_OPTIMAL</c>. Submitted immediately, so call after <see cref="BeginDraw"/>
        /// and before Skia records anything.
        /// </summary>
        internal void AcquireForSkia(Texture texture)
        {
            var commandList = _handoffCommandList!;
            commandList.Reset();
            commandList.ResourceBarrierTransition(texture, BarrierLayout.RenderTarget);
            _graphicsDevice!.ExecuteCommandList(commandList.Close());
        }

        /// <summary>
        /// Starts a pass that draws into the render target bound on <paramref name="commandList"/>. Vulkan has
        /// no bound-target query, so the caller passes the texture Stride has bound. Stride submits what it has
        /// recorded at the end of its command list, but Skia submits straight away, so the list is flushed
        /// first: that is what puts Skia's pixels over everything Stride drew before this call (and the flush is
        /// the cost of the pass). The image is then moved to <c>COLOR_ATTACHMENT_OPTIMAL</c> through Stride's own
        /// barrier, as <see cref="AcquireForSkia"/> does, and wrapped in a surface that lives for this pass only,
        /// because a swap chain hands Stride a different image under the same <see cref="Texture"/> every frame.
        /// Validation comes first, so a target Skia cannot draw into throws before anything has been flushed.
        /// </summary>
        internal SKCanvas BeginDirectDraw(CommandList commandList, Texture target, out DirectPass pass)
        {
            var (colorType, viewFormat) = GetDirectFormat(target);

            SubmitRecordedCommands(commandList);
            AcquireForSkia(target);

            BeginDraw();
            try
            {
                var state = CreateTextureState(GetImageOwner(target), DirectUsageFlags, viewFormat);
                var (surface, renderTarget) = CreateSurface(state, target.ViewWidth, target.ViewHeight, colorType);
                pass = new DirectPass(commandList, target, surface, renderTarget);
                return surface.Canvas;
            }
            catch
            {
                EndDraw();
                commandList.ResourceBarrierTransition(target, BarrierLayout.RenderTarget);
                throw;
            }
        }

        /// <summary>
        /// Submits Skia's work and hands the target back to Stride. Stride's tracker already has the image in
        /// <c>COLOR_ATTACHMENT_OPTIMAL</c> (where Skia leaves it), so the barrier recorded on Stride's list is
        /// a layout no-op that makes Skia's color writes available to Stride's next draw into the target.
        /// Hands it back even if the submit throws.
        /// </summary>
        internal void EndDirectDraw(DirectPass pass)
        {
            try
            {
                pass.Surface.Flush();
                _factory.EndDraw(synchronous: false);
            }
            finally
            {
                pass.Surface.Dispose();
                pass.RenderTarget.Dispose();
                pass.CommandList.ResourceBarrierTransition(pass.Target, BarrierLayout.RenderTarget);
            }
        }

        /// <summary>One <see cref="BeginDirectDraw"/> pass.</summary>
        internal sealed class DirectPass(CommandList commandList, Texture target, SKSurface surface, GRBackendRenderTarget renderTarget)
        {
            internal CommandList CommandList { get; } = commandList;
            internal Texture Target { get; } = target;
            internal SKSurface Surface { get; } = surface;
            internal GRBackendRenderTarget RenderTarget { get; } = renderTarget;
        }

        // TRANSFER_SRC | TRANSFER_DST | COLOR_ATTACHMENT. Stride creates its own images with the transfer bits
        // always, and its swap chain with TRANSFER_DST and, when the surface supports it, TRANSFER_SRC.
        const uint DirectUsageFlags =
            VkConstants.ImageUsageTransferSrc | VkConstants.ImageUsageTransferDst | VkConstants.ImageUsageColorAttachment;

        /// <summary>
        /// The color type Skia draws <paramref name="target"/> as and the <c>VkFormat</c> it is told the image
        /// has, or <see cref="NotSupportedException"/> for a target it cannot draw into: multisampled, or any
        /// format but 8-bit RGBA or BGRA, UNORM or sRGB. Skia cannot wrap an sRGB-format image, and the swap
        /// chain Stride creates in its default Linear color space is exactly that, so an sRGB image is wrapped
        /// as its UNORM twin (same texel layout): Skia then writes the bytes as given, so an
        /// <see cref="SKColor"/> lands as that color, as on every other backend. Stride creates the image
        /// without <c>VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT</c>, so the UNORM view is outside the spec's
        /// letter, which drivers accept for same-size formats.
        /// </summary>
        static (SKColorType colorType, VkFormat viewFormat) GetDirectFormat(Texture target)
        {
            if (target.MultisampleCount != MultisampleCount.None)
                throw new NotSupportedException(
                    $"SkiaDirectCanvas needs a single-sample target, not {target.MultisampleCount}.");

            _nativeFormatField ??= typeof(Texture).GetField("NativeFormat", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.Texture.NativeFormat.");
            var format = (VkFormat)_nativeFormatField.GetValue(GetImageOwner(target))!;
            return format switch
            {
                VkFormat.R8G8B8A8Unorm or VkFormat.R8G8B8A8Srgb => (SKColorType.Rgba8888, VkFormat.R8G8B8A8Unorm),
                VkFormat.B8G8R8A8Unorm or VkFormat.B8G8R8A8Srgb => (SKColorType.Bgra8888, VkFormat.B8G8R8A8Unorm),
                _ => throw new NotSupportedException(
                    $"SkiaDirectCanvas draws into 8-bit RGBA or BGRA targets, not {format}."),
            };
        }

        static Texture GetImageOwner(Texture texture)
        {
            _parentTextureProperty ??= typeof(Texture).GetProperty("ParentTexture", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.Texture.ParentTexture.");
            return (Texture?)_parentTextureProperty.GetValue(texture) ?? texture;
        }

        /// <summary>
        /// Submits everything <paramref name="commandList"/> has recorded and starts a new recording, which is
        /// what Stride itself does mid-frame before a read-back. The public <c>CommandList.Flush</c> only closes
        /// and submits, leaving a list nothing can record into; the private <c>FlushInternal(false)</c> also
        /// resets it, which marks its viewport, scissors and render targets to be applied again. It drops the
        /// bound pipeline, so that is put back for a caller who draws again without setting it.
        /// </summary>
        static void SubmitRecordedCommands(CommandList commandList)
        {
            _flushInternal ??= CreateFlushInternal();
            _activePipelineField ??= typeof(CommandList).GetField("activePipeline", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.CommandList.activePipeline.");

            var pipeline = _activePipelineField.GetValue(commandList);
            _flushInternal(commandList, false);
            _activePipelineField.SetValue(commandList, pipeline);
        }

        static Action<CommandList, bool> CreateFlushInternal()
        {
            var method = typeof(CommandList).GetMethod("FlushInternal", NonPublicInstance, [typeof(bool)])
                ?? throw new Exception("Could not find Stride.Graphics.CommandList.FlushInternal(bool).");
            return (Action<CommandList, bool>)Delegate.CreateDelegate(typeof(Action<CommandList, bool>), method);
        }

        internal VkTextureState CreateTextureState(Texture texture, uint? usageFlags = null, VkFormat? viewFormat = null)
        {
            _nativeImageField ??= typeof(Texture).GetField("NativeImage", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.Texture.NativeImage.");
            _nativeLayoutField ??= typeof(Texture).GetField("NativeLayout", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.Texture.NativeLayout.");
            _nativeFormatField ??= typeof(Texture).GetField("NativeFormat", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.Texture.NativeFormat.");

            var image = (VkImage)_nativeImageField.GetValue(texture)!;
            var layout = (VkImageLayout)_nativeLayoutField.GetValue(texture)!;
            var format = (VkFormat)_nativeFormatField.GetValue(texture)!;
            format = viewFormat ?? format;

            if ((ulong)image == 0)
                throw new Exception($"Vulkan image is null on Texture ({texture.Width}x{texture.Height}).");

            return _factory.CreateTextureState(
                (ulong)image, (uint)format, (uint)layout, usageFlags ?? ComputeImageUsageFlags(texture.Flags),
                imageTiling: 0 /* VK_IMAGE_TILING_OPTIMAL - matches Texture.Vulkan.cs's CreateImage, which always uses Optimal tiling. */);
        }

        /// <summary>
        /// Mirrors Stride's own <c>VkImageUsageFlags</c> computation in <c>Texture.Vulkan.cs</c>'s
        /// <c>CreateImage</c> (<c>createInfo.usage |= ...</c>). Stride does not expose the flags it
        /// actually used back on <see cref="Texture"/>, so this recomputes them from the same
        /// <see cref="TextureFlags"/> mapping: <c>TransferSrc</c>/<c>TransferDst</c> are
        /// unconditional on every image Stride creates (see this class's doc comment on the landmine
        /// this answers), <c>ColorAttachment</c> follows from <see cref="TextureFlags.RenderTarget"/>
        /// and <c>Sampled</c> from <see cref="TextureFlags.ShaderResource"/> - the two flags
        /// <see cref="SkiaStrideVulkanTarget"/> always requests.
        /// </summary>
        static uint ComputeImageUsageFlags(TextureFlags flags)
        {
            uint usage = VkConstants.ImageUsageTransferSrc | VkConstants.ImageUsageTransferDst;
            if ((flags & TextureFlags.RenderTarget) != 0)
                usage |= VkConstants.ImageUsageColorAttachment;
            if ((flags & TextureFlags.ShaderResource) != 0)
                usage |= VkConstants.ImageUsageSampled;
            return usage;
        }

        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            VkTextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null) =>
            _factory.CreateSurface(state, width, height, colorType, colorSpace);

        public void Dispose()
        {
            _handoffCommandList?.Dispose();
            _handoffCommandList = null;
            _factory.Dispose();
        }
    }
}
