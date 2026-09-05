using System.Reflection;
using System.Threading;
using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Stride.Graphics;
using Vortice.Vulkan;

namespace SkiaGameRendering.Stride.VK
{
    /// <summary>
    /// Stride-specific adapter over <see cref="VkSkiaSurfaceFactory"/> (see that class for how the
    /// Vulkan interop itself works). This class's only job is pulling Stride's Vulkan
    /// instance/physical-device/device/queue handles and handing them to the shared factory, and
    /// wiring Stride's own queue lock through so Skia's and Stride's <c>vkQueueSubmit</c> calls stay
    /// externally synchronized on the shared <c>VkQueue</c>, per the Vulkan spec.
    ///
    /// MAINTENANCE NOTES:
    /// - Targets Stride 4.4.0-beta5+, built with <c>StrideGraphicsApi=Vulkan</c> downstream (see
    ///   SkiaGameRendering.Stride.VK.csproj's TFM comment). Unlike
    ///   <c>SkiaGameRendering.Stride</c>'s D3D11 adapter, EVERY Vulkan handle this class needs -
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

        static PropertyInfo? _nativeInstanceProperty;
        static PropertyInfo? _nativePhysicalDeviceProperty;
        static PropertyInfo? _nativeDeviceProperty;
        static FieldInfo? _nativeCommandQueueField;
        static FieldInfo? _queueLockField;
        static FieldInfo? _nativeImageField;
        static FieldInfo? _nativeLayoutField;
        static FieldInfo? _nativeFormatField;

        /// <summary><c>VK_MAKE_API_VERSION(0, 1, 3, 0)</c> - see this class's doc comment.</summary>
        const uint VK_API_VERSION_1_3 = (1u << 22) | (3u << 12);

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
                apiVersion: VK_API_VERSION_1_3,
                instanceExtensions: [],
                deviceExtensions: [],
                acquireQueueLock: () => AcquireQueueLock(queueLock));
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

        internal VkTextureState CreateTextureState(Texture texture)
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

            if ((ulong)image == 0)
                throw new Exception($"Vulkan image is null on Texture ({texture.Width}x{texture.Height}).");

            return _factory.CreateTextureState(
                (ulong)image, (uint)format, (uint)layout, ComputeImageUsageFlags(texture.Flags),
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
            const uint VK_IMAGE_USAGE_TRANSFER_SRC_BIT = 0x1;
            const uint VK_IMAGE_USAGE_TRANSFER_DST_BIT = 0x2;
            const uint VK_IMAGE_USAGE_SAMPLED_BIT = 0x4;
            const uint VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT = 0x10;

            uint usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            if ((flags & TextureFlags.RenderTarget) != 0)
                usage |= VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
            if ((flags & TextureFlags.ShaderResource) != 0)
                usage |= VK_IMAGE_USAGE_SAMPLED_BIT;
            return usage;
        }

        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            VkTextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null) =>
            _factory.CreateSurface(state, width, height, colorType, colorSpace);

        public void Dispose() => _factory.Dispose();
    }
}
