using SkiaSharp;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// Creates Skia's <see cref="GRContext"/> on the current desktop GL context. Every desktop GL
    /// backend goes through this instead of calling <see cref="GRContext.CreateGl()"/> directly.
    /// </summary>
    public static class GlGrContextFactory
    {
        private const int GL_VERSION = 0x1F02;

        public static GRContext Create(GlFunctions gl)
        {
            var options = new GRContextOptions();

            // Skia's GrGLCaps assumes every desktop GL context supports gl_VertexID, which needs GL
            // 3.0. On a GL 2.x context (the legacy one MonoGame and KNI get on macOS) the
            // tessellation and atlas path renderers' shaders fail to compile and complex paths
            // draw nothing. Avoiding stencil buffers turns both renderers off.
            if (IsBelowGl3(gl.GetString(GL_VERSION)))
                options.AvoidStencilBuffers = true;

            return GRContext.CreateGl(options)
                ?? throw new InvalidOperationException("GRContext.CreateGl failed on the Skia GL context.");
        }

        // Desktop GL_VERSION starts "<major>.<minor>", e.g. "2.1 Metal - 90.5" or "4.6.0 NVIDIA 555.85".
        internal static bool IsBelowGl3(string? version)
        {
            if (string.IsNullOrEmpty(version) || !char.IsDigit(version[0]))
                return false;
            return version[0] < '3' && (version.Length == 1 || !char.IsDigit(version[1]));
        }
    }
}
