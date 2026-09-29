using Xunit;

namespace Tests.CoreMetal;

/// <summary>
/// Skips on anything but macOS. <c>Tests.proj</c> pulls every test project into the Windows
/// <c>desktop-and-core</c> job, which has no Metal; the <c>core-metal-macos</c> job is where these run.
/// </summary>
public sealed class MacOnlyFactAttribute : FactAttribute
{
    public MacOnlyFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "Metal is macOS-only.";
    }
}
