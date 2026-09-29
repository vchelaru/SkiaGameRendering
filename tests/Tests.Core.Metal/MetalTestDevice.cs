using static Tests.CoreMetal.MetalTestNative;

namespace Tests.CoreMetal;

/// <summary>
/// The system default <c>MTLDevice</c> plus one command queue on it, standing in for a host engine's.
/// macOS runners (GitHub's included) have a Metal device, so unlike the Vulkan and D3D12 tests there
/// is no software rasterizer to vendor.
/// </summary>
sealed class MetalTestDevice : IDisposable
{
    public IntPtr Device { get; }
    public IntPtr Queue { get; }

    public MetalTestDevice()
    {
        Device = MTLCreateSystemDefaultDevice();
        if (Device == IntPtr.Zero)
            throw new InvalidOperationException("MTLCreateSystemDefaultDevice returned nil: this machine has no Metal device.");
        Queue = Send(Device, Sel("newCommandQueue"));
        if (Queue == IntPtr.Zero)
            throw new InvalidOperationException("-[MTLDevice newCommandQueue] returned nil.");
    }

    /// <summary>A private-storage 2D texture, the way engines allocate render targets (Godot included).</summary>
    public IntPtr CreateTexture(int width, int height, nuint pixelFormat, nuint usage)
    {
        var descriptor = Send(objc_getClass("MTLTextureDescriptor"),
            Sel("texture2DDescriptorWithPixelFormat:width:height:mipmapped:"),
            pixelFormat, (nuint)width, (nuint)height, false);
        SendVoid(descriptor, Sel("setUsage:"), usage);
        SendVoid(descriptor, Sel("setStorageMode:"), MTLStorageModePrivate);
        var texture = Send(Device, Sel("newTextureWithDescriptor:"), descriptor);
        if (texture == IntPtr.Zero)
            throw new InvalidOperationException("-[MTLDevice newTextureWithDescriptor:] returned nil.");
        return texture;
    }

    /// <summary>
    /// Blits <paramref name="texture"/> into a shared buffer on <see cref="Queue"/> and returns its
    /// bytes, tightly packed, in the texture's own channel order. Committed after Skia's work on the
    /// same queue, so it also exercises the ordering a host engine relies on.
    /// </summary>
    public byte[] ReadBack(IntPtr texture, int width, int height)
    {
        var pool = objc_autoreleasePoolPush();
        var buffer = IntPtr.Zero;
        try
        {
            int bytesPerRow = width * 4;
            int length = bytesPerRow * height;
            buffer = Send(Device, Sel("newBufferWithLength:options:"), (nuint)length, MTLResourceStorageModeShared);

            var commandBuffer = Send(Queue, Sel("commandBuffer"));
            var blit = Send(commandBuffer, Sel("blitCommandEncoder"));
            SendCopyTextureToBuffer(blit,
                Sel("copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toBuffer:destinationOffset:destinationBytesPerRow:destinationBytesPerImage:"),
                texture, 0, 0, default, new MTLSize { Width = (nuint)width, Height = (nuint)height, Depth = 1 },
                buffer, 0, (nuint)bytesPerRow, (nuint)length);
            SendVoid(blit, Sel("endEncoding"));
            SendVoid(commandBuffer, Sel("commit"));
            SendVoid(commandBuffer, Sel("waitUntilCompleted"));

            var pixels = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(Send(buffer, Sel("contents")), pixels, 0, length);
            return pixels;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                objc_release(buffer);
            objc_autoreleasePoolPop(pool);
        }
    }

    public void Dispose()
    {
        objc_release(Queue);
        objc_release(Device);
    }
}
