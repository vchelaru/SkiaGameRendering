using System.Runtime.InteropServices;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using static Tests.CoreD3D12.D3D12TestNative;

namespace Tests.CoreD3D12;

/// <summary>
/// Draws through the full <c>Core.D3D12</c> pipeline - a real headless WARP device/queue (see
/// <see cref="D3D12TestDevice"/>), <c>D3D12SkiaSurfaceFactory</c>'s texture wrapping and queue-lock
/// bracketing, Skia's Ganesh D3D12 backend - into a real, host-allocated <c>ID3D12Resource</c>, then
/// reads it back to CPU via a readback-heap copy and checks the pixels. No window is ever shown and
/// no engine is involved: this plays the "host engine" role itself, the same shape
/// <c>AngleSkiaPixelReadbackTests</c> (WARP/D3D11) and <c>VkSkiaPixelReadbackTests</c> (lavapipe)
/// play for the other two Core.* backends - see the headless-gpu-testing skill.
/// <para>
/// The post-draw resource-state transition below (<c>D3D12_RESOURCE_STATE_RENDER_TARGET</c> -&gt;
/// <c>D3D12_RESOURCE_STATE_COPY_SOURCE</c>) is exactly the ASSUMED state documented on
/// <see cref="D3D12SkiaSurfaceFactory.EndDraw"/> - this test is also, incidentally, the closest
/// thing to empirical evidence for that assumption this repo has: a wrong <c>StateBefore</c> here is
/// undefined behavior per the D3D12 spec (WARP tends to render garbage rather than crash, unlike a
/// real driver, which is exactly why this being WARP-only is not a substitute for the debug layer,
/// which this test does not enable).
/// </para>
/// </summary>
public sealed unsafe class D3D12SkiaPixelReadbackTests
{
    readonly ITestOutputHelper _output;

    public D3D12SkiaPixelReadbackTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Clear_WritesExpectedColor_ThroughRealD3D12Device()
    {
        var expected = new SKColor(10, 20, 30, 255);

        var pixels = RenderAndReadBack(4, 4, canvas => canvas.Clear(expected));

        Assert.Equal([expected.Red, expected.Green, expected.Blue, expected.Alpha], pixels[..4]);
    }

    /// <summary>
    /// The same pipeline as above, but drawing <see cref="GoldenScene"/> and comparing the whole
    /// image against a checked-in reference instead of sampling one pixel - see
    /// <c>VkSkiaPixelReadbackTests.Scene_MatchesGolden_ThroughRealVulkanDevice</c>'s doc comment for
    /// why a solid clear can't catch what this does. WARP is deterministic the same way it is for
    /// the D3D11/ANGLE goldens, so this needs no <c>SKIAGAMERENDERING_PINNED_RASTERIZER</c> gate.
    /// </summary>
    [Fact]
    public void Scene_MatchesGolden_ThroughRealD3D12Device()
    {
        var pixels = RenderAndReadBack(GoldenScene.Width, GoldenScene.Height, GoldenScene.Draw);

        GoldenImage.AssertMatchesGolden(pixels, "core-d3d12-scene.png");
    }

