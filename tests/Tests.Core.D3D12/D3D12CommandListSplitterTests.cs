using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.D3D12;
using Xunit;
using static Tests.CoreD3D12.D3D12TestNative;

namespace Tests.CoreD3D12;

/// <summary>
/// <see cref="D3D12CommandListSplitter"/> on a real WARP device: a list can be submitted mid-recording and
/// reopened, each reopen in one frame takes its own allocator, allocators are recycled only after the retire
/// window and then stay bounded, and the state setters it carries record on a real list (a wrong vtable slot
/// would call some other method with the wrong arguments).
/// </summary>
public sealed unsafe class D3D12CommandListSplitterTests
{
    const int D3D12_COMMAND_LIST_TYPE_DIRECT = 0;
    const int D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV = 0;
    const int D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER = 1;
    const int D3D12_DESCRIPTOR_HEAP_TYPE_RTV = 2;
    const int D3D12_DESCRIPTOR_HEAP_TYPE_DSV = 3;
    const int D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE = 1;
    const int D3D_ROOT_SIGNATURE_VERSION_1 = 1;

    static readonly Guid IID_ID3D12DescriptorHeap = new("8efb471d-616c-4f49-90f7-127bb763fa51");
    static readonly Guid IID_ID3D12RootSignature = new("c54a6b66-72df-4ee8-8be5-a946a1429214");

    [Fact]
    public void NullDeviceOrQueue_Throws()
    {
        using var d3d12 = new D3D12TestDevice();

        Assert.Throws<ArgumentException>(() => new D3D12CommandListSplitter(IntPtr.Zero, d3d12.Queue));
        Assert.Throws<ArgumentException>(() => new D3D12CommandListSplitter(d3d12.Device, IntPtr.Zero));
    }

    [Fact]
    public void SubmitThenReopen_RecordsAgainAndBothHalvesRun()
    {
        using var d3d12 = new D3D12TestDevice();
        using var splitter = new D3D12CommandListSplitter(d3d12.Device, d3d12.Queue);
        using var list = new TestList(d3d12);
        var fence = CreateFence(d3d12.Device, 0, 0, IID_ID3D12Fence);
        try
        {
            splitter.Submit(list.Handle);
            Signal(d3d12.Queue, fence, 1);

            splitter.Reopen(list.Handle, frame: 0);
            splitter.Submit(list.Handle);
            Signal(d3d12.Queue, fence, 2);

            WaitFor(fence, 2);
        }
        finally
        {
            Release(fence);
        }
    }

    [Fact]
    public void Reopen_InOneFrame_TakesAFreshAllocatorEachTime()
    {
        using var d3d12 = new D3D12TestDevice();
        using var splitter = new D3D12CommandListSplitter(d3d12.Device, d3d12.Queue);
        using var list = new TestList(d3d12);

        for (var i = 1; i <= 5; i++)
        {
            splitter.Submit(list.Handle);
            splitter.Reopen(list.Handle, frame: 7);
            Assert.Equal(i, splitter.AllocatorCount);
        }
    }

    [Fact]
    public void Reopen_AcrossFrames_RecyclesAllocatorsAndStaysBounded()
    {
        const int reopensPerFrame = 3;
        using var d3d12 = new D3D12TestDevice();
        using var splitter = new D3D12CommandListSplitter(d3d12.Device, d3d12.Queue);
        using var list = new TestList(d3d12);
        var fence = CreateFence(d3d12.Device, 0, 0, IID_ID3D12Fence);
        try
        {
            ulong value = 0;
            var countAtFrame20 = 0;
            for (var frame = 0; frame < 60; frame++)
            {
                for (var i = 0; i < reopensPerFrame; i++)
                {
                    splitter.Submit(list.Handle);
                    splitter.Reopen(list.Handle, frame);
                }

                // The engine's frame ends in a submission the GPU finishes before the allocators retire.
                splitter.Submit(list.Handle);
                Signal(d3d12.Queue, fence, ++value);
                WaitFor(fence, value);
                splitter.Reopen(list.Handle, frame);

                if (frame == 20)
                    countAtFrame20 = splitter.AllocatorCount;
            }

            Assert.Equal(countAtFrame20, splitter.AllocatorCount);
            Assert.True(splitter.AllocatorCount <= (reopensPerFrame + 1) * D3D12CommandListSplitter.RetireFrames + 1,
                $"{splitter.AllocatorCount} allocators for {reopensPerFrame + 1} reopens a frame");
        }
        finally
        {
            Release(fence);
        }
    }

