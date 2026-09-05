using System.Runtime.InteropServices;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Creates a D3D11 device backed by WARP (Windows' software D3D11 rasterizer, bundled with the OS)
/// so D3D11/ANGLE interop tests run on any CI runner without needing a real GPU.
/// </summary>
static class WarpDevice
{
    const int D3D_DRIVER_TYPE_WARP = 5;
    const uint D3D11_SDK_VERSION = 7;
    internal const int D3D_FEATURE_LEVEL_11_0 = 0xb000;

    [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
    static extern int D3D11CreateDevice(
        IntPtr pAdapter, int driverType, IntPtr software, uint flags,
        int[]? pFeatureLevels, uint featureLevels, uint sdkVersion,
        out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

    internal static (IntPtr device, IntPtr context) Create()
    {
        int hr = D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_WARP, IntPtr.Zero, 0,
            new[] { D3D_FEATURE_LEVEL_11_0 }, 1, D3D11_SDK_VERSION,
            out var device, out _, out var context);
        Assert.True(hr >= 0, $"D3D11CreateDevice (WARP) failed. HRESULT: 0x{hr:X8}");
        return (device, context);
    }
}
