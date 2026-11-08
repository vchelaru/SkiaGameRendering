using System.Runtime.InteropServices;
using SkiaGameRendering;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Brutal;

/// <summary>
/// Draws the shared <see cref="GoldenScene"/> through <see cref="SkiaRenderTarget2D"/> on a BRUTAL Vulkan device, reads the
/// whole image back through Vulkan (test-only), and compares it to the golden, in both layout modes. The golden was
/// rendered by CI's Mesa lavapipe, so the pixel comparison only runs under SKIAGAMERENDERING_PINNED_RASTERIZER, the same
/// gate Tests.Core.VK uses; orientation is always checked. Set SKIAGAMERENDERING_BRUTAL_SCREENSHOTS to a folder to
/// also save the rendered PNGs.
/// </summary>
public sealed class BrutalGoldenImageTests
{
    readonly ITestOutputHelper _output;

    public BrutalGoldenImageTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scene_MatchesGolden_ThroughBrutalVulkanDevice(bool appManagesLayout)
    {
        using var vk = new BrutalDevice();
        using var target = new SkiaRenderTarget2D(vk.Context, GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888, appManagesLayout);
        target.Begin();
        GoldenScene.Draw(target.Canvas);
        target.End(synchronous: true);

        var pixels = vk.ReadImage(target, appManagesLayout);
        SaveScreenshot(pixels, appManagesLayout ? "scene-app-managed.png" : "scene-library-managed.png");

        GoldenImage.AssertSceneOrientation(pixels);
        if (GoldenImage.PinnedRasterizerInUse)
            GoldenImage.AssertMatchesGolden(pixels, "brutal-scene.png");
        else
            _output.WriteLine("Golden comparison skipped: the golden was rendered by CI's pinned Mesa lavapipe and this run " +
                "used whichever Vulkan driver this machine has. Orientation was still checked.");
    }

    static void SaveScreenshot(byte[] rgba, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SKIAGAMERENDERING_BRUTAL_SCREENSHOTS");
        if (string.IsNullOrEmpty(dir))
            return;
        Directory.CreateDirectory(dir);
        using var bitmap = new SKBitmap(new SKImageInfo(GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, name), data.ToArray());
    }
}
