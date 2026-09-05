using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.VK
{
    /// <summary>
    /// Resolves the system Vulkan loader - <c>vulkan-1.dll</c> on Windows, <c>libvulkan.so.1</c> on
    /// Linux, <c>libvulkan.dylib</c>/<c>libMoltenVK.dylib</c> on macOS (MoltenVK) - and P/Invokes the
    /// two Vulkan entry points every loader guarantees to export directly:
    /// <c>vkGetInstanceProcAddr</c> and <c>vkGetDeviceProcAddr</c>. Every other Vulkan function,
    /// including the ones Skia's <c>GrVkGpu</c> resolves internally through the
    /// <see cref="SkiaSharp.GRVkGetProcedureAddressDelegate"/> handed to
    /// <see cref="SkiaSharp.GRVkBackendContext"/>, is looked up dynamically through those two - this
    /// is the standard Vulkan loader discipline (see the Vulkan spec's "Command Function Pointers"
    /// section), not something specific to this library.
    ///
    /// Unlike ANGLE's libEGL/libGLESv2 (vendored inside Core.ANGLE's own NuGet package, since ANGLE
    /// is not something the OS or GPU driver ships), the Vulkan loader is a system component - every
    /// Vulkan-capable install already has one, so there is nothing to vendor here. No PackageReference
    /// to a Vulkan interop library either (no Silk.NET.Vulkan, no Vortice.Vulkan) - same "no interop
    /// library on either side" discipline <c>D3D11Com</c> documents for Core.ANGLE, and for the same
    /// reason: different host engines pin different Vulkan binding versions (Stride uses
    /// Vortice.Vulkan internally), and a hard PackageReference here would force NuGet's resolver to
    /// pick one version for every consumer.
    /// </summary>
    internal static class VulkanNative
    {
        const string VulkanLoader = "vulkan-1";

        static VulkanNative()
        {
            NativeLibrary.SetDllImportResolver(typeof(VulkanNative).Assembly, (name, assembly, searchPath) =>
            {
                if (name != VulkanLoader)
                    return IntPtr.Zero;

                if (NativeLibrary.TryLoad("vulkan-1.dll", out var winHandle))
                    return winHandle;

                if (NativeLibrary.TryLoad("libvulkan.so.1", out var linuxHandle))
                    return linuxHandle;

                if (NativeLibrary.TryLoad("libvulkan.dylib", out var macHandle))
                    return macHandle;
                if (NativeLibrary.TryLoad("libMoltenVK.dylib", out var moltenHandle))
                    return moltenHandle;

                throw new DllNotFoundException(
                    "Could not locate the system Vulkan loader. SkiaGameRendering.Core.VK needs a " +
                    "Vulkan loader (vulkan-1.dll / libvulkan.so.1 / libvulkan.dylib / " +
                    "libMoltenVK.dylib) already installed on the host - it does not vendor one, " +
                    "unlike Core.ANGLE's bundled ANGLE DLLs, because the Vulkan loader is a system " +
                    "component every Vulkan-capable install already ships.");
            });
        }

        [DllImport(VulkanLoader, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr vkGetInstanceProcAddr(IntPtr instance, [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(VulkanLoader, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr vkGetDeviceProcAddr(IntPtr device, [MarshalAs(UnmanagedType.LPStr)] string name);

        /// <summary>
        /// The proc-address resolution Skia's <c>GrVkGpu</c> uses internally via
        /// <see cref="SkiaSharp.GRVkGetProcedureAddressDelegate"/>: try the device-level dispatch
        /// table first when a device handle is available (skips a layer of indirection versus the
        /// instance-level table), falling back to the instance-level lookup otherwise. This is the
        /// same order Skia's own Vulkan window contexts use.
        /// </summary>
        internal static IntPtr GetProcedureAddress(string name, IntPtr instance, IntPtr device)
        {
            if (device != IntPtr.Zero)
            {
                var deviceProc = vkGetDeviceProcAddr(device, name);
                if (deviceProc != IntPtr.Zero)
                    return deviceProc;
            }
            return vkGetInstanceProcAddr(instance, name);
        }
    }
}
