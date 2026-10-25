using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Shared;

/// <summary>
/// <see cref="SkiaRenderTarget2D"/>'s <c>multiSampleCount</c> through the engine-facing API. Linked into
/// each engine test project, like <see cref="EngineSkiaGolden"/>: a GL backend runs
/// <see cref="AssertMultiSampledMatchesPlainAwayFromEdges"/> and <see cref="AssertNoGrowthOverMultiSampledFrames"/>,
/// every other backend runs <see cref="AssertMultiSampleThrows"/>.
/// </summary>
static class EngineMultiSample
{
    /// <summary>The scene drawn plain and 4x multisampled, read back with the engine's own <c>GetData</c>.</summary>
    internal static void AssertMultiSampledMatchesPlainAwayFromEdges(GraphicsDevice graphicsDevice, ITestOutputHelper output)
    {
        var plain = Render(graphicsDevice, 0);
        var multiSampled = Render(graphicsDevice, 4);
        Assert.True(SkiaRenderer.SupportsMultiSampledTargets);

        // The hard-edged rect has no antialiasing to differ in; its interior (clear of the translucent
        // rects that overlap its right and bottom) must come out identical.
        var rect = GoldenScene.OpaqueRect;
        for (var y = (int)rect.Top + 2; y < 18; y++)
            for (var x = (int)rect.Left + 2; x < 28; x++)
            {
                var i = (y * GoldenScene.Width + x) * 4;
                Assert.Equal(plain[i..(i + 4)], multiSampled[i..(i + 4)]);
                Assert.Equal(GoldenScene.OpaqueRectColor.Red, multiSampled[i]);
            }

        // The scene must have rendered (an unresolved surface stays black).
        var background = GoldenScene.Background;
        Assert.Equal(background.Red, multiSampled[0]);
        Assert.Equal(background.Green, multiSampled[1]);
        Assert.Equal(background.Blue, multiSampled[2]);

        // Everything else may differ only where the scene is antialiased: count and size are bounded.
        int differing = 0, maxDifference = 0;
        for (var i = 0; i < plain.Length; i++)
        {
            var difference = Math.Abs(plain[i] - multiSampled[i]);
            maxDifference = Math.Max(maxDifference, difference);
            if (difference > 0 && i % 4 == 0)
                differing++;
        }

        output.WriteLine($"4x vs none: {differing} of {GoldenScene.Width * GoldenScene.Height} pixels differ, max channel difference {maxDifference}.");
        Assert.True(differing < GoldenScene.Width * GoldenScene.Height / 5, $"{differing} pixels differ; edges alone should not reach that many.");
        Assert.True(maxDifference <= 130, $"A channel differs by {maxDifference}; edge smoothing alone stays well under that.");
    }

    /// <summary>Every unsupported value throws <see cref="NotSupportedException"/> on any backend.</summary>
    internal static void AssertInvalidCountsThrow(GraphicsDevice graphicsDevice)
    {
        foreach (var count in new[] { -1, 1, 3, 5, 16 })
            Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(
                graphicsDevice, GoldenScene.Width, GoldenScene.Height, multiSampleCount: count));
    }

    /// <summary>A backend with no multisampling throws for 2, 4 and 8 instead of falling back to 0, and the default still works.</summary>
    internal static void AssertMultiSampleThrows(GraphicsDevice graphicsDevice)
    {
        foreach (var count in new[] { 2, 4, 8 })
            Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(
                graphicsDevice, GoldenScene.Width, GoldenScene.Height, multiSampleCount: count));

        using var plain = new SkiaRenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height);
        Assert.False(SkiaRenderer.SupportsMultiSampledTargets);
    }

    /// <summary>Creates, draws and disposes multisampled targets over and over; the process must not grow.</summary>
    internal static void AssertNoGrowthOverMultiSampledFrames(GraphicsDevice graphicsDevice, ITestOutputHelper output) =>
        FrameLeakCheck.AssertNoPerFrameGrowth(() =>
        {
            using var canvas = new SkiaRenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height, multiSampleCount: 4);
            canvas.Begin();
            GoldenScene.Draw(canvas.Canvas);
            canvas.EndWithoutDrawing();
        }, output, warmupFrames: 20, frames: 100);

    static byte[] Render(GraphicsDevice graphicsDevice, int multiSampleCount)
    {
        using var canvas = new SkiaRenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height, multiSampleCount: multiSampleCount);
        canvas.Begin();
        GoldenScene.Draw(canvas.Canvas);
        canvas.EndWithoutDrawing();
        return EngineSkiaGolden.ReadRgba(canvas.Texture);
    }
}
