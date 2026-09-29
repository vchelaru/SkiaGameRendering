using System.Runtime.InteropServices;

namespace Tests.CoreMetal;

/// <summary>
/// Just enough raw Objective-C runtime and Metal to play the host engine: a device, a queue, a
/// render-target texture, and a blit back to a CPU-visible buffer. Test-only, like
/// <c>VulkanTestNative</c>; <c>Core.Metal</c> itself needs none of this.
/// </summary>
static class MetalTestNative
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

    public const nuint MTLPixelFormatRGBA8Unorm = 70;
    public const nuint MTLPixelFormatBGRA8Unorm = 80;
    public const nuint MTLTextureUsageShaderRead = 0x1;
    public const nuint MTLTextureUsageRenderTarget = 0x4;
    public const nuint MTLStorageModePrivate = 2;
    public const nuint MTLResourceStorageModeShared = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct MTLOrigin { public nuint X, Y, Z; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MTLSize { public nuint Width, Height, Depth; }

    [DllImport(MetalFramework)]
    public static extern IntPtr MTLCreateSystemDefaultDevice();

    [DllImport(ObjC)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    public static extern void objc_release(IntPtr obj);

    [DllImport(ObjC)]
    public static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    public static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector, nuint arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, nuint arg0, nuint arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, nuint pixelFormat, nuint width, nuint height,
        [MarshalAs(UnmanagedType.I1)] bool mipmapped);

    // -[MTLBlitCommandEncoder copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toBuffer:
    //   destinationOffset:destinationBytesPerRow:destinationBytesPerImage:]
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendCopyTextureToBuffer(IntPtr receiver, IntPtr selector,
        IntPtr texture, nuint slice, nuint level, MTLOrigin origin, MTLSize size,
        IntPtr buffer, nuint offset, nuint bytesPerRow, nuint bytesPerImage);

    public static IntPtr Sel(string name) => sel_registerName(name);
}
