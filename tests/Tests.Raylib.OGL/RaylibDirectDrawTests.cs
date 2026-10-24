using System.Runtime.InteropServices;
using Raylib_cs;
using SkiaGameRendering;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using RaylibApi = Raylib_cs.Raylib;

namespace Tests.Raylib.OGL;

/// <summary>
/// Checks <see cref="SkiaDirectCanvas"/> against raylib's own draws, the raylib twin of
/// <c>tests/Shared/DirectDrawLayering.cs</c>: a raylib rect before, a Skia rect over it, then a raylib
/// rect over the Skia one. Each probe pixel is covered by a known set of layers, so a vertical flip, a
/// channel swap or a Skia draw landing out of order each fails a specific probe. Runs on Windows (Mesa
/// llvmpipe, see <c>MesaVendor.props</c>) and Linux (Xvfb).
/// </summary>
[Collection(RaylibWindowCollection.Name)]
public sealed class RaylibDirectDrawTests
{
    const int Size = 128;

    static readonly Color Background = new(20, 30, 200, 255);
    static readonly Color Before = new(30, 180, 40, 255);
    static readonly SKColor Skia = new(210, 50, 90);
    static readonly Color After = new(240, 220, 10, 255);

    [Fact]
    public void BackBuffer_LayersWithRaylibDraws()
    {
        RunInWindow(() =>
        {
            using var direct = new SkiaDirectCanvas();
            RaylibApi.BeginDrawing();
            var errors = DrawAndCheck(direct, () =>
            {
                Rlgl.DrawRenderBatchActive();
                // Top row first, like raylib's own drawing coordinates.
                return ReadImage(RaylibApi.LoadImageFromScreen(), flipRows: false);
            });
            RaylibApi.EndDrawing();
            Assert.Equal("", errors);
        });
    }

    [Fact]
    public void RenderTexture_LayersWithRaylibDraws()
    {
        RunInWindow(() =>
        {
            using var direct = new SkiaDirectCanvas();
            // Twice, each with a fresh render texture: the second one usually gets the first one's GL
            // names, which a cached Skia FBO would mistake for the unloaded texture.
            for (var i = 0; i < 2; i++)
            {
                var renderTexture = RaylibApi.LoadRenderTexture(Size, Size);
                try
                {
                    RaylibApi.BeginDrawing();
                    RaylibApi.BeginTextureMode(renderTexture);
                    var errors = DrawAndCheck(direct, () =>
                    {
                        RaylibApi.EndTextureMode();
                        // raylib draws its y = 0 into the texture's last row, so read it bottom row first.
                        return ReadImage(RaylibApi.LoadImageFromTexture(renderTexture.Texture), flipRows: true);
                    });
                    RaylibApi.EndDrawing();
                    Assert.True(errors == "", $"render texture {i}: {errors}");
                }
                finally
                {
                    RaylibApi.UnloadRenderTexture(renderTexture);
                }
            }
        });
    }

    [Fact]
    public void Canvas_OutsideBeginEnd_Throws()
    {
        RunInWindow(() =>
        {
            using var direct = new SkiaDirectCanvas();
            Assert.Throws<InvalidOperationException>(() => direct.Canvas);
            Assert.Throws<InvalidOperationException>(direct.End);

            RaylibApi.BeginDrawing();
            direct.Begin();
            Assert.Throws<InvalidOperationException>(direct.Begin);
            Assert.Throws<InvalidOperationException>(direct.Dispose);
            direct.End();
            RaylibApi.EndDrawing();
            Assert.Throws<InvalidOperationException>(() => direct.Canvas);
        });
    }

    /// <summary>Draws the layers into whatever raylib has bound and reports every probe that is wrong, or "".</summary>
    static string DrawAndCheck(SkiaDirectCanvas direct, Func<Color[]> readBack)
    {
        RaylibApi.ClearBackground(Background);

        // Before: the top-left quadrant.
        RaylibApi.DrawRectangle(0, 0, Size / 2, Size / 2, Before);

        // Skia: a strip along the top, across the middle half. Drawn twice so the second pass reuses the
        // back buffer's cached surface.
        using var paint = new SKPaint { Color = Skia };
        for (var i = 0; i < 2; i++)
        {
            direct.Begin();
            direct.Canvas.DrawRect(Size / 4, 0, Size / 2, Size / 4, paint);
            direct.End();
        }

        // After: the right half, from 1/8 down to 1/2.
        RaylibApi.DrawRectangle(Size / 2, Size / 8, Size / 2, Size * 3 / 8, After);

        var pixels = readBack();
        var skia = new Color(Skia.Red, Skia.Green, Skia.Blue, Skia.Alpha);
        var errors = new List<string>();
        void Probe(string what, int x, int y, Color expected)
        {
            var actual = pixels[y * Size + x];
            if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B || actual.A != expected.A)
                errors.Add($"{what} at ({x}, {y}): expected {expected}, got {actual}");
        }

        Probe("before only", Size / 8, Size / 8, Before);
        Probe("Skia over before", Size * 3 / 8, Size / 16, skia);
        Probe("Skia alone, at the top", Size * 5 / 8, Size / 16, skia);
        Probe("after over Skia", Size * 5 / 8, Size * 3 / 16, After);
        Probe("background where a flipped Skia rect would be", Size * 5 / 8, Size * 15 / 16, Background);
        Probe("background", Size / 8, Size * 7 / 8, Background);
        return string.Join("; ", errors);
    }

    static unsafe Color[] ReadImage(Image image, bool flipRows)
    {
        try
        {
            Assert.Equal(Size, image.Width);
            Assert.Equal(Size, image.Height);
            Assert.Equal(PixelFormat.UncompressedR8G8B8A8, image.Format);

            var rgba = new byte[Size * Size * 4];
            Marshal.Copy((IntPtr)image.Data, rgba, 0, rgba.Length);
            var pixels = new Color[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                var row = flipRows ? Size - 1 - y : y;
                for (var x = 0; x < Size; x++)
                {
                    var i = (row * Size + x) * 4;
                    pixels[y * Size + x] = new Color(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
                }
            }
            return pixels;
        }
        finally
        {
            RaylibApi.UnloadImage(image);
        }
    }

    static void RunInWindow(Action body)
    {
        // GLFW's wgl loader resolves opengl32.dll like SDL does - see VendoredOpenGl.PreloadIfPresent.
        VendoredOpenGl.PreloadIfPresent();
        RaylibApi.SetConfigFlags(ConfigFlags.HiddenWindow);
        RaylibApi.InitWindow(Size, Size, "Tests.Raylib.OGL");
        try
        {
            SkiaRenderer.Initialize();
            try
            {
                body();
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
}
