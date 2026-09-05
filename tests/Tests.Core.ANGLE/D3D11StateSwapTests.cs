using SkiaGameRendering.Core.ANGLE;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Exercises <c>D3D11Com</c>'s QueryInterface/CreateDeviceContextState/SwapDeviceContextState
/// vtable calls against a real D3D11 device, without needing a GPU (see <see cref="WarpDevice"/>).
///
/// This is the risky half of <c>AngleSkiaSurfaceFactory</c>'s state swap - a wrong vtable index or
/// argument order here corrupts the stack instead of failing loudly. It doesn't prove Skia still
/// draws through it end to end; <c>AngleSkiaPixelReadbackTests</c> covers that.
/// </summary>
public sealed class D3D11StateSwapTests
{
    [Fact]
    public void QueryInterface_UnsupportedInterface_Throws()
    {
        var (device, context) = WarpDevice.Create();
        try
        {
            var bogusIid = Guid.NewGuid();
            Assert.Throws<InvalidOperationException>(() => D3D11Com.QueryInterface(device, bogusIid));
        }
        finally
        {
            D3D11Com.Release(context);
            D3D11Com.Release(device);
        }
    }

    [Fact]
    public void CreateDeviceContextState_SwapDeviceContextState_RoundTrips_OnWarp()
    {
        var (device, context) = WarpDevice.Create();
        var device1 = D3D11Com.QueryInterface(device, D3D11Com.IID_ID3D11Device1);
        var context1 = D3D11Com.QueryInterface(context, D3D11Com.IID_ID3D11DeviceContext1);
        var emptyState = D3D11Com.CreateDeviceContextState(device1);
        try
        {
            // Swap to the empty state, capturing whatever the device's original state was.
            var originalState = D3D11Com.SwapDeviceContextState(context1, emptyState);
            try
            {
                // Swap back: this must hand back the exact state pointer we swapped to above,
                // which is only true if vtable slot 131 has the right signature/argument order.
                var restored = D3D11Com.SwapDeviceContextState(context1, originalState);
                Assert.Equal(emptyState, restored);
            }
            finally
            {
                if (originalState != IntPtr.Zero)
                    D3D11Com.Release(originalState);
            }
        }
        finally
        {
            D3D11Com.Release(emptyState);
            D3D11Com.Release(context1);
            D3D11Com.Release(device1);
            D3D11Com.Release(context);
            D3D11Com.Release(device);
        }
    }
}
