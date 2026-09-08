using System.Runtime.InteropServices;
using Raylib_cs;
using SkiaGameRendering.Raylib.OGL;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
// This test project's own namespace (Tests.Raylib.OGL) has a "Raylib" segment that shadows
// Raylib_cs.Raylib (the static class) in unqualified lookups - the same reason
// SkiaRaylibRenderTarget2D.cs aliases it as RaylibApi. Follow that convention here.
using RaylibApi = Raylib_cs.Raylib;

namespace Tests.Raylib.OGL;

/// <summary>
/// Draws <see cref="GoldenScene"/> through a real raylib window and the real <c>Glx</c> path (see
/// <see cref="LinuxOnlyFactAttribute"/> - this only runs on Linux) and compares the readback against
/// a checked-in reference. Closes the gap issue #9 opened: the GLX implementation
/// (<c>src/SkiaGameRendering.Raylib.OGL/Glx.cs</c>) has been build-only since it landed - every CI
/// job that touches the raylib backend only compiles it, on <c>windows-latest</c>, where
/// <c>SkiaRaylibContext</c> always picks <c>Wgl</c> instead. A prior spike on this same
/// shared-GL-context trick (issue #3) found a real state-corruption bug a clean build completely
/// missed, so "it compiles" is not this repo's bar for this code path.
/// </summary>
public sealed class RaylibGoldenImageTests
{
    const string Golden = "raylib-ogl-scene.png";

    readonly ITestOutputHelper _output;

    public RaylibGoldenImageTests(ITestOutputHelper output) => _output = output;

    [LinuxOnlyFact]
    public void Scene_DrawnThroughSkiaRaylibRenderTarget2D_MatchesGolden()
    {
        // Not just an assumption baked into the Skip gate above: this proves the run that's about to
        // happen actually exercises SkiaRaylibContext.CreatePlatformGlContext's Glx branch, since that
        // branch selection is itself driven by OperatingSystem.IsLinux().
        Assert.True(OperatingSystem.IsLinux());

        // HiddenWindow keeps Xvfb's virtual display from ever needing to show anything - GLFW's X11
        // backend (and therefore GLX context creation) still needs a real display connection even so;
        // see the "Runs under Xvfb" step in master.yml's new Linux job and issue #12's comment in the
        // webgl-functional job for why a bare headless process isn't enough on its own.
        RaylibApi.SetConfigFlags(ConfigFlags.HiddenWindow);
        RaylibApi.InitWindow(GoldenScene.Width, GoldenScene.Height, "Tests.Raylib.OGL");
        try
        {
            SkiaRaylibRenderer.Initialize();
            try
            {
                var glProfile = ReadGlProfile();
                _output.WriteLine($"rlgl GL profile: {glProfile}");

                var pixels = RenderSceneAndReadBack();

                GoldenImage.AssertSceneOrientation(pixels);
                if (GoldenImage.PinnedRasterizerInUse)
                    GoldenImage.AssertMatchesGolden(pixels, Golden);
                else
                    _output.WriteLine("Golden comparison skipped: SKIAGAMERENDERING_PINNED_RASTERIZER " +
                        "is not set, so this run isn't necessarily CI's pinned Mesa llvmpipe build. " +
                        "The render itself still ran and was checked for orientation.");
            }
            finally
            {
                SkiaRaylibRenderer.Dispose();
            }
        }
        finally
        {
            RaylibApi.CloseWindow();
        }
    }

    static byte[] RenderSceneAndReadBack()
    {
        using var canvas = new SkiaRaylibRenderTarget2D(GoldenScene.Width, GoldenScene.Height);

        // EndWithoutDrawing, not End: this test reads SkiaRaylibRenderTarget2D.Texture directly, so a
        // failure here is in the Skia-to-raylib-texture path and not in Raylib.DrawTexture's own
        // composite blit.
        canvas.Begin();
        GoldenScene.Draw(canvas.Canvas);
        canvas.EndWithoutDrawing();

        return ReadTopDownRgba(canvas.Texture);
    }

    /// <summary>
    /// Reads <paramref name="texture"/> back via <c>Raylib_cs.Raylib.LoadImageFromTexture</c> (rlgl's
    /// <c>rlReadTexturePixels</c>, i.e. <c>glGetTexImage</c>) and flips it into the top-down,
    /// tightly-packed RGBA8888 layout <see cref="GoldenImage"/> expects.
    /// <para>
    /// The flip is required, not optional: <c>SkiaRaylibContext.CreateSurface</c> creates the
    /// Skia surface with <c>GRSurfaceOrigin.BottomLeft</c> so the texture matches raylib's own
    /// v=0-at-the-bottom sampling convention with no per-draw flip. That means GL texture row 0
    /// (what a tightly-packed <c>glGetTexImage</c> readback puts first) is the canvas's *bottom* row,
    /// not its top - the opposite of what <see cref="GoldenImage.AssertMatchesGolden"/> assumes.
    /// </para>
    /// </summary>
    static unsafe byte[] ReadTopDownRgba(Texture2D texture)
    {
        var image = RaylibApi.LoadImageFromTexture(texture);
        try
        {
            Assert.Equal(GoldenScene.Width, image.Width);
            Assert.Equal(GoldenScene.Height, image.Height);
            Assert.Equal(PixelFormat.UncompressedR8G8B8A8, image.Format);

            var rowBytes = image.Width * 4;
            var rgba = new byte[image.Width * image.Height * 4];
            var source = (byte*)image.Data;
            for (var y = 0; y < image.Height; y++)
            {
                var sourceRow = source + (long)(image.Height - 1 - y) * rowBytes;
                Marshal.Copy((IntPtr)sourceRow, rgba, y * rowBytes, rowBytes);
            }

            return rgba;
        }
        finally
        {
            RaylibApi.UnloadImage(image);
        }
    }

    /// <summary>
    /// Rlgl doesn't wrap <c>glGetString(GL_RENDERER)</c> the way <c>WglSkiaPixelReadbackTests</c>
    /// prints the driver name, so this is only the GL profile rlgl detected - still enough to confirm
    /// a real GL context initialized rather than nothing at all.
    /// </summary>
    static string ReadGlProfile() => Rlgl.GetVersion().ToString();
}
