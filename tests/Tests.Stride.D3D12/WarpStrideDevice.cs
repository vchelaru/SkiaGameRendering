using Stride.Graphics;

namespace Tests.StrideD3D12;

/// <summary>
/// A headless Stride D3D12 <see cref="GraphicsDevice"/> on WARP. Stride's D3D12
/// <c>GraphicsAdapterFactory</c> enumerates only the WARP adapter when
/// <c>STRIDE_GRAPHICS_SOFTWARE_RENDERING=1</c>, the same switch <c>Tests.Stride.D3D11</c> uses. The
/// adapter list is cached on first use, so every test in this assembly sets it before creating a
/// device; that makes the goldens comparable on a dev box with a real GPU as well as on CI.
/// </summary>
static class WarpStrideDevice
{
    internal static GraphicsDevice Create(ColorSpace colorSpace)
    {
        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", "1");
        var device = GraphicsDevice.New();
        device.ColorSpace = colorSpace;
        return device;
    }
}
