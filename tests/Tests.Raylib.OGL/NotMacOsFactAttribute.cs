using Xunit;

namespace Tests.Raylib.OGL;

/// <summary>
/// Skips the decorated test on macOS. GLFW wants <c>InitWindow</c> on the process's main thread and xUnit
/// runs tests on thread pool threads, so on a Mac testhost spins at 100% CPU forever instead of failing.
/// macOS is checked by running Sample.Raylib.OGL instead.
/// </summary>
public sealed class NotMacOsFactAttribute : FactAttribute
{
    public NotMacOsFactAttribute()
    {
        if (OperatingSystem.IsMacOS())
            Skip = "raylib windows can't be created off the main thread on macOS, where these tests hang.";
    }
}
