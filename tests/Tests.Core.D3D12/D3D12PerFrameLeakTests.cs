using System.Runtime.InteropServices;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using static Tests.CoreD3D12.D3D12TestNative;

namespace Tests.CoreD3D12;

/// <summary>
/// Draws <see cref="GoldenScene"/> through one long-lived surface for hundreds of frames on WARP and
/// fails if anything grows per frame (see <see cref="FrameLeakCheck"/>). Each frame is the Godot
/// backend's: flush without waiting, then copy Skia's resource into the host's texture through
/// <see cref="D3D12ResourceTransitioner"/>. The device's reference count is the exact check: every
/// D3D12 object holds a reference on its device, so a command list, allocator, fence or resource
/// created per frame and never released shows up there.
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class D3D12PerFrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_CopiedToHostTexture_GrowsNothing_OnWarp()
    {
        using var d3d12 = new D3D12TestDevice();
        var skiaResource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(
            d3d12.Device, GoldenScene.Width, GoldenScene.Height, DXGI_FORMAT_R8G8B8A8_UNORM);
        // Stands in for Godot's own texture, which it keeps in the pixel-shader-resource state.
        var hostTexture = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(
            d3d12.Device, GoldenScene.Width, GoldenScene.Height, DXGI_FORMAT_R8G8B8A8_UNORM);
        try
        {
            using var transitioner = new D3D12ResourceTransitioner(d3d12.Device, d3d12.Queue);
            transitioner.Transition(hostTexture, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12Constants.ResourceStatePixelShaderResource);

            using var factory = new D3D12SkiaSurfaceFactory();
            factory.InitializeFromNative(d3d12.Adapter, d3d12.Device, d3d12.Queue);
            var state = factory.CreateTextureState(skiaResource, DXGI_FORMAT_R8G8B8A8_UNORM, D3D12_RESOURCE_STATE_RENDER_TARGET);
            factory.BeginDraw();
            var (surface, renderTarget) = factory.CreateSurface(state, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888);
            factory.EndDraw();

            int frame = 0;
            try
            {
                FrameLeakCheck.AssertNoPerFrameGrowth(() =>
                {
                    factory.BeginDraw();
                    GoldenScene.Draw(surface.Canvas);
                    surface.Flush();
                    factory.EndDraw(synchronous: false);
                    transitioner.CopyWithTransitions(
                        hostTexture, D3D12Constants.ResourceStatePixelShaderResource, D3D12Constants.ResourceStatePixelShaderResource,
                        skiaResource, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_RENDER_TARGET);
                    // An engine never runs more than a couple of frames ahead of the GPU; without
                    // this the CPU would queue unboundedly many, which looks like growth but is not.
                    if (++frame % 2 == 0)
                        transitioner.WaitForCompletion();
                }, output, counters: [("device refs", () => RefCount(d3d12.Device))]);
                output.WriteLine($"Transition ring: {transitioner.SlotCount} slots.");
            }
            finally
            {
                transitioner.WaitForCompletion();
                factory.BeginDraw();
                surface.Dispose();
                renderTarget.Dispose();
                factory.EndDraw();
            }
        }
        finally
        {
            D3D12SkiaSurfaceFactory.ReleaseResource(hostTexture);
            D3D12SkiaSurfaceFactory.ReleaseResource(skiaResource);
        }
    }

    static long RefCount(IntPtr unknown)
    {
        Marshal.AddRef(unknown);
        return Marshal.Release(unknown);
    }
}
