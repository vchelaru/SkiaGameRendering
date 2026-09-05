using SkiaGameRendering.Core.ANGLE;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Exercises <c>D3D11Com.GetImmediateContext</c> (ID3D11Device vtable slot 40) against a real D3D11
/// device, without needing a GPU (see <see cref="WarpDevice"/>).
///
/// Added for the Stride adapter: Stride's <c>GraphicsDevice.NativeDevice</c> is public, but its
/// immediate context accessor isn't, so the adapter gets the context from the device itself over
/// COM instead of reflecting a private Stride field. A wrong vtable index here corrupts the stack
/// instead of failing loudly, same risk as the D3D11.1 calls <see cref="D3D11StateSwapTests"/> covers.
/// </summary>
public sealed class GetImmediateContextTests
{
    [Fact]
    public void GetImmediateContext_ReturnsSameContext_OnWarp()
    {
        var (device, context) = WarpDevice.Create();
        try
        {
            // WarpDevice.Create() already got the immediate context via D3D11CreateDevice's own
            // ppImmediateContext out-parameter - GetImmediateContext must hand back that exact same
            // COM identity, which is only true if vtable slot 40 has the right signature.
            var immediateContext = D3D11Com.GetImmediateContext(device);
            try
            {
                Assert.Equal(context, immediateContext);
            }
            finally
            {
                D3D11Com.Release(immediateContext);
            }
        }
        finally
        {
            D3D11Com.Release(context);
            D3D11Com.Release(device);
        }
    }
}
