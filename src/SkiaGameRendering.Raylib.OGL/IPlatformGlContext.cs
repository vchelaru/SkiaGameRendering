namespace SkiaGameRendering.Raylib
{
    /// <summary>
    /// A second, OS-native GL context that shares raylib's GL object namespace (textures, buffers,
    /// programs) with raylib's own context but has entirely independent bound state. See
    /// <see cref="Wgl"/> (Windows) and <see cref="Glx"/> (Linux) for why this is needed: rlgl and
    /// Skia both issue raw GL calls, and interleaving them on a single shared context corrupts
    /// rlgl's own rendering.
    /// <para>
    /// <see cref="SkiaRaylibContext"/> picks an implementation at runtime via
    /// <see cref="OperatingSystem.IsWindows"/>/<see cref="OperatingSystem.IsLinux"/> so the rest of
    /// the raylib adapter (and the public <c>SkiaRaylibRenderTarget2D</c> API) doesn't need to know
    /// which platform it's running on.
    /// </para>
    /// </summary>
    internal interface IPlatformGlContext
    {
        /// <summary>
        /// Creates the Skia-dedicated context, sharing object namespace with whatever context is
        /// current on this thread (raylib's own, since this is called right after
        /// <c>Raylib.InitWindow</c>). <paramref name="windowHandle"/> is <c>Raylib.GetWindowHandle()</c>.
        /// </summary>
        void CreateSharedContext(IntPtr windowHandle);

        void MakeSkiaContextCurrent();

        void MakeEngineContextCurrent();

        /// <summary>
        /// Resolves a GL function pointer against the Skia context. Only valid to call while the
        /// Skia context is current (see <see cref="MakeSkiaContextCurrent"/>).
        /// </summary>
        IntPtr GetProcAddress(string name);
    }
}
