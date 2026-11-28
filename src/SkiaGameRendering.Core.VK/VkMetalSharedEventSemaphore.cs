using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.VK
{
    /// <summary>
    /// MoltenVK only: a timeline <c>VkSemaphore</c> exported as its <c>id&lt;MTLSharedEvent&gt;</c>
    /// (<see cref="VkMetalObjectExport.ExportSharedEvent"/>), so Vulkan submits and Metal command buffers on another
    /// <c>MTLCommandQueue</c> can wait on each other's values. The device must have the <c>timelineSemaphore</c>
    /// feature enabled (Vulkan 1.2 core); Vulkan cannot report whether it is, so this checks only that the GPU
    /// supports it, and the validation layer reports a device that did not enable it.
    /// <para>
    /// <see cref="Next"/> hands out each value once, in order. Every value is signaled by exactly one side, after
    /// that side has waited on or signaled the value before it, so the counter only rises. The caller destroys this
    /// only after every submit and command buffer using it has finished.
    /// </para>
    /// </summary>
    public sealed unsafe class VkMetalSharedEventSemaphore : IDisposable
    {
        const uint StructureTypeSemaphoreCreateInfo = 9;
        const uint StructureTypeSemaphoreTypeCreateInfo = 1000207002;
        const uint StructureTypePhysicalDeviceFeatures2 = 1000059000;
        const uint StructureTypePhysicalDeviceTimelineSemaphoreFeatures = 1000207000;
        const uint SemaphoreTypeTimeline = 1;

        readonly IntPtr _device;
        readonly delegate* unmanaged<IntPtr, ulong, IntPtr, void> _vkDestroySemaphore;
        ulong _value;
        bool _disposed;

        public VkMetalSharedEventSemaphore(IntPtr instance, IntPtr physicalDevice, IntPtr device)
        {
            if (device == IntPtr.Zero)
                throw new ArgumentException("Vulkan device native pointer is null.", nameof(device));
            ThrowUnlessTimelineSemaphoresSupported(instance, physicalDevice);

            _device = device;
            var create = (delegate* unmanaged<IntPtr, SemaphoreCreateInfo*, IntPtr, ulong*, int>)VulkanNative.RequireDeviceProc(device, "vkCreateSemaphore");
            _vkDestroySemaphore = (delegate* unmanaged<IntPtr, ulong, IntPtr, void>)VulkanNative.RequireDeviceProc(device, "vkDestroySemaphore");

            var export = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalSharedEvent);
            var type = new SemaphoreTypeCreateInfo { sType = StructureTypeSemaphoreTypeCreateInfo, pNext = (IntPtr)(&export), semaphoreType = SemaphoreTypeTimeline };
            var info = new SemaphoreCreateInfo { sType = StructureTypeSemaphoreCreateInfo, pNext = (IntPtr)(&type) };
            ulong semaphore;
            var result = create(device, &info, IntPtr.Zero, &semaphore);
            if (result != 0)
                throw new InvalidOperationException($"vkCreateSemaphore (timeline, MTLSharedEvent export) failed. VkResult: {result}");
            Semaphore = semaphore;
            try
            {
                SharedEvent = VkMetalObjectExport.ExportSharedEvent(device, semaphore);
            }
            catch
            {
                _vkDestroySemaphore(device, semaphore, IntPtr.Zero);
                throw;
            }
        }

        /// <summary>The raw <c>VkSemaphore</c>.</summary>
        public ulong Semaphore { get; }

        /// <summary>The <c>id&lt;MTLSharedEvent&gt;</c> behind <see cref="Semaphore"/>; not retained, it lives as long as the semaphore.</summary>
        public IntPtr SharedEvent { get; }

        /// <summary>The next value to signal and wait on.</summary>
        public ulong Next() => ++_value;

        static void ThrowUnlessTimelineSemaphoresSupported(IntPtr instance, IntPtr physicalDevice)
        {
            var proc = VulkanNative.vkGetInstanceProcAddr(instance, "vkGetPhysicalDeviceFeatures2");
            if (proc == IntPtr.Zero)
                throw new InvalidOperationException("vkGetPhysicalDeviceFeatures2 is unavailable; the instance must be Vulkan 1.1 or later.");
            var timeline = new PhysicalDeviceTimelineSemaphoreFeatures { sType = StructureTypePhysicalDeviceTimelineSemaphoreFeatures };
            var features = new PhysicalDeviceFeatures2 { sType = StructureTypePhysicalDeviceFeatures2, pNext = (IntPtr)(&timeline) };
            ((delegate* unmanaged<IntPtr, PhysicalDeviceFeatures2*, void>)proc)(physicalDevice, &features);
            if (timeline.timelineSemaphore == 0)
                throw new NotSupportedException(
                    "This GPU's Vulkan driver does not support timeline semaphores, which Skia on MoltenVK uses to order its " +
                    "Metal work against the app's Vulkan submits.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _vkDestroySemaphore(_device, Semaphore, IntPtr.Zero);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SemaphoreCreateInfo
        {
            public uint sType;
            public IntPtr pNext;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SemaphoreTypeCreateInfo
        {
            public uint sType;
            public IntPtr pNext;
            public uint semaphoreType;
            public ulong initialValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PhysicalDeviceTimelineSemaphoreFeatures
        {
            public uint sType;
            public IntPtr pNext;
            public uint timelineSemaphore;
        }

        // VkPhysicalDeviceFeatures2: VkPhysicalDeviceFeatures is 55 VkBool32s.
        [StructLayout(LayoutKind.Sequential)]
        struct PhysicalDeviceFeatures2
        {
            public uint sType;
            public IntPtr pNext;
            public fixed uint features[55];
        }
    }
}
