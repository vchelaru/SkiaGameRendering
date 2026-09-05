using System.Runtime.InteropServices;
using SkiaGameRendering.Core.ANGLE;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Pins <c>AngleEgl.GetRuntimeIdentifier</c>, the piece of the DllImport resolver that picks which
/// runtimes/&lt;rid&gt;/native folder to probe for the vendored ANGLE binaries (issue #36).
///
/// Only win-x64 and win-arm64 are vendored (see eng/angle-provenance.json) - x86 was dropped rather
/// than left half-supported, so any architecture other than those two must fail loudly here instead
/// of the resolver silently guessing win-x64 and then hitting a bare, unhelpful DllNotFoundException
/// later.
/// </summary>
public sealed class AngleEglResolverTests
{
    [Theory]
    [InlineData(Architecture.X64, "win-x64")]
    [InlineData(Architecture.Arm64, "win-arm64")]
    public void GetRuntimeIdentifier_SupportedArchitecture_ReturnsVendoredRid(Architecture architecture, string expectedRid)
    {
        Assert.Equal(expectedRid, AngleEgl.GetRuntimeIdentifier(architecture));
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    public void GetRuntimeIdentifier_UnsupportedArchitecture_ThrowsPlatformNotSupported(Architecture architecture)
    {
        var ex = Assert.Throws<PlatformNotSupportedException>(() => AngleEgl.GetRuntimeIdentifier(architecture));
        Assert.Contains("win-x64", ex.Message);
        Assert.Contains("win-arm64", ex.Message);
    }
}
