using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;
using Xunit;

namespace Tests.Shared;

/// <summary>
/// Compares a backend's readback of <see cref="GoldenScene"/> against a checked-in reference PNG.
/// Linked into each <c>Tests.Core.*</c> project, the same pattern <c>EngineReflectionPin</c> uses,
/// so all three backends share one scene and one comparison.
/// <para>
/// Goldens are per-backend, not shared: WARP, Mesa llvmpipe and Mesa lavapipe rasterize the same
/// scene to slightly different pixels, so no single reference image could satisfy all three.
/// </para>
/// <para>
/// The comparison is tolerance-based rather than byte-exact. Antialiased coverage shifts by a
/// channel step or two whenever SkiaSharp or the pinned Mesa build changes, which is not a defect
/// in this library - byte-exact goldens would turn every dependency bump into a red build. The
/// tolerance is still tight enough that a real regression (a shape that stopped drawing, a lost
/// blend mode, a swapped channel order) fails it comfortably.
/// </para>
/// <para>
/// <see cref="GoldenScene"/> is opaque everywhere, so premultiplied and unpremultiplied readbacks
/// are byte-identical and no alpha-type conversion is needed on either side of the comparison.
/// </para>
/// </summary>
static class GoldenImage
{
    /// <summary>Per-channel difference a pixel may have before it counts as differing at all.</summary>
    const int MaxChannelDelta = 4;

    /// <summary>Fraction of pixels that may exceed <see cref="MaxChannelDelta"/> before the test fails.</summary>
    const double MaxDifferingFraction = 0.005;

    /// <summary>
    /// Set to 1 to overwrite the golden from this run instead of comparing against it. Local use
    /// only - it writes into the test project's source tree, found via the caller's compile-time path.
    /// </summary>
    const string UpdateEnvironmentVariable = "SKIAGAMERENDERING_UPDATE_GOLDENS";

    /// <summary>
    /// Where a failing run drops the rendered image and a diff, for a human - or CI's artifact
    /// upload - to look at afterwards. "0.9% of pixels differ" is close to useless without the picture.
    /// </summary>
    const string FailureDirectoryName = "golden-failures";

    /// <summary>
    /// Set by CI, where the OpenGL and Vulkan backends are pinned to the Mesa software rasterizers
    /// the goldens were rendered from (see <c>master.yml</c> and the <c>headless-gpu-testing</c>
    /// skill). Those two backends run against whatever real driver a dev box has instead, which
    /// rasterizes antialiased edges differently enough that comparing would fail for reasons that
    /// are not regressions - so they check orientation locally and compare only where this is set.
    /// The ANGLE backend needs no such gate: it is pinned to WARP everywhere, CI or not.
    /// </summary>
    internal static bool PinnedRasterizerInUse =>
        Environment.GetEnvironmentVariable("SKIAGAMERENDERING_PINNED_RASTERIZER") == "1";

    /// <param name="rgba">
    /// Tightly packed, top-down RGBA8888 readback of a <see cref="GoldenScene"/> render, with any
    /// API-specific row pitch already removed by the caller.
    /// </param>
    /// <param name="goldenFileName">File name inside the test project's <c>goldens</c> folder.</param>
    /// <param name="callerFilePath">
    /// Filled in by the compiler; used only by update mode, to find the source tree to write to.
    /// </param>
    internal static void AssertMatchesGolden(byte[] rgba, string goldenFileName,
        [CallerFilePath] string callerFilePath = "")
    {
        AssertSceneOrientation(rgba);

        if (Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) == "1")
        {
            WriteUpdatedGolden(rgba, goldenFileName, callerFilePath);
            return;
        }

        var goldenPath = Path.Combine(AppContext.BaseDirectory, "goldens", goldenFileName);
        Assert.True(File.Exists(goldenPath), $"Golden image not found: {goldenPath}. " +
            $"Set {UpdateEnvironmentVariable}=1 and re-run to generate it.");

        var golden = DecodeRgba(goldenPath);
        Assert.Equal(rgba.Length, golden.Length);

        var (differingPixels, worstDelta) = Compare(rgba, golden);
        var pixelCount = rgba.Length / 4;
        var differingFraction = (double)differingPixels / pixelCount;
        if (differingFraction <= MaxDifferingFraction)
            return;

