using SkiaGameRendering.Core.Metal;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using static Tests.CoreMetal.MetalTestNative;

namespace Tests.CoreMetal;

/// <summary>
/// Draws <see cref="GoldenScene"/> into one long-lived surface over a host-owned <c>MTLTexture</c> for
/// hundreds of frames and fails if the process grows per frame (see <see cref="FrameLeakCheck"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class MetalPerFrameLeakTests(ITestOutputHelper output)
{
    [MacOnlyFact]
    public void DrawingFrames_GrowsNothing()
    {
        using var metal = new MetalTestDevice();
        var texture = metal.CreateTexture(GoldenScene.Width, GoldenScene.Height, MTLPixelFormatRGBA8Unorm,
            MTLTextureUsageRenderTarget | MTLTextureUsageShaderRead);
        try
        {
            using var factory = new MetalSkiaSurfaceFactory();
            factory.InitializeFromNative(metal.Device, metal.Queue);
            var state = factory.CreateTextureState(texture);
            var (surface, renderTarget) = factory.CreateSurface(state, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888);
            try
            {
                FrameLeakCheck.AssertNoPerFrameGrowth(() =>
                {
                    factory.BeginDraw();
                    GoldenScene.Draw(surface.Canvas);
                    surface.Flush();
                    factory.EndDraw();
                }, output);
            }
            finally
            {
                surface.Dispose();
                renderTarget.Dispose();
            }
        }
        finally
        {
            objc_release(texture);
        }
    }
}
