using System.Runtime.InteropServices;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Draws through the full <c>Core.ANGLE</c> pipeline - EGL device/surface setup, the D3D11.1 state
/// swap, Skia's GL ES backend - into a real D3D11 texture on WARP (see <see cref="WarpDevice"/>),
/// then reads the texture back to CPU and checks the pixels. No window or GPU needed: the target is
/// an offscreen texture, the same shape <c>SkiaRenderTarget2D</c> uses, not a swapchain backbuffer.
///
/// This is the piece <see cref="D3D11StateSwapTests"/> can't cover: proof that Skia's draw calls
/// actually land in the shared D3D11 texture, not just that the state-swap vtable calls succeed.
/// It still can't stand in for a manual GPU run of Sample.MonoGame.WindowsDX /
/// Sample.Kni.WindowsDX - WARP is a software rasterizer, so a real driver could still misbehave in
/// ways this never exercises.
/// </summary>
public sealed class AngleSkiaPixelReadbackTests
{
    [Fact]
    public void Clear_WritesExpectedColor_ThroughAngleOnWarp()
    {
        const int width = 4;
        const int height = 4;
        var expected = new SKColor(10, 20, 30, 255);

        var (device, context) = WarpDevice.Create();
        using var factory = new AngleSkiaSurfaceFactory();
        var texture = IntPtr.Zero;
        var staging = IntPtr.Zero;
        try
        {
            factory.InitializeFromNative(device, context);

            var textureDesc = new D3D11RawResources.Texture2DDesc
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = D3D11RawResources.DXGI_FORMAT_R8G8B8A8_UNORM,
                SampleCount = 1,
                SampleQuality = 0,
                Usage = D3D11RawResources.D3D11_USAGE_DEFAULT,
                BindFlags = D3D11RawResources.D3D11_BIND_RENDER_TARGET | D3D11RawResources.D3D11_BIND_SHADER_RESOURCE,
            };
            texture = D3D11RawResources.CreateTexture2D(device, textureDesc);

            factory.BeginDraw();
            var state = factory.CreateTextureState(texture);
            var (surface, renderTarget) = factory.CreateSurface(state, width, height, SKColorType.Rgba8888);
            surface.Canvas.Clear(expected);
            surface.Flush();
            factory.UnbindAfterDrawing();
            factory.EndDraw();
            renderTarget.Dispose();
            surface.Dispose();
            factory.DisposeRenderState(state);

            var stagingDesc = textureDesc;
            stagingDesc.Usage = D3D11RawResources.D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = D3D11RawResources.D3D11_CPU_ACCESS_READ;
            staging = D3D11RawResources.CreateTexture2D(device, stagingDesc);
            D3D11RawResources.CopyResource(context, staging, texture);

            var pixel = new byte[4];
            D3D11RawResources.MapReadAndCopy(context, staging, (data, _) => Marshal.Copy(data, pixel, 0, 4));

            Assert.Equal([expected.Red, expected.Green, expected.Blue, expected.Alpha], pixel);
        }
        finally
        {
            if (staging != IntPtr.Zero)
                D3D11Com.Release(staging);
            if (texture != IntPtr.Zero)
                D3D11Com.Release(texture);
            D3D11Com.Release(context);
            D3D11Com.Release(device);
        }
    }
}
