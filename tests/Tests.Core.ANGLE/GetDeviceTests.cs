using SkiaGameRendering.Core.ANGLE;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Exercises <c>D3D11Com.GetDevice</c> (ID3D11DeviceChild vtable slot 3) on WARP. The Unity adapter
/// finds Unity's device this way, from a texture, so a wrong slot would corrupt the stack there.
/// </summary>
public sealed class GetDeviceTests
{
    [Fact]
    public void GetDevice_OnTexture_ReturnsOwningDevice_OnWarp()
    {
        var (device, context) = WarpDevice.Create();
        var texture = D3D11RawResources.CreateTexture2D(device, new D3D11RawResources.Texture2DDesc
        {
            Width = 4,
            Height = 4,
            MipLevels = 1,
            ArraySize = 1,
            Format = D3D11RawResources.DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleCount = 1,
            Usage = D3D11RawResources.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11RawResources.D3D11_BIND_RENDER_TARGET,
        });
        try
        {
            var owner = D3D11Com.GetDevice(texture);
            try
            {
                Assert.Equal(device, owner);
            }
            finally
            {
                D3D11Com.Release(owner);
            }
        }
        finally
        {
            D3D11Com.Release(texture);
            D3D11Com.Release(context);
            D3D11Com.Release(device);
        }
    }
}