        var failureDirectory = WriteFailureArtifacts(rgba, golden, goldenFileName);
        Assert.Fail(
            $"Render does not match golden '{goldenFileName}': {differingPixels}/{pixelCount} pixels " +
            $"({differingFraction:P2}) differ by more than {MaxChannelDelta} per channel, worst channel " +
            $"delta {worstDelta}, allowed {MaxDifferingFraction:P2}. " +
            $"Rendered image and diff written to {failureDirectory}.");
    }

    /// <summary>
    /// Checks the scene landed the right way up and the right way round, independently of the
    /// golden. Without this, a golden regenerated from an already-flipped render would lock the flip
    /// in and every future run would happily agree with it.
    /// </summary>
    internal static void AssertSceneOrientation(byte[] rgba)
    {
        Assert.Equal(GoldenScene.Width * GoldenScene.Height * 4, rgba.Length);

        var centerX = (int)GoldenScene.OpaqueRect.MidX;
        var centerY = (int)GoldenScene.OpaqueRect.MidY;
        var expected = GoldenScene.OpaqueRectColor;
        byte[] expectedPixel = [expected.Red, expected.Green, expected.Blue, expected.Alpha];

        // The rect is drawn with antialiasing off and nothing overlaps it, so its center matches
        // exactly on every rasterizer - no tolerance needed or wanted here.
        Assert.Equal(expectedPixel, PixelAt(rgba, centerX, centerY));

        // The same point mirrored in each axis. The scene is asymmetric in both, so neither can be
        // this color unless the readback came back flipped.
        Assert.NotEqual(expectedPixel, PixelAt(rgba, centerX, GoldenScene.Height - 1 - centerY));
        Assert.NotEqual(expectedPixel, PixelAt(rgba, GoldenScene.Width - 1 - centerX, centerY));
    }

    static byte[] PixelAt(byte[] rgba, int x, int y)
    {
        var offset = (y * GoldenScene.Width + x) * 4;
        return [rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]];
    }

    static (int differingPixels, int worstDelta) Compare(byte[] actual, byte[] golden)
    {
        var differingPixels = 0;
        var worstDelta = 0;
        for (var i = 0; i < actual.Length; i += 4)
        {
            var pixelDelta = PixelDelta(actual, golden, i);
            worstDelta = Math.Max(worstDelta, pixelDelta);
            if (pixelDelta > MaxChannelDelta)
                differingPixels++;
        }

        return (differingPixels, worstDelta);
    }

    static int PixelDelta(byte[] actual, byte[] golden, int offset)
    {
        var delta = 0;
        for (var channel = 0; channel < 4; channel++)
            delta = Math.Max(delta, Math.Abs(actual[offset + channel] - golden[offset + channel]));

        return delta;
    }

    static byte[] DecodeRgba(string path)
    {
        using var decoded = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Could not decode golden image: {path}");
        using var converted = decoded.Copy(SKColorType.Rgba8888)
            ?? throw new InvalidOperationException($"Could not convert golden image to RGBA8888: {path}");
        return converted.Bytes;
    }

    static void WriteUpdatedGolden(byte[] rgba, string goldenFileName, string callerFilePath)
    {
        var sourceDirectory = Path.GetDirectoryName(callerFilePath)
            ?? throw new InvalidOperationException("Could not determine the calling test's source directory.");
        var goldensDirectory = Path.Combine(sourceDirectory, "goldens");
        Directory.CreateDirectory(goldensDirectory);
        var path = Path.Combine(goldensDirectory, goldenFileName);
        File.WriteAllBytes(path, EncodePng(rgba));

        // Deliberately a failure, not a pass: update mode never compared anything, and a green run
        // here would read as "the backend is fine" when nothing was actually checked.
        Assert.Fail($"{UpdateEnvironmentVariable}=1: wrote golden to {path} instead of comparing. " +
            "Unset it and re-run to actually test.");
    }

    static string WriteFailureArtifacts(byte[] actual, byte[] golden, string goldenFileName)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, FailureDirectoryName);
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(goldenFileName);
        File.WriteAllBytes(Path.Combine(directory, $"{stem}.actual.png"), EncodePng(actual));
        File.WriteAllBytes(Path.Combine(directory, $"{stem}.diff.png"), EncodePng(BuildDiff(actual, golden)));
        return directory;
    }

    /// <summary>Differing pixels in magenta over a dimmed copy of the render, so the diff reads at a glance.</summary>
    static byte[] BuildDiff(byte[] actual, byte[] golden)
    {
        var diff = new byte[actual.Length];
        for (var i = 0; i < actual.Length; i += 4)
        {
            if (PixelDelta(actual, golden, i) > MaxChannelDelta)
            {
                diff[i] = 255;
                diff[i + 1] = 0;
                diff[i + 2] = 255;
            }
            else
            {
                var dimmed = (byte)((actual[i] + actual[i + 1] + actual[i + 2]) / 6);
                diff[i] = dimmed;
                diff[i + 1] = dimmed;
                diff[i + 2] = dimmed;
            }

            diff[i + 3] = 255;
        }

        return diff;
    }

    static byte[] EncodePng(byte[] rgba)
    {
        var info = new SKImageInfo(GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
