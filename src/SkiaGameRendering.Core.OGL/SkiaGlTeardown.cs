using SkiaSharp;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// Every free of a Skia-side GL object (surface, FBO, renderbuffer, the <see cref="GRContext"/>) goes
    /// through <see cref="Run"/>. FBO names belong to one context, so a delete on the host's context
    /// deletes the host's FBO with that name, and even where a driver shares them the delete rebinds the
    /// host context's framebuffer behind the host's binding cache.
    /// </summary>
    public static class SkiaGlTeardown
    {
        /// <summary>
        /// Runs <paramref name="teardown"/> with Skia's context current, then makes the host's current
        /// again. When the host is gone (<paramref name="hostAlive"/> false, typically at shutdown) or Skia's
        /// context cannot be made current, nothing is made current: <paramref name="grContext"/> is abandoned
        /// so Skia makes no GL calls, and <paramref name="teardown"/> gets false, meaning it must make no GL
        /// calls either. Those objects then go when the context is destroyed.
        /// </summary>
        public static void Run(bool hostAlive, GRContext? grContext, Action makeSkiaContextCurrent,
            Action restoreHostContext, Action<bool> teardown)
        {
            var current = false;
            if (hostAlive)
            {
                try
                {
                    makeSkiaContextCurrent();
                    current = true;
                    // Skia's cached GL state may be stale after raw GL calls on its context.
                    grContext?.ResetContext();
                }
                catch (Exception)
                {
                    // A failed switch leaves later Skia draws failing the same way, so abandoning costs nothing.
                }
            }

            if (!current)
            {
                grContext?.AbandonContext();
                teardown(false);
                return;
            }

            try
            {
                teardown(true);
            }
            finally
            {
                restoreHostContext();
            }
        }
    }
}
