using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.VK
{
    /// <summary>
    /// A <c>VkImage</c> and the <c>VkDeviceMemory</c> bound to it, created by
    /// <see cref="VkImageAllocator.Create"/> and released with <see cref="VkImageAllocator.Destroy"/>.
    /// </summary>
    public readonly record struct VkImageAllocation(ulong Image, ulong Memory, uint Format, uint UsageFlags);

    /// <summary>
    /// Creates a device-local 2D <c>VkImage</c> on a host-supplied <c>VkDevice</c>, for hosts that hand
    /// Skia an image they did not get from the engine (MonoGame's native DesktopVK platform wraps an
    /// image the caller allocates, via <c>RenderTarget2D.FromNativeHandle</c>). Every other consumer
    /// of <see cref="VkSkiaSurfaceFactory"/> wraps an image the engine already owns and never needs
    /// this. Entry points are resolved through the loader like the rest of Core.VK; there is no
    /// Vulkan binding dependency.
    ///
    /// The image is created with <c>TRANSFER_SRC | TRANSFER_DST | SAMPLED | COLOR_ATTACHMENT</c>:
    /// Skia requires both transfer bits on any wrapped image (see
    /// <see cref="VkSkiaSurfaceFactory.CreateTextureState"/>), and the other two let the host sample it
    /// and Skia draw into it. It is created in <c>VK_IMAGE_LAYOUT_UNDEFINED</c>; moving it to the layout
    /// the host expects is the caller's job (see <see cref="VkImageLayoutTransitioner"/>).
    /// </summary>
    public static unsafe class VkImageAllocator
    {
        const uint VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO = 5;
        const uint VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO = 14;
        const uint VK_IMAGE_TYPE_2D = 1;
        const uint VK_SAMPLE_COUNT_1_BIT = 1;
        const uint VK_IMAGE_TILING_OPTIMAL = 0;
        const uint VK_SHARING_MODE_EXCLUSIVE = 0;
        const uint VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT = 0x1;
        const int VK_SUCCESS = 0;

        public const uint ImageUsageFlags =
            VkConstants.ImageUsageTransferSrc | VkConstants.ImageUsageTransferDst |
            VkConstants.ImageUsageSampled | VkConstants.ImageUsageColorAttachment;

        /// <summary>
        /// Creates a <paramref name="width"/> x <paramref name="height"/> image of
        /// <paramref name="format"/> (a <c>VkFormat</c>) in device-local memory.
        /// </summary>
        public static VkImageAllocation Create(
            IntPtr instance, IntPtr physicalDevice, IntPtr device, int width, int height, uint format)
        {
            if (instance == IntPtr.Zero)
                throw new ArgumentException("Vulkan instance native pointer is null.", nameof(instance));
            if (physicalDevice == IntPtr.Zero)
                throw new ArgumentException("Vulkan physical device native pointer is null.", nameof(physicalDevice));
            if (device == IntPtr.Zero)
                throw new ArgumentException("Vulkan device native pointer is null.", nameof(device));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

            var vkCreateImage = (delegate* unmanaged<IntPtr, VkImageCreateInfo*, IntPtr, ulong*, int>)VulkanNative.RequireDeviceProc(device, "vkCreateImage");
            var vkDestroyImage = (delegate* unmanaged<IntPtr, ulong, IntPtr, void>)VulkanNative.RequireDeviceProc(device, "vkDestroyImage");
            var vkGetImageMemoryRequirements = (delegate* unmanaged<IntPtr, ulong, VkMemoryRequirements*, void>)VulkanNative.RequireDeviceProc(device, "vkGetImageMemoryRequirements");
            var vkAllocateMemory = (delegate* unmanaged<IntPtr, VkMemoryAllocateInfo*, IntPtr, ulong*, int>)VulkanNative.RequireDeviceProc(device, "vkAllocateMemory");
            var vkFreeMemory = (delegate* unmanaged<IntPtr, ulong, IntPtr, void>)VulkanNative.RequireDeviceProc(device, "vkFreeMemory");
            var vkBindImageMemory = (delegate* unmanaged<IntPtr, ulong, ulong, ulong, int>)VulkanNative.RequireDeviceProc(device, "vkBindImageMemory");
            var vkGetPhysicalDeviceMemoryProperties = (delegate* unmanaged<IntPtr, VkPhysicalDeviceMemoryProperties*, void>)
                VulkanNative.vkGetInstanceProcAddr(instance, "vkGetPhysicalDeviceMemoryProperties");
            if (vkGetPhysicalDeviceMemoryProperties == null)
                throw new EntryPointNotFoundException("vkGetInstanceProcAddr could not resolve 'vkGetPhysicalDeviceMemoryProperties'.");

            var createInfo = new VkImageCreateInfo
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
                imageType = VK_IMAGE_TYPE_2D,
                format = format,
                extent = new VkExtent3D { width = (uint)width, height = (uint)height, depth = 1 },
                mipLevels = 1,
                arrayLayers = 1,
                samples = VK_SAMPLE_COUNT_1_BIT,
                tiling = VK_IMAGE_TILING_OPTIMAL,
                usage = ImageUsageFlags,
                sharingMode = VK_SHARING_MODE_EXCLUSIVE,
                initialLayout = VkConstants.ImageLayoutUndefined,
            };

            ulong image;
            Check(vkCreateImage(device, &createInfo, IntPtr.Zero, &image), "vkCreateImage");

            try
            {
                VkMemoryRequirements requirements;
                vkGetImageMemoryRequirements(device, image, &requirements);

                VkPhysicalDeviceMemoryProperties memoryProperties;
                vkGetPhysicalDeviceMemoryProperties(physicalDevice, &memoryProperties);

                uint memoryType = uint.MaxValue;
                for (uint i = 0; i < memoryProperties.memoryTypeCount; i++)
                {
                    if ((requirements.memoryTypeBits & (1u << (int)i)) != 0 &&
                        (memoryProperties.memoryTypes[(int)i].propertyFlags & VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT) != 0)
                    {
                        memoryType = i;
                        break;
                    }
                }
                if (memoryType == uint.MaxValue)
                    throw new InvalidOperationException("No device-local Vulkan memory type is compatible with the image.");

                var allocateInfo = new VkMemoryAllocateInfo
                {
                    sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                    allocationSize = requirements.size,
                    memoryTypeIndex = memoryType,
                };

                ulong memory;
                Check(vkAllocateMemory(device, &allocateInfo, IntPtr.Zero, &memory), "vkAllocateMemory");

                try
                {
                    Check(vkBindImageMemory(device, image, memory, 0), "vkBindImageMemory");
                }
                catch
                {
                    vkFreeMemory(device, memory, IntPtr.Zero);
                    throw;
                }

                return new VkImageAllocation(image, memory, format, ImageUsageFlags);
            }
            catch
            {
                vkDestroyImage(device, image, IntPtr.Zero);
                throw;
            }
        }

        /// <summary>
        /// Destroys the image and frees its memory. The GPU must be done with the image first.
        /// </summary>
        public static void Destroy(IntPtr device, VkImageAllocation allocation)
        {
            if (device == IntPtr.Zero || allocation.Image == 0)
                return;

            var vkDestroyImage = (delegate* unmanaged<IntPtr, ulong, IntPtr, void>)VulkanNative.RequireDeviceProc(device, "vkDestroyImage");
            var vkFreeMemory = (delegate* unmanaged<IntPtr, ulong, IntPtr, void>)VulkanNative.RequireDeviceProc(device, "vkFreeMemory");
            vkDestroyImage(device, allocation.Image, IntPtr.Zero);
            if (allocation.Memory != 0)
                vkFreeMemory(device, allocation.Memory, IntPtr.Zero);
        }

        static void Check(int result, string call)
        {
            if (result != VK_SUCCESS)
                throw new InvalidOperationException($"{call} failed. VkResult: {result}");
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VkExtent3D { public uint width, height, depth; }

        [StructLayout(LayoutKind.Sequential)]
        struct VkImageCreateInfo
        {
            public uint sType;
            public IntPtr pNext;
            public uint flags;
            public uint imageType;
            public uint format;
            public VkExtent3D extent;
            public uint mipLevels;
            public uint arrayLayers;
            public uint samples;
            public uint tiling;
            public uint usage;
            public uint sharingMode;
            public uint queueFamilyIndexCount;
            public IntPtr pQueueFamilyIndices;
            public uint initialLayout;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VkMemoryRequirements
        {
            public ulong size;
            public ulong alignment;
            public uint memoryTypeBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VkMemoryType { public uint propertyFlags; public uint heapIndex; }

        [StructLayout(LayoutKind.Sequential)]
        struct VkMemoryHeap { public ulong size; public uint flags; }

        [InlineArray(32)]
        struct VkMemoryTypeArray32 { VkMemoryType _element0; }

        [InlineArray(16)]
        struct VkMemoryHeapArray16 { VkMemoryHeap _element0; }

        [StructLayout(LayoutKind.Sequential)]
        struct VkPhysicalDeviceMemoryProperties
        {
            public uint memoryTypeCount;
            public VkMemoryTypeArray32 memoryTypes;
            public uint memoryHeapCount;
            public VkMemoryHeapArray16 memoryHeaps;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct VkMemoryAllocateInfo
        {
            public uint sType;
            public IntPtr pNext;
            public ulong allocationSize;
            public uint memoryTypeIndex;
        }
    }
}
