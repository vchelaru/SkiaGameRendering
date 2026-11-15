using SkiaGameRendering.Core.Metal;
using SkiaGameRendering.Core.VK;

namespace SkiaGameRendering
{
    /// <summary>
    /// Skia's connection to the app's Vulkan device. Create one per <c>VkDevice</c> and share it between
    /// every <see cref="SkiaRenderTarget2D"/>. The handles are the raw ones BRUTAL's wrappers expose as
    /// <c>VkHandle</c> (<c>Instance</c>, <c>PhysicalDevice</c>, <c>Device</c>, <c>Queue</c>).
    /// <code>
    /// var context = new SkiaVulkanContext(
    ///     instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queue.Family);
    /// </code>
    /// The device must be Vulkan 1.3 (BRUTAL's main path) and the queue must support graphics. Skia calls
    /// <c>vkQueueSubmit</c> on <c>queue</c> itself, so submits on that queue from other threads must be
    /// serialized with Skia's: pass the lock the app uses as <c>acquireQueueLock</c>. BRUTAL's own
    /// <c>Queue.Submit</c> lock is private, so an app that submits from several threads needs its own.
    /// <para>
    /// <b>macOS.</b> SkiaSharp's macOS native has no Vulkan, so on MoltenVK Skia draws with Metal instead, into the
    /// <c>MTLTexture</c> behind each image, on the <c>MTLCommandQueue</c> behind <c>queue</c>. The app enables
    /// <c>VK_EXT_metal_objects</c> on the device and chains <c>VkExportMetalObjectCreateInfoEXT</c> for the
    /// MTLDevice and the MTLCommandQueue on the instance (see <c>docs/brutal/quickstart.md</c>). Ordering between
    /// the app's Vulkan work and Skia's relies on MoltenVK committing each <c>vkQueueSubmit</c> on the calling
    /// thread, its default; with <c>MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS=0</c> this constructor throws.
    /// </para>
    /// </summary>
    public sealed class SkiaVulkanContext : IDisposable
    {
        readonly VkSkiaSurfaceFactory? _factory;
        readonly MetalSkiaSurfaceFactory? _metalFactory;
        readonly VkImageLayoutTransitioner _transitioner;
        bool _disposed;

        public SkiaVulkanContext(
            IntPtr instance, IntPtr physicalDevice, IntPtr device, IntPtr queue, uint queueFamilyIndex,
            Func<IDisposable>? acquireQueueLock = null)
        {
            Instance = instance;
            PhysicalDevice = physicalDevice;
            Device = device;

            if (OperatingSystem.IsMacOS())
            {
                VkMetalObjectExport.ThrowIfAsynchronousQueueSubmits();
                _metalFactory = new MetalSkiaSurfaceFactory();
                _metalFactory.InitializeFromNative(
                    VkMetalObjectExport.ExportDevice(device), VkMetalObjectExport.ExportCommandQueue(device, queue));
            }
            else
            {
                // Skia's 2D path never presents, so it needs none of the swapchain extensions (same as Stride.VK).
                _factory = new VkSkiaSurfaceFactory();
                _factory.InitializeFromNative(
                    instance, physicalDevice, device, queue, queueFamilyIndex,
                    apiVersion: VkConstants.MakeApiVersion(1, 3),
                    instanceExtensions: [], deviceExtensions: [],
                    acquireQueueLock: acquireQueueLock);
            }
            _transitioner = new VkImageLayoutTransitioner(device, queue, queueFamilyIndex, acquireQueueLock);
        }

        internal IntPtr Instance { get; }
        internal IntPtr PhysicalDevice { get; }
        internal IntPtr Device { get; }
        internal VkSkiaSurfaceFactory Factory => _factory
            ?? throw new PlatformNotSupportedException("On macOS Skia draws through Metal; this path has no Vulkan Skia context.");
        // Non-null on macOS, where Skia draws through Metal into the images' exported MTLTextures.
        internal MetalSkiaSurfaceFactory? MetalFactory => _metalFactory;
        internal VkImageLayoutTransitioner Transitioner => _transitioner;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _transitioner.WaitForCompletion();
            _transitioner.Dispose();
            _factory?.Dispose();
            _metalFactory?.Dispose();
        }
    }
}
