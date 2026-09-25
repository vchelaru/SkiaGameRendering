using System.Runtime.InteropServices;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;
using Xunit;
using static Tests.CoreD3D12.D3D12TestNative;

namespace Tests.CoreD3D12;

/// <summary>
/// Exercises the pieces Core.D3D12 grew for the Godot backend, on the same headless WARP device
/// <see cref="D3D12SkiaPixelReadbackTests"/> uses: <see cref="D3D12SkiaSurfaceFactory.CreateRenderTargetResource"/>
/// (a typed resource Skia can render into), an asynchronous <see cref="D3D12SkiaSurfaceFactory.EndDraw(bool)"/>,
/// and <see cref="D3D12ResourceTransitioner.CopyWithTransitions"/> landing the result in a TYPELESS
/// resource of the same family - the exact shape the Godot D3D12 backend runs every frame, since
/// Godot allocates all its textures typeless. The destination is then read back through
/// <see cref="D3D12ResourceTransitioner.Transition"/> plus a readback-heap copy and checked pixel by
/// pixel, and the ring of in-flight submissions is wrapped around more than once so slot reuse gets
/// exercised too.
/// </summary>
public sealed unsafe class D3D12ResourceTransitionerTests
{
    const uint DXGI_FORMAT_R8G8B8A8_TYPELESS = 27;
    const uint D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE = 0x40 | 0x80;

