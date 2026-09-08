using Xunit;

namespace Tests.Raylib.OGL;

/// <summary>
/// Skips the decorated test at runtime unless <see cref="OperatingSystem.IsLinux"/>. <c>Tests.proj</c>'s
/// wildcard discovery (see its own comment) pulls this project into the existing <c>windows-latest</c>
/// <c>desktop-and-core</c> job automatically the moment it exists, so without this gate that job would
/// try to run a real raylib window and GLX-only code path on Windows.
/// <para>
/// Windows raylib golden coverage is a deliberate follow-up, not an oversight: it would need Mesa
/// llvmpipe vendored into this project the way <c>Tests.Core.OGL.csproj</c> does for
/// <c>WglSkiaPixelReadbackTests</c> (see <c>tests/MesaVendor.props</c>), and - unlike that project,
/// which only needs a hidden window - proof that raylib's own <c>InitWindow</c> succeeds at all
/// headless on <c>windows-latest</c>, which has never been checked. Skipping (not silently no-op'ing)
/// keeps that gap visible in CI output instead of hiding it.
/// </para>
/// </summary>
public sealed class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Raylib golden coverage runs on Linux (GLX) only for now - see this attribute's doc comment.";
    }
}
