using System.Runtime.CompilerServices;

namespace Tests.Fna.OGL;

/// <summary>
/// FNA3D picks its driver once per process, when the first game is built, so the OpenGL hint has to
/// be set before any test class runs. A per-class static constructor missed whichever class ran
/// first without one.
/// </summary>
static class FnaOglDriver
{
    [ModuleInitializer]
    internal static void ForceOpenGl() =>
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");
}
