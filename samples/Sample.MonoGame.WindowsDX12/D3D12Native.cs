using System.Runtime.InteropServices;

namespace Sample.MonoGame.WindowsDX12;

/// <summary>
/// Just enough raw D3D12 P/Invoke to allocate a render-target-capable <c>ID3D12Resource</c> on a
/// device handed to us by MonoGame (via <c>GraphicsDevice.GetNativeHandles()</c>, MonoGame/MonoGame#9536).
/// Trimmed from <c>tests/Tests.Core.D3D12/D3D12TestNative.cs</c> - see that file's doc comment for how
/// the vtable slots and struct layouts here were verified (cross-checked against
/// <c>terrafx.interop.windows</c>, not taken from memory).
/// </summary>
internal static unsafe class D3D12Native
{
    internal static readonly Guid IID_ID3D12Resource = new("696442be-a72e-4059-bc79-5b5c98040fad");

    internal const int D3D12_HEAP_TYPE_DEFAULT = 1;
    internal const int D3D12_CPU_PAGE_PROPERTY_UNKNOWN = 0;
    internal const int D3D12_RESOURCE_DIMENSION_TEXTURE2D = 3;
    internal const int D3D12_TEXTURE_LAYOUT_UNKNOWN = 0;
    internal const uint D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET = 0x1;
    internal const uint D3D12_RESOURCE_STATE_RENDER_TARGET = 0x4;
    internal const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28;
    internal const uint D3D12_HEAP_FLAG_NONE = 0;

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

    /// <summary>ID3D12Device::CreateCommittedResource, vtable slot 27.</summary>
    static IntPtr CreateCommittedResource(
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

    /// <summary>IUnknown::Release, vtable slot 2.</summary>
    internal static uint Release(IntPtr unknown)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint>)(*(void***)unknown)[2];
        return fn(unknown);
    }

    /// <summary>
    /// Allocates an <c>ID3D12Resource</c> on <paramref name="device"/> (MonoGame's own device, from
    /// <c>NativeGraphicsHandles.LogicalDevice</c>) with <c>D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET</c>,
    /// starting in <c>D3D12_RESOURCE_STATE_RENDER_TARGET</c> so it is immediately usable by
    /// <c>D3D12SkiaSurfaceFactory.CreateTextureState</c>.
    /// </summary>
    internal static IntPtr CreateRenderTargetResource(IntPtr device, int width, int height)
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
            MemoryPoolPreference = 0,
            CreationNodeMask = 0,
            VisibleNodeMask = 0,
        };

        return CreateCommittedResource(
            device, heapProperties, D3D12_HEAP_FLAG_NONE, resourceDesc,
            D3D12_RESOURCE_STATE_RENDER_TARGET, IID_ID3D12Resource);
    }
}
