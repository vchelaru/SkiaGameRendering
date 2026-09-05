using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Tests.CoreOgl;

/// <summary>
/// Draws through the full <c>Core.OGL</c> pipeline - a real WGL context (see
/// <see cref="WglContext"/>), the real FBO setup in <see cref="GlSkiaSurfaceFactory"/>, Skia's GL
/// backend - into a real GL texture, then reads the texture back to CPU and checks the pixels. No
/// window is ever shown and no engine is involved: the target is an offscreen texture wrapped in an
/// FBO, the same shape <c>SkiaRenderTarget2D</c> uses, not a swapchain backbuffer.
/// <para>
/// This is the piece <c>GlSkiaSurfaceFactoryTests</c> can't cover: proof that Skia's draw calls
/// actually land in a real GL texture on a real driver, not just that the FBO call sequence is
/// correct against a fake loader. Unlike <c>Tests.Core.ANGLE</c>'s WARP-backed
/// <c>AngleSkiaPixelReadbackTests</c>, WGL has no first-party software rasterizer to force - on a
/// dev box with a GPU this runs against the real driver; the CI-relevant path (a software
/// rasterizer with no GPU, matching GitHub's <c>windows-latest</c> runner) is only exercised when a
/// vendored <c>opengl32.dll</c> sits next to the test binary, per the <c>headless-gpu-testing</c>
/// skill. <see cref="Clear_WritesExpectedColor_ThroughRealWglContext"/> passes either way; it prints
/// <c>GL_RENDERER</c> so a run can be checked against which driver actually loaded.
/// </para>
/// </summary>
public sealed class WglSkiaPixelReadbackTests
{
    readonly ITestOutputHelper _output;

    public WglSkiaPixelReadbackTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public unsafe void Clear_WritesExpectedColor_ThroughRealWglContext()
    {
        const int width = 4;
        const int height = 4;
        var expected = new SKColor(10, 20, 30, 255);

        using var context = new WglContext();
        var loader = new WglFunctionLoader();
        var raw = new GlRawTestFunctions(loader);

        _output.WriteLine($"GL_VENDOR: {raw.GetStringUtf8(GlRawTestFunctions.GL_VENDOR)}");
        _output.WriteLine($"GL_RENDERER: {raw.GetStringUtf8(GlRawTestFunctions.GL_RENDERER)}");
        _output.WriteLine($"GL_VERSION: {raw.GetStringUtf8(GlRawTestFunctions.GL_VERSION)}");

        raw.GenTextures(1, out var textureId);
        try
        {
            raw.BindTexture(GlRawTestFunctions.GL_TEXTURE_2D, textureId);
            raw.TexImage2D(GlRawTestFunctions.GL_TEXTURE_2D, 0, GlRawTestFunctions.GL_RGBA8,
                width, height, 0, GlRawTestFunctions.GL_RGBA, GlRawTestFunctions.GL_UNSIGNED_BYTE, null);

            var gl = GlFunctions.Load(loader);
            using var grContext = GRContext.CreateGl()
                ?? throw new InvalidOperationException("GRContext.CreateGl() returned null.");
            grContext.ResetContext();

            var (surface, renderTarget) = GlSkiaSurfaceFactory.CreateSurface(
                grContext, gl, textureId, width, height, SKColorType.Rgba8888, out var framebufferState);

            GlSkiaSurfaceFactory.BindForDrawing(gl, framebufferState);
            surface.Canvas.Clear(expected);
            surface.Flush();

            // glReadPixels reads the currently bound framebuffer, unlike the ANGLE/D3D11 test's
            // CopyResource (a texture-level op independent of any binding) - so the readback has to
            // happen before UnbindAfterDrawing, not after like the D3D11 pattern.
            var pixel = new byte[4];
            fixed (byte* pixelPtr = pixel)
            {
                raw.ReadPixels(0, 0, 1, 1, GlRawTestFunctions.GL_RGBA, GlRawTestFunctions.GL_UNSIGNED_BYTE, pixelPtr);
            }

            GlSkiaSurfaceFactory.UnbindAfterDrawing(gl);

            renderTarget.Dispose();
            surface.Dispose();
            GlSkiaSurfaceFactory.DisposeRenderState(gl, framebufferState);

            Assert.Equal([expected.Red, expected.Green, expected.Blue, expected.Alpha], pixel);
        }
        finally
        {
            raw.DeleteTextures(1, ref textureId);
        }
    }
}
