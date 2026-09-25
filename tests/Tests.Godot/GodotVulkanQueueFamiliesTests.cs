using SkiaGameRendering.Godot;
using Xunit;

namespace Tests.Godot;

/// <summary>
/// Pins <see cref="GodotVulkanQueueFamilies.UploadsShareMainQueue"/> to Godot's own queue-family pick
/// on the family layouts real drivers report. Flag values: graphics 0x1, compute 0x2, transfer 0x4,
/// sparse binding 0x8, video decode 0x20.
/// </summary>
public class GodotVulkanQueueFamiliesTests
{
    [Theory]
    // One family does everything (Intel iGPU, MoltenVK, most mobile): uploads share the main queue.
    [InlineData(new uint[] { 0x7 }, 0u, true)]
    // A graphics family that does not report TRANSFER explicitly, and nothing else: falls back to main.
    [InlineData(new uint[] { 0x3 }, 0u, true)]
    // NVIDIA-style: G|C|T|S, a dedicated T|S family, and a compute family. The dedicated one wins.
    [InlineData(new uint[] { 0xF, 0xC, 0xE }, 0u, false)]
    // AMD-style: G|C|T|S plus compute and transfer families.
    [InlineData(new uint[] { 0xF, 0xE, 0xC }, 0u, false)]
    // Only a compute family besides main: it has fewer flags than main, so uploads go there.
    [InlineData(new uint[] { 0xF, 0x6 }, 0u, false)]
    // A video-decode family that also reports TRANSFER counts, but 0x24 is a higher value than main's
    // 0xF, so Godot's lowest-value rule keeps uploads on main.
    [InlineData(new uint[] { 0xF, 0x24 }, 0u, true)]
    // A family without G/C/T (video decode only) is skipped.
    [InlineData(new uint[] { 0x7, 0x20 }, 0u, true)]
    // Main family is not index 0.
    [InlineData(new uint[] { 0xC, 0xF }, 1u, false)]
    [InlineData(new uint[] { 0x20, 0x7 }, 1u, true)]
    public void MatchesGodotsTransferQueuePick(uint[] familyFlags, uint mainFamily, bool expectShared)
    {
        Assert.Equal(expectShared, GodotVulkanQueueFamilies.UploadsShareMainQueue(familyFlags, mainFamily));
    }
}
