using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using Tests.Shared;
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

    /// <summary>
    /// The same pipeline as above, but drawing <see cref="GoldenScene"/> - antialiased shapes, a
    /// gradient, overlapping translucent fills - and comparing the whole surface against a checked-in
    /// reference image instead of sampling one pixel. A solid-color clear can't tell a working Skia
    /// GPU pipeline from one that silently lost its blending or its antialiasing; this can.
    /// <para>
    /// The framebuffer binding is checked afterwards because that is the only GL state
    /// <see cref="GlSkiaSurfaceFactory"/> undertakes to restore. Unlike the ANGLE backend, which
    /// isolates Skia behind a D3D11.1 device-context-state swap, this path shares one GL context
    /// with the engine and leaves the rest of the state for the engine to reset itself - so
    /// asserting that, say, the viewport survived would be testing a promise nothing here makes.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void Scene_MatchesGolden_ThroughRealWglContext()
    {
        using var context = new WglContext();
        var loader = new WglFunctionLoader();
        var raw = new GlRawTestFunctions(loader);

        var renderer = raw.GetStringUtf8(GlRawTestFunctions.GL_RENDERER);
        _output.WriteLine($"GL_RENDERER: {renderer}");

        raw.GenTextures(1, out var textureId);
        try
        {
            raw.BindTexture(GlRawTestFunctions.GL_TEXTURE_2D, textureId);
            raw.TexImage2D(GlRawTestFunctions.GL_TEXTURE_2D, 0, GlRawTestFunctions.GL_RGBA8,
                GoldenScene.Width, GoldenScene.Height, 0,
                GlRawTestFunctions.GL_RGBA, GlRawTestFunctions.GL_UNSIGNED_BYTE, null);

            var gl = GlFunctions.Load(loader);
            using var grContext = GRContext.CreateGl()
                ?? throw new InvalidOperationException("GRContext.CreateGl() returned null.");
            grContext.ResetContext();

            var (surface, renderTarget) = GlSkiaSurfaceFactory.CreateSurface(
                grContext, gl, textureId, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888,
                out var framebufferState);

            GlSkiaSurfaceFactory.BindForDrawing(gl, framebufferState);
            GoldenScene.Draw(surface.Canvas);
            surface.Flush();

            // As in the clear test above, glReadPixels reads the bound framebuffer, so this has to
            // happen before UnbindAfterDrawing. Rows come back tightly packed here (the default
            // GL_PACK_ALIGNMENT of 4 divides a 4-byte-per-pixel row evenly), so no pitch handling.
            var pixels = new byte[GoldenScene.Width * GoldenScene.Height * 4];
            fixed (byte* pixelPtr = pixels)
            {
                raw.ReadPixels(0, 0, GoldenScene.Width, GoldenScene.Height,
                    GlRawTestFunctions.GL_RGBA, GlRawTestFunctions.GL_UNSIGNED_BYTE, pixelPtr);
            }

            GlSkiaSurfaceFactory.UnbindAfterDrawing(gl);
            raw.GetIntegerv(GlRawTestFunctions.GL_FRAMEBUFFER_BINDING, out var boundFramebuffer);

            renderTarget.Dispose();
            surface.Dispose();
            GlSkiaSurfaceFactory.DisposeRenderState(gl, framebufferState);

            Assert.Equal(0, boundFramebuffer);

            GoldenImage.AssertSceneOrientation(pixels);
            if (GoldenImage.PinnedRasterizerInUse)
                GoldenImage.AssertMatchesGolden(pixels, "core-ogl-scene.png");
            else
                _output.WriteLine($"Golden comparison skipped: the golden was rendered by CI's pinned " +
                    $"Mesa llvmpipe build and this run used '{renderer}'. The render itself still ran " +
                    "and was checked for orientation.");
        }
        finally
        {
            raw.DeleteTextures(1, ref textureId);
        }
    }
}
