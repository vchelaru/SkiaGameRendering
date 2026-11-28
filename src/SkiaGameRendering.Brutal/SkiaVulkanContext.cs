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
    /// serialized with Skia's: pass the lock the app uses as <c>acquireQueueLock</c>. It is held only while Skia and
    /// the layout transitions submit (in <c>End</c>), not while the app draws on the canvas. BRUTAL's own
    /// <c>Queue.Submit</c> lock is private, so an app that submits from several threads needs its own.
    /// <para>
    /// <b>macOS.</b> SkiaSharp's macOS native has no Vulkan, so on MoltenVK Skia draws with Metal instead, into the
    /// <c>MTLTexture</c> behind each image. The app enables <c>VK_EXT_metal_objects</c> on the device and chains
    /// <c>VkExportMetalObjectCreateInfoEXT</c> for the MTLDevice and the MTLCommandQueue on the instance (see
    /// <c>docs/brutal/quickstart.md</c>). With MoltenVK's synchronous queue submits, its default, Skia draws on the
    /// <c>MTLCommandQueue</c> behind <c>queue</c>, and submit order on that shared queue orders its work against the
    /// app's. With asynchronous submits MoltenVK may commit a submit after <c>vkQueueSubmit</c> returns, so Skia draws
    /// on an <c>MTLCommandQueue</c> of its own instead, and each pass adds waits on a timeline semaphore exported as an
    /// <c>MTLSharedEvent</c>; that needs the device's <c>timelineSemaphore</c> feature. <c>MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS=0</c>
    /// turns that path on by itself. An app that turns synchronous submits off through <c>VK_EXT_layer_settings</c>
    /// passes <c>asynchronousQueueSubmits: true</c>, since MoltenVK does not report an instance's layer settings;
    /// without it Skia's work races the app's. The parameter does nothing off macOS.
    /// </para>
    /// </summary>
    public sealed class SkiaVulkanContext : IDisposable
    {
        readonly VkSkiaSurfaceFactory? _factory;
        readonly MetalSkiaSurfaceFactory? _metalFactory;
        readonly VkImageLayoutTransitioner _transitioner;
        // macOS with asynchronous submits: Skia's own MTLCommandQueue (retained) and the semaphore that orders it
        // against the app's queue. Null with synchronous submits, where Skia shares the app's MTLCommandQueue.
        readonly IntPtr _metalQueue;
        readonly VkMetalSharedEventSemaphore? _metalOrder;
        bool _disposed;

        public SkiaVulkanContext(
            IntPtr instance, IntPtr physicalDevice, IntPtr device, IntPtr queue, uint queueFamilyIndex,
            Func<IDisposable>? acquireQueueLock = null, bool asynchronousQueueSubmits = false)
        {
            Instance = instance;
            PhysicalDevice = physicalDevice;
            Device = device;

            if (OperatingSystem.IsMacOS())
            {
                var metalDevice = VkMetalObjectExport.ExportDevice(device);
                IntPtr metalQueue;
                if (asynchronousQueueSubmits || VkMetalObjectExport.AsynchronousQueueSubmits())
                {
                    // Not the queue MoltenVK submits the app's work to: a Metal wait there would block the app's own
                    // submit that signals it, whenever MoltenVK commits that submit later.
                    _metalOrder = new VkMetalSharedEventSemaphore(instance, physicalDevice, device);
                    metalQueue = _metalQueue = MetalSkiaSurfaceFactory.NewCommandQueue(metalDevice);
                }
                else
                    metalQueue = VkMetalObjectExport.ExportCommandQueue(device, queue);
                _metalFactory = new MetalSkiaSurfaceFactory();
                _metalFactory.InitializeFromNative(metalDevice, metalQueue);
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

        // macOS with asynchronous submits: the timeline semaphore Begin and End order Skia's Metal queue against the
        // app's queue with. With synchronous submits the shared queue's submit order does that at no extra cost.
        internal bool OrdersMetalWithSemaphore => _metalOrder != null;
        internal ulong MetalOrderSemaphore => _metalOrder!.Semaphore;
        internal ulong NextMetalOrderValue() => _metalOrder!.Next();
        internal void CommitMetalWait(ulong value) => _metalFactory!.CommitWait(_metalOrder!.SharedEvent, value);
        internal void CommitMetalSignal(ulong value) => _metalFactory!.CommitSignal(_metalOrder!.SharedEvent, value);

        /// <summary>macOS, before Skia draws: its Metal work waits for everything submitted on the app's queue so far.</summary>
        internal void OrderMetalAfterQueue()
        {
            var value = NextMetalOrderValue();
            _transitioner.SubmitTimeline(MetalOrderSemaphore, waitValue: 0, signalValue: value);
            CommitMetalWait(value);
        }

        /// <summary>macOS, after Skia's flush: everything submitted on the app's queue from now on waits for Skia's work.</summary>
        internal void OrderQueueAfterMetal()
        {
            var value = NextMetalOrderValue();
            CommitMetalSignal(value);
            _transitioner.SubmitTimeline(MetalOrderSemaphore, waitValue: value, signalValue: 0);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _transitioner.WaitForCompletion();
            _transitioner.Dispose();
            _factory?.Dispose();
            if (_metalFactory != null)
            {
                // Every Metal wait's value was signaled by a submit the transitioner just waited for.
                if (_metalOrder != null)
                    _metalFactory.WaitForQueue();
                _metalFactory.Dispose();
                if (_metalOrder != null)
                {
                    MetalSkiaSurfaceFactory.ReleaseObject(_metalQueue);
                    _metalOrder.Dispose();
                }
            }
        }
    }
}
