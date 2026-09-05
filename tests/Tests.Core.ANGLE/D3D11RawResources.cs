using System.Runtime.InteropServices;

namespace Tests.CoreAngle;

/// <summary>
/// The handful of raw D3D11 vtable calls <c>AngleSkiaPixelReadbackTests</c> needs to create a
/// render-target texture and read its pixels back to CPU - test scaffolding only, not part of the
/// production interop in <c>SkiaGameRendering.Core.ANGLE</c> (see <c>D3D11Com</c> there).
///
/// Vtable slot indices are cross-checked against github.com/terrafx/terrafx.interop.windows the
/// same way <c>D3D11Com</c>'s are: ID3D11Device::CreateTexture2D is slot 5, ID3D11DeviceContext::Map
/// is slot 14, ::Unmap is slot 15, ::RSSetViewports is slot 44, ::CopyResource is slot 47,
/// ::RSGetViewports is slot 95 (all on the base, non-.1 interfaces, which the .1 pointers this test
/// passes in satisfy too since ID3D11Device1/DeviceContext1 only append slots after their base
/// interface's).
/// </summary>
static unsafe class D3D11RawResources
{
    internal const int DXGI_FORMAT_R8G8B8A8_UNORM = 28;

    internal const uint D3D11_USAGE_DEFAULT = 0;
    internal const uint D3D11_USAGE_STAGING = 3;

    internal const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    internal const uint D3D11_BIND_RENDER_TARGET = 0x20;

    internal const uint D3D11_CPU_ACCESS_READ = 0x20000;

    const uint D3D11_MAP_READ = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public uint SampleCount;
        public uint SampleQuality;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    /// <summary>D3D11_VIEWPORT. Used as a marker value to check the context state survives a Skia draw.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Viewport
    {
        public float TopLeftX;
        public float TopLeftY;
        public float Width;
        public float Height;
        public float MinDepth;
        public float MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MappedSubresource
    {
        public void* pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    /// <summary>ID3D11Device::CreateTexture2D, vtable slot 5.</summary>
    internal static IntPtr CreateTexture2D(IntPtr device, in Texture2DDesc desc)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, Texture2DDesc*, void*, void**, int>)
            (*(void***)device)[5];

        void* texture;
        fixed (Texture2DDesc* descPtr = &desc)
        {
            int hr = fn(device, descPtr, null, &texture);
            if (hr < 0)
                throw new InvalidOperationException($"ID3D11Device::CreateTexture2D failed. HRESULT: 0x{hr:X8}");
        }
        return (IntPtr)texture;
    }

    /// <summary>ID3D11DeviceContext::CopyResource, vtable slot 47.</summary>
    internal static void CopyResource(IntPtr context, IntPtr dst, IntPtr src)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, IntPtr, void>)(*(void***)context)[47];
        fn(context, dst, src);
    }

    /// <summary>ID3D11DeviceContext::RSSetViewports, vtable slot 44.</summary>
    internal static void SetViewport(IntPtr context, in Viewport viewport)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, Viewport*, void>)(*(void***)context)[44];
        fixed (Viewport* viewportPtr = &viewport)
            fn(context, 1, viewportPtr);
    }

    /// <summary>
    /// ID3D11DeviceContext::RSGetViewports, vtable slot 95. Returns null when the context has no
    /// viewport bound at all, which is distinct from having a different one bound.
    /// </summary>
    internal static Viewport? GetViewport(IntPtr context)
    {
        var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint*, Viewport*, void>)(*(void***)context)[95];

        Viewport viewport;
        uint count = 1;
        fn(context, &count, &viewport);
        return count == 0 ? null : viewport;
    }

    /// <summary>
    /// ID3D11DeviceContext::Map (slot 14) for D3D11_MAP_READ on subresource 0, then hands the
    /// mapped bytes to <paramref name="read"/> before Unmap-ping (slot 15) unconditionally.
    /// </summary>
    internal static void MapReadAndCopy(IntPtr context, IntPtr resource, Action<IntPtr, uint> read)
    {
        var mapFn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, uint, uint, uint, MappedSubresource*, int>)
            (*(void***)context)[14];
        var unmapFn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, uint, void>)(*(void***)context)[15];

        MappedSubresource mapped;
        int hr = mapFn(context, resource, 0, D3D11_MAP_READ, 0, &mapped);
        if (hr < 0)
            throw new InvalidOperationException($"ID3D11DeviceContext::Map failed. HRESULT: 0x{hr:X8}");
        try
        {
            read((IntPtr)mapped.pData, mapped.RowPitch);
        }
        finally
        {
            unmapFn(context, resource, 0);
        }
    }
}
