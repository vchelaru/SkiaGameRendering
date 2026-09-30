using System.Runtime.InteropServices;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// A disposed <see cref="AngleSkiaSurfaceFactory"/> must give back every reference it took on the
/// host's D3D11 device. The ANGLE display, GL context and D3D11 state objects each hold one, so a
/// host that recreates the factory against the same device (Unity does on every domain reload)
/// leaks them all otherwise.
/// </summary>
public sealed class AngleFactoryDisposeTests
{
    [Fact]
    public void Dispose_ReleasesEveryDeviceReference()
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
            BindFlags = D3D11RawResources.D3D11_BIND_RENDER_TARGET | D3D11RawResources.D3D11_BIND_SHADER_RESOURCE,
        });
        try
        {
            int before = RefCount(device);
            for (int i = 0; i < 3; i++)
            {
                var factory = new AngleSkiaSurfaceFactory();
                factory.InitializeFromNative(device, context);
                factory.BeginDraw();
                var state = factory.CreateTextureState(texture);
                var (surface, renderTarget) = factory.CreateSurface(state, 4, 4, SKColorType.Rgba8888);
                surface.Canvas.Clear(SKColors.Red);
                surface.Flush();
                factory.UnbindAfterDrawing();
                factory.EndDraw();
                surface.Dispose();
                renderTarget.Dispose();
                factory.DisposeRenderState(state);
                factory.Dispose();

                Assert.Equal(before, RefCount(device));
            }
        }
        finally
        {
            Marshal.Release(texture);
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    static int RefCount(IntPtr unknown)
    {
        Marshal.AddRef(unknown);
        return Marshal.Release(unknown);
    }
}