    /// <summary>
    /// Wraps a host-allocated <c>ID3D12Resource</c> (a committed, render-target-usage Texture2D) with
    /// <see cref="D3D12SkiaSurfaceFactory"/>, runs <paramref name="draw"/> on the resulting canvas,
    /// and copies the result back to CPU as tightly packed RGBA8888. Shared by both tests above so
    /// the D3D12 scaffolding - resource, readback buffer, command list, barrier, fence - is written
    /// once.
    /// </summary>
    byte[] RenderAndReadBack(int width, int height, Action<SKCanvas> draw)
    {
        using var d3d12 = new D3D12TestDevice();

        var resource = IntPtr.Zero;
        var readbackBuffer = IntPtr.Zero;
        var commandAllocator = IntPtr.Zero;
        var commandList = IntPtr.Zero;
        var fence = IntPtr.Zero;

        try
        {
            var resourceDesc = new D3D12_RESOURCE_DESC
            {
                Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D,
                Alignment = 0,
                Width = (ulong)width,
                Height = (uint)height,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = DXGI_FORMAT_R8G8B8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN,
                Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,
            };
            var heapProperties = new D3D12_HEAP_PROPERTIES
            {
                Type = D3D12_HEAP_TYPE_DEFAULT,
                CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN,
                MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN,
                CreationNodeMask = 1,
                VisibleNodeMask = 1,
            };
            resource = CreateCommittedResource(
                d3d12.Device, heapProperties, D3D12_HEAP_FLAG_NONE, resourceDesc,
                D3D12_RESOURCE_STATE_RENDER_TARGET, IID_ID3D12Resource);

            // --- The actual Core.D3D12 round trip: wrap the host-owned ID3D12Resource, draw with Skia. ---
            using var factory = new D3D12SkiaSurfaceFactory();
            factory.InitializeFromNative(d3d12.Adapter, d3d12.Device, d3d12.Queue);

            var state = factory.CreateTextureState(
                resource, format: DXGI_FORMAT_R8G8B8A8_UNORM, resourceState: D3D12_RESOURCE_STATE_RENDER_TARGET);

            factory.BeginDraw();
            var (surface, renderTarget) = factory.CreateSurface(state, width, height, SKColorType.Rgba8888);
            draw(surface.Canvas);
            surface.Flush();
            factory.EndDraw();
            renderTarget.Dispose();
            surface.Dispose();

            // --- Read the drawn resource back to CPU via a readback-heap buffer copy. ---
            // D3D12_TEXTURE_DATA_PITCH_ALIGNMENT is 256 bytes - the readback buffer's row pitch must
            // be rounded up to it, independently of the texture's own (tightly packed) width.
            const uint pixelBytes = 4;
            const uint pitchAlignment = 256;
            uint rowPitch = ((uint)width * pixelBytes + (pitchAlignment - 1)) & ~(pitchAlignment - 1);
            ulong bufferSize = rowPitch * (uint)height;

            var bufferDesc = new D3D12_RESOURCE_DESC
            {
                Dimension = D3D12_RESOURCE_DIMENSION_BUFFER,
                Alignment = 0,
                Width = bufferSize,
                Height = 1,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = 0, // DXGI_FORMAT_UNKNOWN - required for buffer resources.
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR,
                Flags = D3D12_RESOURCE_FLAG_NONE,
            };
            var readbackHeapProperties = new D3D12_HEAP_PROPERTIES
            {
                Type = D3D12_HEAP_TYPE_READBACK,
                CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN,
                MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN,
                CreationNodeMask = 1,
                VisibleNodeMask = 1,
            };
            readbackBuffer = CreateCommittedResource(
                d3d12.Device, readbackHeapProperties, D3D12_HEAP_FLAG_NONE, bufferDesc,
                D3D12_RESOURCE_STATE_COPY_DEST, IID_ID3D12Resource);

            commandAllocator = CreateCommandAllocator(d3d12.Device, D3D12_COMMAND_LIST_TYPE_DIRECT, IID_ID3D12CommandAllocator);
            var commandListPtr = CreateCommandList(
                d3d12.Device, 0, D3D12_COMMAND_LIST_TYPE_DIRECT, commandAllocator, IID_ID3D12GraphicsCommandList);
            commandList = commandListPtr;

            // See D3D12SkiaSurfaceFactory.EndDraw's doc comment: SkiaSharp 3.119.4 gives no way to
            // query or steer the resource's post-draw state, so D3D12_RESOURCE_STATE_RENDER_TARGET
            // is the documented ASSUMPTION for a render-target-usage resource after a Skia draw, not
            // a value read back from anywhere.
            var barrier = new D3D12_RESOURCE_BARRIER
            {
                Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION,
                Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE,
                TransitionResource = resource,
                TransitionSubresource = 0,
                TransitionStateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET,
                TransitionStateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE,
            };
            ResourceBarrier(commandList, barrier);

            var dstLocation = new D3D12_TEXTURE_COPY_LOCATION
            {
                pResource = readbackBuffer,
                Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT,
                PlacedFootprintOffset = 0,
                PlacedFootprintFormat = DXGI_FORMAT_R8G8B8A8_UNORM,
                PlacedFootprintWidth = (uint)width,
                PlacedFootprintHeight = (uint)height,
                PlacedFootprintDepth = 1,
                PlacedFootprintRowPitch = rowPitch,
            };
            var srcLocation = new D3D12_TEXTURE_COPY_LOCATION
            {
                pResource = resource,
                Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX,
                SubresourceIndex = 0,
            };
            CopyTextureRegion(commandList, dstLocation, 0, 0, 0, srcLocation);

            CloseCommandList(commandList);
            ExecuteCommandLists(d3d12.Queue, commandList);

            fence = CreateFence(d3d12.Device, 0, D3D12_FENCE_FLAG_NONE, IID_ID3D12Fence);
            const ulong signalValue = 1;
            Signal(d3d12.Queue, fence, signalValue);

            // WARP executes synchronously enough in practice that a spin-wait never blocks long, and
            // this test project takes no dependency on a Win32 event-handle wrapper for
            // SetEventOnCompletion the way a production engine integration would.
            var spinCount = 0;
            while (GetCompletedValue(fence) < signalValue)
            {
                Assert.True(++spinCount < 100_000, "Fence never reached the signaled value - GPU work did not complete.");
                Thread.Sleep(1);
            }

            var mapped = Map(readbackBuffer, 0);
            var pixels = new byte[width * height * pixelBytes];
            for (var row = 0; row < height; row++)
                Marshal.Copy(mapped + (row * (int)rowPitch), pixels, row * width * (int)pixelBytes, width * (int)pixelBytes);
            Unmap(readbackBuffer, 0);

            _output.WriteLine($"D3D12 readback: {width}x{height}, row pitch {rowPitch}.");
            return pixels;
        }
        finally
        {
            if (fence != IntPtr.Zero)
                Release(fence);
            if (commandList != IntPtr.Zero)
                Release(commandList);
            if (commandAllocator != IntPtr.Zero)
                Release(commandAllocator);
            if (readbackBuffer != IntPtr.Zero)
                Release(readbackBuffer);
            if (resource != IntPtr.Zero)
                Release(resource);
        }
    }
}