    [Fact]
    public void CopyWithTransitions_LandsSkiaDrawInTypelessDestination_OnWarp()
    {
        const int width = 8, height = 6;
        var expected = new SKColor(200, 30, 90, 255);

        using var d3d12 = new D3D12TestDevice();
        var skiaResource = IntPtr.Zero;
        var destination = IntPtr.Zero;
        try
        {
            using var factory = new D3D12SkiaSurfaceFactory();
            factory.InitializeFromNative(d3d12.Adapter, d3d12.Device, d3d12.Queue);
            using var transitioner = new D3D12ResourceTransitioner(d3d12.Device, d3d12.Queue, slots: 2);

            skiaResource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, width, height, D3D12Constants.FormatR8G8B8A8Unorm);
            destination = CreateTypelessTexture(d3d12.Device, width, height, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);

            var state = factory.CreateTextureState(skiaResource, D3D12Constants.FormatR8G8B8A8Unorm, D3D12Constants.ResourceStateRenderTarget);
            factory.BeginDraw();
            var (surface, renderTarget) = factory.CreateSurface(state, width, height, SKColorType.Rgba8888);
            factory.EndDraw(synchronous: false);

            // Several frames, so the two-slot ring either reuses allocators/lists that were in flight or grows.
            for (int frame = 0; frame < 5; frame++)
            {
                factory.BeginDraw();
                surface.Canvas.Clear(frame == 4 ? expected : SKColors.Black);
                surface.Flush();
                factory.EndDraw(synchronous: false);

                transitioner.CopyWithTransitions(
                    destination, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE,
                    skiaResource, D3D12Constants.ResourceStateRenderTarget, D3D12Constants.ResourceStateRenderTarget);
            }

            transitioner.Transition(destination, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE, D3D12Constants.ResourceStateCopySource);
            transitioner.WaitForCompletion();

            var pixels = ReadBack(d3d12, destination, width, height);
            for (int i = 0; i < width * height; i++)
            {
                Assert.Equal(expected.Red, pixels[i * 4 + 0]);
                Assert.Equal(expected.Green, pixels[i * 4 + 1]);
                Assert.Equal(expected.Blue, pixels[i * 4 + 2]);
                Assert.Equal(expected.Alpha, pixels[i * 4 + 3]);
            }

            renderTarget.Dispose();
            surface.Dispose();
        }
        finally
        {
            if (destination != IntPtr.Zero)
                Release(destination);
            D3D12SkiaSurfaceFactory.ReleaseResource(skiaResource);
        }
    }

    [Fact]
    public void Transitioner_RejectsBadArgumentsAndUseAfterDispose_OnWarp()
    {
        using var d3d12 = new D3D12TestDevice();

        Assert.Throws<ArgumentException>(() => new D3D12ResourceTransitioner(IntPtr.Zero, d3d12.Queue));
        Assert.Throws<ArgumentException>(() => new D3D12ResourceTransitioner(d3d12.Device, IntPtr.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new D3D12ResourceTransitioner(d3d12.Device, d3d12.Queue, slots: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new D3D12ResourceTransitioner(d3d12.Device, d3d12.Queue, slots: D3D12ResourceTransitioner.MaxSlots + 1));

        var transitioner = new D3D12ResourceTransitioner(d3d12.Device, d3d12.Queue);
        Assert.Throws<ArgumentException>(() => transitioner.Transition(IntPtr.Zero, 0, 0));
        Assert.Throws<ArgumentException>(() => transitioner.CopyWithTransitions(IntPtr.Zero, 0, 0, new IntPtr(1), 0, 0));
        transitioner.WaitForCompletion(); // nothing pending: returns immediately
        transitioner.Dispose();
        transitioner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => transitioner.Transition(new IntPtr(1), 0, 0));
    }

    /// <summary>
    /// Many copies queued back to back from a one-slot ring: the ring grows rather than stalling,
    /// stays within its cap, and the last copy is what lands.
    /// </summary>
    [Fact]
    public void Transitioner_GrowsRingForManyInFlightSubmissions_OnWarp()
    {
        const int width = 4, height = 4;
        var expected = new SKColor(20, 180, 60, 255);

        using var d3d12 = new D3D12TestDevice();
        var skiaResource = IntPtr.Zero;
        var destination = IntPtr.Zero;
        try
        {
            using var factory = new D3D12SkiaSurfaceFactory();
            factory.InitializeFromNative(d3d12.Adapter, d3d12.Device, d3d12.Queue);
            using var transitioner = new D3D12ResourceTransitioner(d3d12.Device, d3d12.Queue, slots: 1);

            skiaResource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, width, height, D3D12Constants.FormatR8G8B8A8Unorm);
            destination = CreateTypelessTexture(d3d12.Device, width, height, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);

            var state = factory.CreateTextureState(skiaResource, D3D12Constants.FormatR8G8B8A8Unorm, D3D12Constants.ResourceStateRenderTarget);
            factory.BeginDraw();
            var (surface, renderTarget) = factory.CreateSurface(state, width, height, SKColorType.Rgba8888);
            surface.Canvas.Clear(expected);
            surface.Flush();
            factory.EndDraw(synchronous: false);

            for (int i = 0; i < 20; i++)
                transitioner.CopyWithTransitions(
                    destination, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE,
                    skiaResource, D3D12Constants.ResourceStateRenderTarget, D3D12Constants.ResourceStateRenderTarget);
            transitioner.Transition(destination, D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE, D3D12Constants.ResourceStateCopySource);
            Assert.InRange(transitioner.SlotCount, 1, D3D12ResourceTransitioner.MaxSlots);
            transitioner.WaitForCompletion();

            var pixels = ReadBack(d3d12, destination, width, height);
            Assert.Equal([expected.Red, expected.Green, expected.Blue, expected.Alpha], pixels[..4]);

            renderTarget.Dispose();
            surface.Dispose();
        }
        finally
        {
            if (destination != IntPtr.Zero)
                Release(destination);
            D3D12SkiaSurfaceFactory.ReleaseResource(skiaResource);
        }
    }

    [Fact]
    public void StaticHelpers_ValidateArguments_OnWarp()
    {
        using var d3d12 = new D3D12TestDevice();

        // WARP's answer depends on the Windows build; only that the query runs is checked here.
        _ = D3D12SkiaSurfaceFactory.QueryEnhancedBarriersSupported(d3d12.Device);

        Assert.Throws<ArgumentException>(() => D3D12SkiaSurfaceFactory.QueryEnhancedBarriersSupported(IntPtr.Zero));
        Assert.Throws<ArgumentException>(() => D3D12SkiaSurfaceFactory.CreateRenderTargetResource(IntPtr.Zero, 4, 4, D3D12Constants.FormatR8G8B8A8Unorm));
        Assert.Throws<ArgumentOutOfRangeException>(() => D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, 0, 4, D3D12Constants.FormatR8G8B8A8Unorm));
        Assert.Throws<ArgumentOutOfRangeException>(() => D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, 4, -1, D3D12Constants.FormatR8G8B8A8Unorm));
        D3D12SkiaSurfaceFactory.ReleaseResource(IntPtr.Zero); // no-op
    }

    static IntPtr CreateTypelessTexture(IntPtr device, int width, int height, uint initialState)
    {
        var desc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D,
            Width = (ulong)width,
            Height = (uint)height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = DXGI_FORMAT_R8G8B8A8_TYPELESS,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN,
            Flags = D3D12_RESOURCE_FLAG_NONE,
        };
        var heap = new D3D12_HEAP_PROPERTIES
        {
            Type = D3D12_HEAP_TYPE_DEFAULT,
            CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN,
            MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN,
            CreationNodeMask = 1,
            VisibleNodeMask = 1,
        };
        return CreateCommittedResource(device, heap, D3D12_HEAP_FLAG_NONE, desc, initialState, IID_ID3D12Resource);
    }

    /// <summary>Same readback-heap copy as <see cref="D3D12SkiaPixelReadbackTests"/>; the source must already be in COPY_SOURCE.</summary>
    static byte[] ReadBack(D3D12TestDevice d3d12, IntPtr source, int width, int height)
    {
        const uint pixelBytes = 4;
        const uint pitchAlignment = 256;
        uint rowPitch = ((uint)width * pixelBytes + (pitchAlignment - 1)) & ~(pitchAlignment - 1);

        var bufferDesc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION_BUFFER,
            Width = rowPitch * (ulong)height,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = 0,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR,
            Flags = D3D12_RESOURCE_FLAG_NONE,
        };
        var heap = new D3D12_HEAP_PROPERTIES
        {
            Type = D3D12_HEAP_TYPE_READBACK,
            CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN,
            MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN,
            CreationNodeMask = 1,
            VisibleNodeMask = 1,
        };
        var readbackBuffer = IntPtr.Zero;
        var allocator = IntPtr.Zero;
        var list = IntPtr.Zero;
        var fence = IntPtr.Zero;
        try
        {
            readbackBuffer = CreateCommittedResource(d3d12.Device, heap, D3D12_HEAP_FLAG_NONE, bufferDesc, D3D12_RESOURCE_STATE_COPY_DEST, IID_ID3D12Resource);
            allocator = CreateCommandAllocator(d3d12.Device, D3D12_COMMAND_LIST_TYPE_DIRECT, IID_ID3D12CommandAllocator);
            list = CreateCommandList(d3d12.Device, 0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator, IID_ID3D12GraphicsCommandList);

            var dst = new D3D12_TEXTURE_COPY_LOCATION
            {
                pResource = readbackBuffer,
                Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT,
                PlacedFootprintFormat = DXGI_FORMAT_R8G8B8A8_UNORM,
                PlacedFootprintWidth = (uint)width,
                PlacedFootprintHeight = (uint)height,
                PlacedFootprintDepth = 1,
                PlacedFootprintRowPitch = rowPitch,
            };
            var src = new D3D12_TEXTURE_COPY_LOCATION { pResource = source, Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX, SubresourceIndex = 0 };
            CopyTextureRegion(list, dst, 0, 0, 0, src);
            CloseCommandList(list);
            ExecuteCommandLists(d3d12.Queue, list);

            fence = CreateFence(d3d12.Device, 0, D3D12_FENCE_FLAG_NONE, IID_ID3D12Fence);
            Signal(d3d12.Queue, fence, 1);
            var spins = 0;
            while (GetCompletedValue(fence) < 1)
            {
                Assert.True(++spins < 100_000, "Fence never reached the signaled value - GPU work did not complete.");
                Thread.Sleep(1);
            }

            var mapped = Map(readbackBuffer, 0);
            var pixels = new byte[width * height * pixelBytes];
            for (var row = 0; row < height; row++)
                Marshal.Copy(mapped + (row * (int)rowPitch), pixels, row * width * (int)pixelBytes, width * (int)pixelBytes);
            Unmap(readbackBuffer, 0);
            return pixels;
        }
        finally
        {
            if (fence != IntPtr.Zero) Release(fence);
            if (list != IntPtr.Zero) Release(list);
            if (allocator != IntPtr.Zero) Release(allocator);
            if (readbackBuffer != IntPtr.Zero) Release(readbackBuffer);
        }
    }
}
