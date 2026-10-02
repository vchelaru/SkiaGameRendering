using System.Runtime.InteropServices;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Two factories, each on its own thread and device, creating, drawing and disposing in lockstep.
/// ANGLE's shader compiler is not safe to drive from two displays at once, so without the
/// factory's shared lock and on-thread compiles this crashes the test host most runs (issue #124).
/// </summary>
public sealed class AngleFactoryConcurrencyTests
{
    [Fact]
    public void TwoThreads_CreateDrawAndDispose_DoNotCrash()
    {
        const int threadCount = 2;
        const int rounds = 30;
        using var barrier = new Barrier(threadCount);
        var errors = new Exception?[threadCount];
        var threads = new Thread[threadCount];
        for (int t = 0; t < threadCount; t++)
        {
            int index = t;
            threads[t] = new Thread(() =>
            {
                try
                {
                    Run(barrier, rounds);
                }
                catch (Exception ex)
                {
                    errors[index] = ex;
                    barrier.RemoveParticipant();
                }
            });
            threads[t].Start();
        }
        foreach (var thread in threads)
            thread.Join();

        Assert.All(errors, Assert.Null);
    }

    static void Run(Barrier barrier, int rounds)
    {
        var (device, context) = WarpDevice.Create();
        var texture = D3D11RawResources.CreateTexture2D(device, new D3D11RawResources.Texture2DDesc
        {
            Width = 16,
            Height = 16,
            MipLevels = 1,
            ArraySize = 1,
            Format = D3D11RawResources.DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleCount = 1,
            Usage = D3D11RawResources.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11RawResources.D3D11_BIND_RENDER_TARGET | D3D11RawResources.D3D11_BIND_SHADER_RESOURCE,
        });
        try
        {
            for (int i = 0; i < rounds; i++)
            {
                // Lines both threads up so their shader compiles and their teardowns overlap.
                barrier.SignalAndWait();
                using var factory = new AngleSkiaSurfaceFactory();
                factory.InitializeFromNative(device, context);
                factory.BeginDraw();
                var state = factory.CreateTextureState(texture);
                var (surface, renderTarget) = factory.CreateSurface(state, 16, 16, SKColorType.Rgba8888);
                using (var paint = new SKPaint { Color = SKColors.Blue, IsAntialias = true })
                {
                    surface.Canvas.Clear(SKColors.Red);
                    surface.Canvas.DrawCircle(8, 8, 5, paint);
                }
                surface.Flush();
                factory.UnbindAfterDrawing();
                factory.EndDraw();
                surface.Dispose();
                renderTarget.Dispose();
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
}
