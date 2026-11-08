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
    /// </summary>
    public sealed class SkiaVulkanContext : IDisposable
    {
        readonly VkSkiaSurfaceFactory _factory = new();
        readonly VkImageLayoutTransitioner _transitioner;
        bool _disposed;

        public SkiaVulkanContext(
            IntPtr instance, IntPtr physicalDevice, IntPtr device, IntPtr queue, uint queueFamilyIndex,
            Func<IDisposable>? acquireQueueLock = null)
        {
            Instance = instance;
            PhysicalDevice = physicalDevice;
            Device = device;

            // Skia's 2D path never presents, so it needs none of the swapchain extensions (same as Stride.VK).
            _factory.InitializeFromNative(
                instance, physicalDevice, device, queue, queueFamilyIndex,
                apiVersion: VkConstants.MakeApiVersion(1, 3),
                instanceExtensions: [], deviceExtensions: [],
                acquireQueueLock: acquireQueueLock);
            _transitioner = new VkImageLayoutTransitioner(device, queue, queueFamilyIndex, acquireQueueLock);
        }

        internal IntPtr Instance { get; }
        internal IntPtr PhysicalDevice { get; }
        internal IntPtr Device { get; }
        internal VkSkiaSurfaceFactory Factory => _factory;
        internal VkImageLayoutTransitioner Transitioner => _transitioner;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _transitioner.WaitForCompletion();
            _transitioner.Dispose();
            _factory.Dispose();
        }
    }
}
