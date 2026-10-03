using System.Runtime.InteropServices;
using Raylib_cs;
using SkiaGameRendering.Raylib.OGL;
using Tests.Shared;
using Xunit;
using RaylibApi = Raylib_cs.Raylib;

namespace Tests.Raylib.OGL;

/// <summary>
/// Composites <see cref="GoldenScene"/> with <c>SkiaRaylibRenderTarget2D.End()</c> (the real
/// <c>DrawTexture</c> blit a sample uses) and reads the window's framebuffer back, so a vertical flip
/// in the on-screen result fails here. <c>RaylibGoldenImageTests</c> reads the texture instead, which
/// cannot see that. Runs on Windows (Mesa llvmpipe, see <c>MesaVendor.props</c>) and Linux (Xvfb).
/// </summary>
public sealed class RaylibOnScreenOrientationTests
{
    [Fact]
    public unsafe void Scene_CompositedToScreen_IsNotFlipped()
    {
        // GLFW's wgl loader resolves opengl32.dll like SDL does - see VendoredOpenGl.PreloadIfPresent.
        VendoredOpenGl.PreloadIfPresent();
        RaylibApi.SetConfigFlags(ConfigFlags.HiddenWindow);
        RaylibApi.InitWindow(GoldenScene.Width, GoldenScene.Height, "Tests.Raylib.OGL");
        try
        {
            SkiaRaylibRenderer.Initialize();
            try
            {
                using var canvas = new SkiaRaylibRenderTarget2D(GoldenScene.Width, GoldenScene.Height);

                RaylibApi.BeginDrawing();
                RaylibApi.ClearBackground(Color.Black);
                canvas.Begin();
                GoldenScene.Draw(canvas.Canvas);
                canvas.End();

                // DrawTexture is only queued in raylib's batch until flushed. Read before EndDrawing,
                // while the buffer is still the one just drawn into rather than a swapped one.
                Rlgl.DrawRenderBatchActive();
                var image = RaylibApi.LoadImageFromScreen();
                RaylibApi.EndDrawing();
                try
                {
                    Assert.Equal(GoldenScene.Width, image.Width);
                    Assert.Equal(GoldenScene.Height, image.Height);
                    Assert.Equal(PixelFormat.UncompressedR8G8B8A8, image.Format);

                    var rgba = new byte[image.Width * image.Height * 4];
                    Marshal.Copy((IntPtr)image.Data, rgba, 0, rgba.Length);
                    GoldenImage.AssertSceneOrientation(rgba);
                }
                finally
                {
                    RaylibApi.UnloadImage(image);
                }
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
}
