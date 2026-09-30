using System.Reflection;
using System.Runtime.InteropServices;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.CoreAngle;

/// <summary>
/// Draws <see cref="GoldenScene"/> through one long-lived <see cref="AngleSkiaSurfaceFactory"/> and
/// surface for hundreds of frames on WARP, the way an engine does, and fails if anything grows per
/// frame (see <see cref="FrameLeakCheck"/>). The reference counts are the exact checks: every
/// <c>SwapDeviceContextState</c> hands back a state with a reference the caller owns, so a missed
/// <c>Release</c> shows up as the empty state's count climbing by one a frame.
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class AnglePerFrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_GrowsNothing_OnWarp()
    {
        var (device, context) = WarpDevice.Create();
        var texture = D3D11RawResources.CreateTexture2D(device, new D3D11RawResources.Texture2DDesc
        {
            Width = GoldenScene.Width,
            Height = GoldenScene.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = D3D11RawResources.DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleCount = 1,
            Usage = D3D11RawResources.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11RawResources.D3D11_BIND_RENDER_TARGET | D3D11RawResources.D3D11_BIND_SHADER_RESOURCE,
        });
        try
        {
            using var factory = new AngleSkiaSurfaceFactory();
            factory.InitializeFromNative(device, context);
            var emptyState = (IntPtr)(typeof(AngleSkiaSurfaceFactory)
                .GetField("_emptyState", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(nameof(AngleSkiaSurfaceFactory), "_emptyState")).GetValue(factory)!;

            factory.BeginDraw();
            var state = factory.CreateTextureState(texture);
            var (surface, renderTarget) = factory.CreateSurface(state, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888);
            factory.EndDraw();
            try
            {
                FrameLeakCheck.AssertNoPerFrameGrowth(() =>
                {
                    factory.BeginDraw();
                    factory.BindForDrawing(state);
                    GoldenScene.Draw(surface.Canvas);
                    surface.Flush();
                    factory.UnbindAfterDrawing();
                    factory.EndDraw();
                }, output, counters:
                [
                    ("device refs", () => RefCount(device)),
                    ("context refs", () => RefCount(context)),
                    ("empty-state refs", () => RefCount(emptyState)),
                ]);
            }
            finally
            {
                // The surface's GL objects are freed with the context current, which only
                // BindForDrawing makes it; EndDraw releases it again.
                factory.BeginDraw();
                factory.BindForDrawing(state);
                surface.Dispose();
                renderTarget.Dispose();
                factory.EndDraw();
                factory.DisposeRenderState(state);
            }
        }
        finally
        {
            Marshal.Release(texture);
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    static long RefCount(IntPtr unknown)
    {
        Marshal.AddRef(unknown);
        return Marshal.Release(unknown);
    }
}
