using Xunit;

namespace Tests.Brutal.OGL;

/// <summary>
/// Skips the decorated test on GitHub's Windows runner, which has no OpenGL driver. <c>Tests.proj</c>'s wildcard
/// discovery pulls this project into the <c>windows-latest</c> <c>desktop-and-core</c> job, which would fail
/// creating the GLFW window. These tests run on Linux in the <c>brutal-ogl-linux</c> job (Xvfb and Mesa llvmpipe), and
/// on a dev machine with a GPU.
/// </summary>
public sealed class GlWindowFactAttribute : FactAttribute
{
    public GlWindowFactAttribute()
    {
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Skip = "GitHub's Windows runner has no OpenGL driver; brutal-ogl-linux runs these tests.";
    }
}