    /// <summary>
    /// Records the three state setters on a real list with a real root signature, descriptor heaps and views (the
    /// runtime reads the views while recording, so a made-up handle would crash); the list is closed, never run.
    /// </summary>
    [Fact]
    public void StateSetters_RecordOnARealList()
    {
        using var d3d12 = new D3D12TestDevice();
        using var list = new TestList(d3d12);
        var rootSignature = CreateEmptyRootSignature(d3d12.Device);
        var resourceHeap = CreateHeap(d3d12.Device, D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, shaderVisible: true);
        var samplerHeap = CreateHeap(d3d12.Device, D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER, shaderVisible: true);
        var rtvHeap = CreateHeap(d3d12.Device, D3D12_DESCRIPTOR_HEAP_TYPE_RTV, shaderVisible: false);
        var dsvHeap = CreateHeap(d3d12.Device, D3D12_DESCRIPTOR_HEAP_TYPE_DSV, shaderVisible: false);
        var color = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, 4, 4, D3D12Constants.FormatR8G8B8A8Unorm);
        var depth = CreateDepthTexture(d3d12.Device, 4, 4);
        try
        {
            var rtv = HeapStart(rtvHeap);
            var dsv = HeapStart(dsvHeap);
            CreateView(d3d12.Device, 20, color, rtv); // ID3D12Device::CreateRenderTargetView
            CreateView(d3d12.Device, 21, depth, dsv); // ID3D12Device::CreateDepthStencilView

            D3D12CommandListSplitter.SetGraphicsRootSignature(list.Handle, rootSignature);
            D3D12CommandListSplitter.SetDescriptorHeaps(list.Handle, resourceHeap, samplerHeap);
            D3D12CommandListSplitter.SetRenderTargets(list.Handle, new[] { rtv }, dsv);
            D3D12CommandListSplitter.SetRenderTargets(list.Handle, new[] { rtv }, depthStencilView: 0);

            CloseCommandList(list.Handle);
            list.Closed = true;
        }
        finally
        {
            Release(depth);
            D3D12SkiaSurfaceFactory.ReleaseResource(color);
            Release(dsvHeap);
            Release(rtvHeap);
            Release(samplerHeap);
            Release(resourceHeap);
            Release(rootSignature);
        }
    }

    [Fact]
    public void AfterDispose_UsingItThrows()
    {
        using var d3d12 = new D3D12TestDevice();
        using var list = new TestList(d3d12);
        var splitter = new D3D12CommandListSplitter(d3d12.Device, d3d12.Queue);
        splitter.Submit(list.Handle);
        splitter.Reopen(list.Handle, frame: 0);
        splitter.Dispose();
        splitter.Dispose();

        Assert.Throws<ObjectDisposedException>(() => splitter.Submit(list.Handle));
        Assert.Throws<ObjectDisposedException>(() => splitter.Reopen(list.Handle, frame: 1));
    }

    static void WaitFor(IntPtr fence, ulong value)
    {
        var timer = Stopwatch.StartNew();
        while (GetCompletedValue(fence) < value)
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(20), $"The GPU never reached fence value {value}.");
            Thread.Sleep(1);
        }
    }

    static IntPtr CreateHeap(IntPtr device, int type, bool shaderVisible)
    {
        var desc = new HeapDesc { Type = type, NumDescriptors = 8, Flags = shaderVisible ? D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE : 0 };
        // ID3D12Device::CreateDescriptorHeap, vtable slot 14.
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, HeapDesc*, Guid*, void**, int>)(*(void***)device)[14];
        void* heap;
        var iid = IID_ID3D12DescriptorHeap;
        Assert.True(fn(device, &desc, &iid, &heap) >= 0, "CreateDescriptorHeap failed.");
        return (IntPtr)heap;
    }

    /// <summary>ID3D12DescriptorHeap::GetCPUDescriptorHandleForHeapStart, vtable slot 9; the handle comes back through a hidden pointer.</summary>
    static nuint HeapStart(IntPtr heap)
    {
        nuint handle;
        ((delegate* unmanaged[MemberFunction]<IntPtr, nuint*, nuint*>)(*(void***)heap)[9])(heap, &handle);
        return handle;
    }

    /// <summary>CreateRenderTargetView (slot 20) / CreateDepthStencilView (slot 21): resource, default description, destination handle.</summary>
    static void CreateView(IntPtr device, int slot, IntPtr resource, nuint destination) =>
        ((delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, IntPtr, nuint, void>)(*(void***)device)[slot])(device, resource, IntPtr.Zero, destination);

    static IntPtr CreateDepthTexture(IntPtr device, int width, int height)
    {
        const uint D32_FLOAT = 40, ALLOW_DEPTH_STENCIL = 0x2, DEPTH_WRITE = 0x10;
        var desc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D,
            Width = (ulong)width,
            Height = (uint)height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = D32_FLOAT,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN,
            Flags = ALLOW_DEPTH_STENCIL,
        };
        var heap = new D3D12_HEAP_PROPERTIES
        {
            Type = D3D12_HEAP_TYPE_DEFAULT,
            CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN,
            MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN,
            CreationNodeMask = 1,
            VisibleNodeMask = 1,
        };
        return CreateCommittedResource(device, heap, D3D12_HEAP_FLAG_NONE, desc, DEPTH_WRITE, IID_ID3D12Resource);
    }

    static IntPtr CreateEmptyRootSignature(IntPtr device)
    {
        // D3D12_ROOT_SIGNATURE_DESC (40 bytes): no parameters, no static samplers, no flags.
        var desc = stackalloc ulong[5];
        new Span<ulong>(desc, 5).Clear();
        IntPtr blob, errors;
        Assert.True(D3D12SerializeRootSignature(desc, D3D_ROOT_SIGNATURE_VERSION_1, out blob, out errors) >= 0, "D3D12SerializeRootSignature failed.");
        try
        {
            // ID3DBlob::GetBufferPointer (slot 3) and GetBufferSize (slot 4).
            var pointer = ((delegate* unmanaged[MemberFunction]<IntPtr, IntPtr>)(*(void***)blob)[3])(blob);
            var size = ((delegate* unmanaged[MemberFunction]<IntPtr, nuint>)(*(void***)blob)[4])(blob);
            // ID3D12Device::CreateRootSignature, vtable slot 16.
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, IntPtr, nuint, Guid*, void**, int>)(*(void***)device)[16];
            void* signature;
            var iid = IID_ID3D12RootSignature;
            Assert.True(fn(device, 0, pointer, size, &iid, &signature) >= 0, "CreateRootSignature failed.");
            return (IntPtr)signature;
        }
        finally
        {
            Release(blob);
            if (errors != IntPtr.Zero)
                Release(errors);
        }
    }

    [DllImport("d3d12.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int D3D12SerializeRootSignature(ulong* rootSignature, int version, out IntPtr blob, out IntPtr errorBlob);

    [StructLayout(LayoutKind.Sequential)]
    struct HeapDesc
    {
        public int Type;
        public uint NumDescriptors;
        public int Flags;
        public uint NodeMask;
    }

    /// <summary>A direct list in the recording state with its own allocator.</summary>
    sealed class TestList : IDisposable
    {
        readonly IntPtr _allocator;

        internal IntPtr Handle { get; }

        /// <summary>Set once the test has closed the list itself, so disposing does not close it twice.</summary>
        internal bool Closed { get; set; }

        internal TestList(D3D12TestDevice device)
        {
            _allocator = CreateCommandAllocator(device.Device, D3D12_COMMAND_LIST_TYPE_DIRECT, IID_ID3D12CommandAllocator);
            Handle = CreateCommandList(device.Device, 0, D3D12_COMMAND_LIST_TYPE_DIRECT, _allocator, IID_ID3D12GraphicsCommandList);
        }

        public void Dispose()
        {
            Release(Handle);
            Release(_allocator);
        }
    }
}
