using Xunit;
using static Tests.CoreD3D12.D3D12TestNative;

namespace Tests.CoreD3D12;

/// <summary>
/// Creates a real, minimal <c>ID3D12Device</c> + <c>ID3D12CommandQueue</c> backed by WARP
/// (Microsoft's software D3D12 rasterizer, bundled with every Windows install) so
/// <c>D3D12SkiaSurfaceFactory</c> interop tests run on any CI runner without needing a real GPU -
/// the D3D12 analog of <c>Tests.Core.ANGLE</c>'s <c>WarpDevice</c> (D3D11) and
/// <c>Tests.Core.VK</c>'s <c>VulkanTestDevice</c> (Vulkan/lavapipe). See the
/// <c>headless-gpu-testing</c> skill.
/// <para>
/// Unlike D3D11's <c>D3D11CreateDevice(..., D3D_DRIVER_TYPE_WARP, ...)</c>, D3D12 has no
/// driver-type enum for this - WARP is just another DXGI adapter, found via
/// <c>IDXGIFactory4::EnumWarpAdapter</c> and handed to <c>D3D12CreateDevice</c> like any other.
/// </para>
/// </summary>
internal sealed class D3D12TestDevice : IDisposable
{
    internal IntPtr Adapter { get; }
    internal IntPtr Device { get; }
    internal IntPtr Queue { get; }

    internal D3D12TestDevice()
    {
        var factory = IntPtr.Zero;
        try
        {
            int hr = CreateDXGIFactory2(0, IID_IDXGIFactory4, out factory);
            Assert.True(hr >= 0, $"CreateDXGIFactory2 failed. HRESULT: 0x{hr:X8}");

            Adapter = EnumWarpAdapter(factory, IID_IDXGIAdapter1);

            hr = D3D12CreateDevice(Adapter, D3D_FEATURE_LEVEL_11_0, IID_ID3D12Device, out var device);
            Assert.True(hr >= 0, $"D3D12CreateDevice (WARP) failed. HRESULT: 0x{hr:X8}");
            Device = device;

            var queueDesc = new D3D12_COMMAND_QUEUE_DESC
            {
                Type = D3D12_COMMAND_LIST_TYPE_DIRECT,
                Priority = 0,
                Flags = D3D12_COMMAND_QUEUE_FLAG_NONE,
                NodeMask = 0,
            };
            Queue = CreateCommandQueue(Device, queueDesc, IID_ID3D12CommandQueue);
        }
        finally
        {
            if (factory != IntPtr.Zero)
                Release(factory);
        }
    }

    public void Dispose()
    {
        if (Queue != IntPtr.Zero)
            Release(Queue);
        if (Device != IntPtr.Zero)
            Release(Device);
        if (Adapter != IntPtr.Zero)
            Release(Adapter);
    }
}
