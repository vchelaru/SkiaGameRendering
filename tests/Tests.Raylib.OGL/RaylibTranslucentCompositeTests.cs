using System.Runtime.InteropServices;
using Raylib_cs;
using SkiaGameRendering;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using RaylibApi = Raylib_cs.Raylib;

namespace Tests.Raylib.OGL;

/// <summary>
/// A translucent Skia pixel composited over a solid background through <c>SkiaRenderTarget2D.End()</c>
/// must come out as ordinary source-over. Skia's texture is premultiplied, so a straight-alpha blend
/// (raylib's default) multiplies by alpha twice and darkens every translucent pixel.
/// <c>RaylibGoldenImageTests</c> reads the texture and cannot see that.
/// </summary>
[Collection(RaylibWindowCollection.Name)]
public sealed class RaylibTranslucentCompositeTests
{
    [NotMacOsFact]
    public unsafe void TranslucentPixel_CompositedOverBackground_IsSourceOver()
    {
        VendoredOpenGl.PreloadIfPresent();
        RaylibApi.SetConfigFlags(ConfigFlags.HiddenWindow);
        RaylibApi.InitWindow(64, 64, "Tests.Raylib.OGL");
        try
        {
            SkiaRenderer.Initialize();
            try
            {
                using var canvas = new SkiaRenderTarget2D(64, 64);
                var bg = new Color(100, 149, 237, 255);

                RaylibApi.BeginDrawing();
                RaylibApi.ClearBackground(bg);
                canvas.Begin();
                canvas.Canvas.Clear(SKColors.Transparent);
                using (var paint = new SKPaint { Color = new SKColor(255, 255, 255, 128) })
                    canvas.Canvas.DrawRect(0, 0, 64, 64, paint);
                canvas.End();

                Rlgl.DrawRenderBatchActive();
                var image = RaylibApi.LoadImageFromScreen();
                RaylibApi.EndDrawing();
                try
                {
                    var rgba = new byte[image.Width * image.Height * 4];
                    Marshal.Copy((IntPtr)image.Data, rgba, 0, rgba.Length);
                    var i = (32 * image.Width + 32) * 4;
                    const float a = 128f / 255f;
                    AssertNear(255 * a + bg.R * (1 - a), rgba[i], "R");
                    AssertNear(255 * a + bg.G * (1 - a), rgba[i + 1], "G");
                    AssertNear(255 * a + bg.B * (1 - a), rgba[i + 2], "B");
                }
                finally
                {
                    RaylibApi.UnloadImage(image);
                }
            }
            finally
            {
                SkiaRenderer.Dispose();
            }
        }
        finally
        {
            RaylibApi.CloseWindow();
        }
    }

    static void AssertNear(double expected, int actual, string channel) =>
        Assert.True(Math.Abs(expected - actual) <= 4, $"{channel}: expected {expected:F1}, got {actual}");
}
