namespace SkiaGameRendering.Raylib.OGL
{
    /// <summary>
    /// Holds the shared Skia GL context (<see cref="SkiaRaylibContext"/>) for the current raylib
    /// window. Most code never calls this directly - constructing a
    /// <see cref="SkiaRaylibRenderTarget2D"/> auto-initializes it. Call <see cref="Initialize"/>
    /// explicitly only to make initialization (and any failure) happen at a known point rather
    /// than lazily on first render target construction.
    /// <para>
    /// Must be called after <c>Raylib.InitWindow</c> - it reads the window handle via
    /// <c>Raylib.GetWindowHandle()</c>, which is only valid once the window exists. raylib only
    /// supports a single window, so this holds a single global context rather than keying off a
    /// window handle the way <c>SkiaRenderer</c> keys off a MonoGame <c>GraphicsDevice</c>.
    /// </para>
    /// </summary>
    public static class SkiaRaylibRenderer
    {
        private static SkiaRaylibContext? _context;

        public static bool IsInitialized => _context != null;

        public static void Initialize()
        {
            if (_context != null)
                return;

            var context = new SkiaRaylibContext();
            context.Initialize();
            _context = context;
        }

        /// <summary>
        /// Called by <see cref="SkiaRaylibRenderTarget2D"/>'s constructor. Auto-initializes if
        /// nothing has initialized the renderer yet.
        /// </summary>
        internal static SkiaRaylibContext EnsureInitialized()
        {
            if (_context == null)
                Initialize();

            return _context!;
        }

        /// <summary>
        /// Disposes the shared context. Dispose any live <see cref="SkiaRaylibRenderTarget2D"/>
        /// instances first - this does not track or dispose them for you.
        /// </summary>
        public static void Dispose()
        {
            if (_context == null)
                return;

            var context = _context;
            _context = null;
            context.Dispose();
        }
    }
}
