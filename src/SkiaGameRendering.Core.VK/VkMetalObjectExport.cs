using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.VK
{
    /// <summary>
    /// <c>VK_EXT_metal_objects</c> on MoltenVK: exports the <c>id&lt;MTLDevice&gt;</c>, the
    /// <c>id&lt;MTLCommandQueue&gt;</c> behind a <c>VkQueue</c>, the <c>id&lt;MTLTexture&gt;</c> behind a
    /// <c>VkImage</c> and the <c>id&lt;MTLSharedEvent&gt;</c> behind a timeline <c>VkSemaphore</c>, so Skia's Metal
    /// backend can draw into a Vulkan app's image. SkiaSharp's macOS native has no Vulkan backend, so this is how a
    /// Vulkan host gets GPU Skia on macOS.
    /// <para>
    /// The app enables <see cref="ExtensionName"/> on its <c>VkDevice</c> and chains a
    /// <see cref="ExportMetalObjectCreateInfo"/> for <see cref="ExportObjectTypeMetalDevice"/> (and, to export the
    /// queue, one for <see cref="ExportObjectTypeMetalCommandQueue"/>) on its <c>VkInstanceCreateInfo</c>. An image
    /// to export is created with one for <see cref="ExportObjectTypeMetalTexture"/>
    /// (<see cref="VkImageAllocator.Create"/>'s <c>pNext</c>), a semaphore with one for
    /// <see cref="ExportObjectTypeMetalSharedEvent"/>.
    /// </para>
    /// <para>
    /// The exported objects are not retained; they live as long as the Vulkan object they came from.
    /// </para>
    /// </summary>
    public static unsafe class VkMetalObjectExport
    {
        public const string ExtensionName = "VK_EXT_metal_objects";

        public const uint StructureTypeExportMetalObjectCreateInfo = 1000311000;
        public const uint StructureTypeExportMetalObjectsInfo = 1000311001;
        public const uint StructureTypeExportMetalDeviceInfo = 1000311002;
        public const uint StructureTypeExportMetalCommandQueueInfo = 1000311003;
        public const uint StructureTypeExportMetalTextureInfo = 1000311006;
        public const uint StructureTypeExportMetalSharedEventInfo = 1000311010;

        public const uint ExportObjectTypeMetalDevice = 0x1;
        public const uint ExportObjectTypeMetalCommandQueue = 0x2;
        public const uint ExportObjectTypeMetalTexture = 0x8;
        public const uint ExportObjectTypeMetalSharedEvent = 0x20;

        const uint ImageAspectPlane0 = 0x10;

        /// <summary>The environment variable MoltenVK reads for its <c>synchronousQueueSubmits</c> setting.</summary>
        public const string SynchronousQueueSubmitsVariable = "MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS";

        /// <summary><c>VkExportMetalObjectCreateInfoEXT</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ExportMetalObjectCreateInfo
        {
            public uint sType;
            public IntPtr pNext;
            public uint exportObjectType;

            public static ExportMetalObjectCreateInfo For(uint exportObjectType, IntPtr next = default) => new()
            {
                sType = StructureTypeExportMetalObjectCreateInfo,
                pNext = next,
                exportObjectType = exportObjectType,
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ExportMetalObjectsInfo
        {
            public uint sType;
            public IntPtr pNext;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ExportMetalDeviceInfo
        {
            public uint sType;
            public IntPtr pNext;
            public IntPtr mtlDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ExportMetalCommandQueueInfo
        {
            public uint sType;
            public IntPtr pNext;
            public IntPtr queue;
            public IntPtr mtlCommandQueue;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ExportMetalTextureInfo
        {
            public uint sType;
            public IntPtr pNext;
            public ulong image;
            public ulong imageView;
            public ulong bufferView;
            public uint plane;
            public IntPtr mtlTexture;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ExportMetalSharedEventInfo
        {
            public uint sType;
            public IntPtr pNext;
            public ulong semaphore;
            public ulong @event;
            public IntPtr mtlSharedEvent;
        }

        /// <summary>The <c>id&lt;MTLDevice&gt;</c> behind <paramref name="device"/>.</summary>
        public static IntPtr ExportDevice(IntPtr device)
        {
            var info = new ExportMetalDeviceInfo { sType = StructureTypeExportMetalDeviceInfo };
            Export(device, &info);
            return Require(info.mtlDevice, "MTLDevice");
        }

        /// <summary>The <c>id&lt;MTLCommandQueue&gt;</c> MoltenVK submits <paramref name="queue"/>'s work to.</summary>
        public static IntPtr ExportCommandQueue(IntPtr device, IntPtr queue)
        {
            var info = new ExportMetalCommandQueueInfo { sType = StructureTypeExportMetalCommandQueueInfo, queue = queue };
            Export(device, &info);
            return Require(info.mtlCommandQueue, "MTLCommandQueue");
        }

        /// <summary>The <c>id&lt;MTLTexture&gt;</c> behind <paramref name="image"/>, a single-plane image.</summary>
        public static IntPtr ExportTexture(IntPtr device, ulong image)
        {
            var info = new ExportMetalTextureInfo { sType = StructureTypeExportMetalTextureInfo, image = image, plane = ImageAspectPlane0 };
            Export(device, &info);
            return Require(info.mtlTexture, "MTLTexture");
        }

        /// <summary>
        /// The <c>id&lt;MTLSharedEvent&gt;</c> behind <paramref name="semaphore"/>, a timeline semaphore created with a
        /// <see cref="ExportMetalObjectCreateInfo"/> for <see cref="ExportObjectTypeMetalSharedEvent"/>. Its
        /// <c>signaledValue</c> is the semaphore's counter, so Metal command buffers can wait on and signal it.
        /// </summary>
        public static IntPtr ExportSharedEvent(IntPtr device, ulong semaphore)
        {
            var info = new ExportMetalSharedEventInfo { sType = StructureTypeExportMetalSharedEventInfo, semaphore = semaphore };
            Export(device, &info);
            return Require(info.mtlSharedEvent, "MTLSharedEvent");
        }

        /// <summary>
        /// Whether <see cref="SynchronousQueueSubmitsVariable"/> turns MoltenVK's synchronous queue submits off, so
        /// <c>vkQueueSubmit</c> may commit its Metal work after it returns. Only the environment variable is visible
        /// here: MoltenVK reports its global settings, not an instance's <c>VK_EXT_layer_settings</c>.
        /// </summary>
        public static bool AsynchronousQueueSubmits() =>
            AsynchronousQueueSubmits(Environment.GetEnvironmentVariable(SynchronousQueueSubmitsVariable));

        internal static bool AsynchronousQueueSubmits(string? value)
        {
            if (value == null)
                return false;
            var trimmed = value.Trim();
            return trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)
                || (long.TryParse(trimmed, out var number) && number == 0);
        }

        internal static IntPtr RequireExportProc(IntPtr proc) => proc != IntPtr.Zero
            ? proc
            : throw new InvalidOperationException(
                $"vkExportMetalObjectsEXT is unavailable: enable the {ExtensionName} device extension on the VkDevice " +
                "(MoltenVK returns no entry point without it), and chain VkExportMetalObjectCreateInfoEXT for the MTLDevice " +
                "and MTLCommandQueue on the VkInstanceCreateInfo.");

        static void Export(IntPtr device, void* exportInfo)
        {
            if (device == IntPtr.Zero)
                throw new ArgumentException("Vulkan device native pointer is null.", nameof(device));
            var export = (delegate* unmanaged<IntPtr, ExportMetalObjectsInfo*, void>)
                RequireExportProc(VulkanNative.vkGetDeviceProcAddr(device, "vkExportMetalObjectsEXT"));
            var objects = new ExportMetalObjectsInfo { sType = StructureTypeExportMetalObjectsInfo, pNext = (IntPtr)exportInfo };
            export(device, &objects);
        }

        static IntPtr Require(IntPtr exported, string what) => exported != IntPtr.Zero
            ? exported
            : throw new InvalidOperationException(
                $"vkExportMetalObjectsEXT returned no {what}. Is this MoltenVK 1.4 or later, with the object created for export?");
    }
}
