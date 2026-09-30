using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.CoreOgl;

/// <summary>
/// Draws <see cref="GoldenScene"/> through one long-lived FBO-wrapped surface for hundreds of frames
/// on a real WGL context (llvmpipe in CI) and fails if the process grows per frame (see
/// <see cref="FrameLeakCheck"/>). GL has no object count to read back, so this checks memory only.
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class WglPerFrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_GrowsNothing_ThroughRealWglContext()
    {
        using var context = new WglContext();
        var loader = new WglFunctionLoader();
        var raw = new GlRawTestFunctions(loader);
        output.WriteLine($"GL_RENDERER: {raw.GetStringUtf8(GlRawTestFunctions.GL_RENDERER)}");

        raw.GenTextures(1, out var textureId);
        try
        {
            raw.BindTexture(GlRawTestFunctions.GL_TEXTURE_2D, textureId);
            unsafe
            {
                raw.TexImage2D(GlRawTestFunctions.GL_TEXTURE_2D, 0, GlRawTestFunctions.GL_RGBA8,
                    GoldenScene.Width, GoldenScene.Height, 0,
                    GlRawTestFunctions.GL_RGBA, GlRawTestFunctions.GL_UNSIGNED_BYTE, null);
            }

            var gl = GlFunctions.Load(loader);
            using var grContext = GlGrContextFactory.Create(gl);
            var (surface, renderTarget) = GlSkiaSurfaceFactory.CreateSurface(
                grContext, gl, textureId, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888,
                out var framebufferState);
            try
            {
                FrameLeakCheck.AssertNoPerFrameGrowth(() =>
                {
                    grContext.ResetContext();
                    GlSkiaSurfaceFactory.BindForDrawing(gl, framebufferState);
                    GoldenScene.Draw(surface.Canvas);
                    surface.Flush();
                    GlSkiaSurfaceFactory.UnbindAfterDrawing(gl);
                }, output);
            }
            finally
            {
                surface.Dispose();
                renderTarget.Dispose();
                GlSkiaSurfaceFactory.DisposeRenderState(gl, framebufferState);
            }
        }
        finally
        {
            raw.DeleteTextures(1, ref textureId);
        }
    }
}
