using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.Metal
{
    /// <summary>
    /// The Objective-C runtime calls this library needs to inspect a host's Metal objects. Skia itself
    /// takes the raw <c>id&lt;MTLDevice&gt;</c>/<c>id&lt;MTLCommandQueue&gt;</c>/<c>id&lt;MTLTexture&gt;</c>
    /// pointers, so this is deliberately tiny: no Metal binding library (no Xamarin.Mac, no
    /// SharpMetal), for the same reason <c>Core.VK</c> takes none - host engines each bring their own,
    /// and a hard PackageReference here would force one version on every consumer.
    /// </summary>
    internal static class MetalNative
    {
        const string ObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjC, EntryPoint = "sel_registerName")]
        public static extern IntPtr Selector(string name);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        public static extern nuint SendNUInt(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport(ObjC, EntryPoint = "objc_getProtocol")]
        public static extern IntPtr GetProtocol(string name);

#if NETSTANDARD2_1
        // netstandard2.1 has no OperatingSystem.IsMacOS. Unity on iOS has not been tried, so whether it
        // reports OSX here is unverified.
        public static bool IsApplePlatform => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#else
        public static bool IsApplePlatform =>
            OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst();
#endif

        public static readonly IntPtr SelUsage = Selector("usage");
        public static readonly IntPtr SelConformsToProtocol = Selector("conformsToProtocol:");

        /// <summary>
        /// Whether <paramref name="obj"/> is an Objective-C object implementing <paramref name="protocol"/>.
        /// Catches a host handing over the wrong handle (a texture where a device belongs, a
        /// <c>RenderingDevice</c> RID instead of the native pointer) before Skia dereferences it.
        /// Returns <c>true</c> when the protocol isn't registered (Metal.framework not loaded), since
        /// there is then nothing to check against rather than evidence of a wrong handle.
        /// </summary>
        public static bool ConformsTo(IntPtr obj, string protocol)
        {
            var proto = GetProtocol(protocol);
            return proto == IntPtr.Zero || SendBool(obj, SelConformsToProtocol, proto);
        }
    }
}
