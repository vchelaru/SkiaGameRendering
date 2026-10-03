using Xunit;

namespace Tests.Raylib.OGL;

/// <summary>
/// Skips the decorated test at runtime unless <see cref="OperatingSystem.IsLinux"/>. <c>Tests.proj</c>'s
/// wildcard discovery (see its own comment) pulls this project into the existing <c>windows-latest</c>
/// <c>desktop-and-core</c> job automatically the moment it exists, so without this gate that job would
/// try to run a real raylib window and GLX-only code path on Windows.
/// <para>
/// Only the golden comparison is Linux-only: it checks <c>SkiaRaylibContext</c>'s GLX path against a
/// golden rendered on Linux llvmpipe. Windows raylib coverage is <c>RaylibOnScreenOrientationTests</c>.
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
