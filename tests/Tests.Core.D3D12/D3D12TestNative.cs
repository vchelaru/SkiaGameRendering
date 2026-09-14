using System.Runtime.InteropServices;

namespace Tests.CoreD3D12;

/// <summary>
/// Raw D3D12/DXGI P/Invokes, structs and constants used ONLY by the test project to play the "host
/// engine" role - creating its own real WARP <c>ID3D12Device</c>/<c>ID3D12CommandQueue</c> and a
/// host-owned <c>ID3D12Resource</c>, exactly like <c>Tests.Core.VK</c>'s <c>VulkanTestNative.cs</c>
/// and <c>Tests.Core.ANGLE</c>'s <c>D3D11RawResources.cs</c> keep their own independent raw bindings
/// rather than reusing the library under test's own P/Invoke declarations - a bug in the shared
/// plumbing should not also hide itself from the test that's supposed to catch it.
///
/// Vtable slot indices and struct layouts below were NOT taken from memory or documentation alone -
/// they were cross-checked against <c>terrafx.interop.windows</c> (headers auto-generated from the
/// Windows SDK, the same cross-check source <c>D3D11Com.cs</c>'s doc comment describes), both by
/// reading its generated method/field declaration order and by compiling a throwaway probe that
/// takes the real <c>sizeof</c>/field-offset of every struct used here (<c>D3D12_TEXTURE_COPY_LOCATION</c>,
/// <c>D3D12_RESOURCE_BARRIER</c>, and friends all contain native unions, which C# has to flatten by
/// hand into explicit-offset structs - exactly the kind of place an off-by-one hides silently).
/// Deliberately minimal: no debug layer, no descriptor heaps beyond what a wrapped render target
/// needs - just enough raw D3D12 to stand up a device and queue, allocate a resource, hand it to
/// <c>D3D12SkiaSurfaceFactory</c>, and read the drawn pixels back via a readback-heap copy.
/// </summary>
internal static unsafe class D3D12TestNative
{
    // ---- IIDs, cross-checked against terrafx.interop.windows ----
    internal static readonly Guid IID_ID3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");
    internal static readonly Guid IID_ID3D12CommandQueue = new("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
    internal static readonly Guid IID_ID3D12CommandAllocator = new("6102dee4-af59-4b09-b999-b44d73f09b24");
    internal static readonly Guid IID_ID3D12GraphicsCommandList = new("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
    internal static readonly Guid IID_ID3D12Resource = new("696442be-a72e-4059-bc79-5b5c98040fad");
    internal static readonly Guid IID_ID3D12Fence = new("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");
    internal static readonly Guid IID_IDXGIFactory4 = new("1bc6ea02-ef36-464f-bf0c-21ca39e5168a");
    internal static readonly Guid IID_IDXGIAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");

    // ---- misc enums/flags/constants ----
    internal const int D3D_FEATURE_LEVEL_11_0 = 0xb000;
    internal const int D3D12_COMMAND_LIST_TYPE_DIRECT = 0;
    internal const int D3D12_COMMAND_QUEUE_FLAG_NONE = 0;
    internal const int D3D12_FENCE_FLAG_NONE = 0;
    internal const int D3D12_HEAP_FLAG_NONE = 0;
    internal const int D3D12_HEAP_TYPE_DEFAULT = 1;
    internal const int D3D12_HEAP_TYPE_READBACK = 3;
    internal const int D3D12_CPU_PAGE_PROPERTY_UNKNOWN = 0;
    internal const int D3D12_MEMORY_POOL_UNKNOWN = 0;
    internal const int D3D12_RESOURCE_DIMENSION_BUFFER = 1;
    internal const int D3D12_RESOURCE_DIMENSION_TEXTURE2D = 3;
    internal const int D3D12_TEXTURE_LAYOUT_UNKNOWN = 0;
    internal const int D3D12_TEXTURE_LAYOUT_ROW_MAJOR = 1;
    internal const uint D3D12_RESOURCE_FLAG_NONE = 0;
    internal const uint D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET = 0x1;
    internal const uint D3D12_RESOURCE_STATE_COMMON = 0;
    internal const uint D3D12_RESOURCE_STATE_RENDER_TARGET = 0x4;
    internal const uint D3D12_RESOURCE_STATE_COPY_DEST = 0x400;
    internal const uint D3D12_RESOURCE_STATE_COPY_SOURCE = 0x800;
    internal const int D3D12_RESOURCE_BARRIER_TYPE_TRANSITION = 0;
    internal const int D3D12_RESOURCE_BARRIER_FLAG_NONE = 0;
    internal const int D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX = 0;
    internal const int D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT = 1;
    internal const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28;
    internal const uint D3D12_TIMEOUT_INFINITE = 0xFFFFFFFF;

    // ---- structs (Sequential ones match the native C ABI layout directly; the two structs that
    // contain a native union - D3D12_TEXTURE_COPY_LOCATION and D3D12_RESOURCE_BARRIER - are
    // hand-flattened with explicit offsets taken from the probe described in this file's doc
    // comment, not derived by eye) ----

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D12_COMMAND_QUEUE_DESC
    {
        internal int Type;
        internal int Priority;
        internal int Flags;
        internal uint NodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D12_HEAP_PROPERTIES
    {
        internal int Type;
        internal int CPUPageProperty;
        internal int MemoryPoolPreference;
        internal uint CreationNodeMask;
        internal uint VisibleNodeMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DXGI_SAMPLE_DESC
    {
        internal uint Count;
        internal uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D12_RESOURCE_DESC
    {
        internal int Dimension;
        internal ulong Alignment;
        internal ulong Width;
        internal uint Height;
        internal ushort DepthOrArraySize;
        internal ushort MipLevels;
        internal uint Format;
        internal DXGI_SAMPLE_DESC SampleDesc;
        internal int Layout;
        internal uint Flags;
    }

    /// <summary>
    /// Flattened <c>D3D12_TEXTURE_COPY_LOCATION</c> - offsets 0/8/16/24/28/32/36/40 confirmed against
    /// real <c>sizeof</c>/field-offset output from <c>terrafx.interop.windows</c>, not computed by
    /// hand (see this file's doc comment). <c>SubresourceIndex</c> and the
    /// <c>PlacedFootprint*</c> fields alias the same bytes (offset 16) - set only the ones matching
    /// <see cref="Type"/>.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct D3D12_TEXTURE_COPY_LOCATION
    {
        [FieldOffset(0)] internal IntPtr pResource;
        [FieldOffset(8)] internal int Type;
        [FieldOffset(16)] internal uint SubresourceIndex;
        [FieldOffset(16)] internal ulong PlacedFootprintOffset;
        [FieldOffset(24)] internal uint PlacedFootprintFormat;
        [FieldOffset(28)] internal uint PlacedFootprintWidth;
        [FieldOffset(32)] internal uint PlacedFootprintHeight;
        [FieldOffset(36)] internal uint PlacedFootprintDepth;
        [FieldOffset(40)] internal uint PlacedFootprintRowPitch;
    }

    /// <summary>
    /// Flattened <c>D3D12_RESOURCE_BARRIER</c>, transition-variant only (this project never needs an
    /// aliasing or UAV barrier). Offsets confirmed the same way as
    /// <see cref="D3D12_TEXTURE_COPY_LOCATION"/> above.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct D3D12_RESOURCE_BARRIER
    {
        [FieldOffset(0)] internal int Type;
        [FieldOffset(4)] internal int Flags;
        [FieldOffset(8)] internal IntPtr TransitionResource;
        [FieldOffset(16)] internal uint TransitionSubresource;
        [FieldOffset(20)] internal uint TransitionStateBefore;
        [FieldOffset(24)] internal uint TransitionStateAfter;
    }

    // ---- free functions (plain exports, not COM vtable calls) ----

    [DllImport("d3d12.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern int D3D12CreateDevice(IntPtr pAdapter, int minimumFeatureLevel, in Guid riid, out IntPtr ppDevice);

    [DllImport("dxgi.dll", CallingConvention = CallingConvention.StdCall)]
    internal static extern int CreateDXGIFactory2(uint flags, in Guid riid, out IntPtr ppFactory);

    // ---- raw COM vtable calls. Every index below is the ABSOLUTE vtable slot (IUnknown's
    // QueryInterface/AddRef/Release occupy 0-2 on every interface), cross-checked against
    // terrafx.interop.windows as described in this file's doc comment. ----

    internal static IntPtr QueryInterface(IntPtr unknown, in Guid iid)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, Guid*, void**, int>)(*(void***)unknown)[0];
        void* result;
        fixed (Guid* iidPtr = &iid)
        {
            int hr = fn(unknown, iidPtr, &result);
            if (hr < 0)
                throw new InvalidOperationException($"QueryInterface({iid}) failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)result;
    }

    internal static uint Release(IntPtr unknown)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint>)(*(void***)unknown)[2];
        return fn(unknown);
    }

    /// <summary>IDXGIFactory4::EnumWarpAdapter, vtable slot 27.</summary>
    internal static IntPtr EnumWarpAdapter(IntPtr factory4, in Guid riid)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, Guid*, void**, int>)(*(void***)factory4)[27];
        void* adapter;
        fixed (Guid* riidPtr = &riid)
        {
            int hr = fn(factory4, riidPtr, &adapter);
            if (hr < 0)
                throw new InvalidOperationException($"IDXGIFactory4::EnumWarpAdapter failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)adapter;
    }

    /// <summary>ID3D12Device::CreateCommandQueue, vtable slot 8.</summary>
    internal static IntPtr CreateCommandQueue(IntPtr device, in D3D12_COMMAND_QUEUE_DESC desc, in Guid riid)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, D3D12_COMMAND_QUEUE_DESC*, Guid*, void**, int>)(*(void***)device)[8];
        void* queue;
        fixed (D3D12_COMMAND_QUEUE_DESC* descPtr = &desc)
        fixed (Guid* riidPtr = &riid)
        {
            int hr = fn(device, descPtr, riidPtr, &queue);
            if (hr < 0)
                throw new InvalidOperationException($"ID3D12Device::CreateCommandQueue failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)queue;
    }

    /// <summary>ID3D12Device::CreateCommandAllocator, vtable slot 9.</summary>
    internal static IntPtr CreateCommandAllocator(IntPtr device, int type, in Guid riid)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, int, Guid*, void**, int>)(*(void***)device)[9];
        void* allocator;
        fixed (Guid* riidPtr = &riid)
        {
            int hr = fn(device, type, riidPtr, &allocator);
            if (hr < 0)
                throw new InvalidOperationException($"ID3D12Device::CreateCommandAllocator failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)allocator;
    }

    /// <summary>ID3D12Device::CreateCommandList, vtable slot 12.</summary>
    internal static IntPtr CreateCommandList(IntPtr device, uint nodeMask, int type, IntPtr allocator, in Guid riid)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, int, IntPtr, IntPtr, Guid*, void**, int>)(*(void***)device)[12];
        void* list;
        fixed (Guid* riidPtr = &riid)
        {
            int hr = fn(device, nodeMask, type, allocator, IntPtr.Zero, riidPtr, &list);
            if (hr < 0)
                throw new InvalidOperationException($"ID3D12Device::CreateCommandList failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)list;
    }

    /// <summary>ID3D12Device::CreateCommittedResource, vtable slot 27.</summary>
    internal static IntPtr CreateCommittedResource(
        IntPtr device, in D3D12_HEAP_PROPERTIES heapProperties, uint heapFlags,
        in D3D12_RESOURCE_DESC desc, uint initialState, in Guid riid)
    {
        var fn = (delegate* unmanaged[MemberFunction]<
            IntPtr, D3D12_HEAP_PROPERTIES*, uint, D3D12_RESOURCE_DESC*, uint, IntPtr, Guid*, void**, int>)(*(void***)device)[27];
        void* resource;
        fixed (D3D12_HEAP_PROPERTIES* heapPtr = &heapProperties)
        fixed (D3D12_RESOURCE_DESC* descPtr = &desc)
        fixed (Guid* riidPtr = &riid)
        {
            int hr = fn(device, heapPtr, heapFlags, descPtr, initialState, IntPtr.Zero, riidPtr, &resource);
            if (hr < 0)
                throw new InvalidOperationException($"ID3D12Device::CreateCommittedResource failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)resource;
    }

    /// <summary>ID3D12Device::CreateFence, vtable slot 36.</summary>
    internal static IntPtr CreateFence(IntPtr device, ulong initialValue, int flags, in Guid riid)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, ulong, int, Guid*, void**, int>)(*(void***)device)[36];
        void* fence;
        fixed (Guid* riidPtr = &riid)
        {
            int hr = fn(device, initialValue, flags, riidPtr, &fence);
            if (hr < 0)
                throw new InvalidOperationException($"ID3D12Device::CreateFence failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)fence;
    }

    /// <summary>ID3D12CommandQueue::ExecuteCommandLists, vtable slot 10. Returns void on the native side.</summary>
    internal static void ExecuteCommandLists(IntPtr queue, IntPtr commandList)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, IntPtr*, void>)(*(void***)queue)[10];
        var local = commandList;
        fn(queue, 1, &local);
    }

    /// <summary>ID3D12CommandQueue::Signal, vtable slot 14.</summary>
    internal static void Signal(IntPtr queue, IntPtr fence, ulong value)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, ulong, int>)(*(void***)queue)[14];
        int hr = fn(queue, fence, value);
        if (hr < 0)
            throw new InvalidOperationException($"ID3D12CommandQueue::Signal failed. HRESULT: 0x{hr:X8}");
    }

    /// <summary>ID3D12GraphicsCommandList::Close, vtable slot 9.</summary>
    internal static void CloseCommandList(IntPtr list)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, int>)(*(void***)list)[9];
        int hr = fn(list);
        if (hr < 0)
            throw new InvalidOperationException($"ID3D12GraphicsCommandList::Close failed. HRESULT: 0x{hr:X8}");
    }

    /// <summary>ID3D12GraphicsCommandList::CopyTextureRegion, vtable slot 16. Returns void on the native side.</summary>
    internal static void CopyTextureRegion(
        IntPtr list, in D3D12_TEXTURE_COPY_LOCATION dst, uint dstX, uint dstY, uint dstZ,
        in D3D12_TEXTURE_COPY_LOCATION src)
    {
        var fn = (delegate* unmanaged[MemberFunction]<
            IntPtr, D3D12_TEXTURE_COPY_LOCATION*, uint, uint, uint, D3D12_TEXTURE_COPY_LOCATION*, IntPtr, void>)(*(void***)list)[16];
        fixed (D3D12_TEXTURE_COPY_LOCATION* dstPtr = &dst)
        fixed (D3D12_TEXTURE_COPY_LOCATION* srcPtr = &src)
        {
            fn(list, dstPtr, dstX, dstY, dstZ, srcPtr, IntPtr.Zero);
        }
    }

    /// <summary>ID3D12GraphicsCommandList::ResourceBarrier, vtable slot 26. Returns void on the native side.</summary>
    internal static void ResourceBarrier(IntPtr list, in D3D12_RESOURCE_BARRIER barrier)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, D3D12_RESOURCE_BARRIER*, void>)(*(void***)list)[26];
        fixed (D3D12_RESOURCE_BARRIER* barrierPtr = &barrier)
        {
            fn(list, 1, barrierPtr);
        }
    }

    /// <summary>ID3D12Resource::Map, vtable slot 8.</summary>
    internal static IntPtr Map(IntPtr resource, uint subresource)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, IntPtr, void**, int>)(*(void***)resource)[8];
        void* data;
        int hr = fn(resource, subresource, IntPtr.Zero, &data);
        if (hr < 0)
            throw new InvalidOperationException($"ID3D12Resource::Map failed. HRESULT: 0x{hr:X8}");
        return (IntPtr)data;
    }

    /// <summary>ID3D12Resource::Unmap, vtable slot 9. Returns void on the native side.</summary>
    internal static void Unmap(IntPtr resource, uint subresource)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, IntPtr, void>)(*(void***)resource)[9];
        fn(resource, subresource, IntPtr.Zero);
    }

    /// <summary>ID3D12Fence::GetCompletedValue, vtable slot 8.</summary>
    internal static ulong GetCompletedValue(IntPtr fence)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, ulong>)(*(void***)fence)[8];
        return fn(fence);
    }
}
