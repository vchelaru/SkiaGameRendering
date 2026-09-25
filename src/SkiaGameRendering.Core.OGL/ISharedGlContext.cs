namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// A second, OS-native GL context for Skia that shares a host engine's GL object namespace
    /// (textures, buffers, programs) but has its own bound state. A host whose GL layer caches bound
    /// state (raylib's rlgl, Godot's GLES3 renderer) is corrupted when Skia issues raw GL calls on
    /// its context, so Skia gets this one instead. See <see cref="WglSharedContext"/> (Windows) and
    /// <see cref="GlxSharedContext"/> (Linux/X11).
    /// </summary>
    public interface ISharedGlContext : IDisposable
    {
        /// <summary>
        /// Creates the Skia context, sharing objects with the context current on this thread (the
        /// host's). <paramref name="windowHandle"/> is the host window's native handle.
        /// </summary>
        void CreateSharedContext(IntPtr windowHandle);

        /// <summary>
        /// Makes the Skia context current, remembering whichever context and drawable were current
        /// before so <see cref="RestoreHostContext"/> can put them back. Calls do not nest.
        /// </summary>
        void MakeSkiaContextCurrent();

        /// <summary>
        /// Makes current again the context and drawable that were current when
        /// <see cref="MakeSkiaContextCurrent"/> ran, which may be a different host window than the
        /// one the Skia context was created on.
        /// </summary>
        void RestoreHostContext();

        /// <summary>
        /// Resolves a GL function pointer against the Skia context. Only valid while the Skia context
        /// is current (see <see cref="MakeSkiaContextCurrent"/>).
        /// </summary>
        IntPtr GetProcAddress(string name);
    }
}
