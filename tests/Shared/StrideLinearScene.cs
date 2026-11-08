using Stride.Graphics;
using Xunit.Abstractions;
using SharedScene = SampleShared.Scene;

namespace Tests.Shared;

static partial class StrideSkiaGolden
{
    /// <summary>
    /// Draws the samples' shared scene through the canvas onto an sRGB-format target, which is what Stride's
    /// back buffer is in its default Linear color space, and checks each cell's center against what MonoGame,
    /// raylib and Brutal produce (within <see cref="GoldenImage"/>'s 4 per channel). The device must be Linear.
    /// <para>
    /// The gradient and the drop's blended fills are what catch a surface that Skia blends in linear space
    /// (lighter mid-tones, plus banding from 8-bit linear storage). The red circle has 0 and 255 channels,
    /// which survive any transfer-function error, so it cannot.
    /// </para>
    /// </summary>
    internal static void AssertSharedSceneMatchesOtherRuntimes(GraphicsDevice graphicsDevice, ITestOutputHelper output)
    {
        using var target = Texture.New2D(
            graphicsDevice, SharedScene.Width, SharedScene.Height, PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        using var canvas = new SkiaStrideCanvas(graphicsDevice, SharedScene.Width, SharedScene.Height);

        Composite(graphicsDevice, target, graphicsContext =>
        {
            canvas.Begin();
            SharedScene.Draw(canvas.Canvas);
            canvas.End(graphicsContext);
        });
        var rgba = ReadRgba(graphicsDevice, target);

        (int R, int G, int B, int A) At(int x, int y)
        {
            var i = (y * SharedScene.Width + x) * 4;
            return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
        }

        var failures = new List<string>();
        void Check(string name, int x, int y, int r, int g, int b)
        {
            var c = At(x, y);
            output.WriteLine($"{name}: {c.R},{c.G},{c.B},{c.A} (expected {r},{g},{b},255)");
            if (Math.Abs(c.R - r) > 4 || Math.Abs(c.G - g) > 4 || Math.Abs(c.B - b) > 4 || c.A != 255)
                failures.Add($"{name}: expected {r},{g},{b},255 got {c.R},{c.G},{c.B},{c.A}");
        }

        Check("circle", SharedScene.CellCenter, SharedScene.CellCenter, 255, 0, 0);
        Check("drop", SharedScene.Cell + SharedScene.CellCenter, SharedScene.CellCenter, 51, 140, 232);
        Check("gradient", SharedScene.CellCenter, SharedScene.Cell + SharedScene.CellCenter, 180, 23, 158);
        Xunit.Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
