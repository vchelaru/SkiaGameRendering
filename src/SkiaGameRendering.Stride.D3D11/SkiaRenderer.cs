using Stride.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// Holds the shared <see cref="SkiaStrideContext"/> for a Stride <see cref="GraphicsDevice"/>.
    /// Most code never calls this directly - constructing a <see cref="SkiaRenderTarget2D"/>
    /// auto-initializes it. Call <see cref="Initialize"/> explicitly only to make initialization
    /// (and any failure) happen at a known point rather than lazily on first render target
    /// construction.
    /// </summary>
    public static class SkiaRenderer
    {
        static SkiaStrideContext? _context;
        static GraphicsDevice? _graphicsDevice;

        public static bool IsInitialized => _context != null;

        /// <summary>
        /// Always false: <see cref="SkiaRenderTarget2D"/>'s <c>multiSampleCount</c> exists for a portable API, but only 0
        /// works here and 2, 4 and 8 throw <see cref="NotSupportedException"/>. Needs no initialization to answer.
        /// </summary>
        public static bool SupportsMultiSampledTargets => false;

        public static void Initialize(GraphicsDevice graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);

            if (_context != null)
            {
                if (ReferenceEquals(_graphicsDevice, graphicsDevice))
                    return;

                throw new InvalidOperationException(
                    "SkiaRenderer is already initialized. Call SkiaRenderer.Dispose() before switching GraphicsDevice.");
            }

            var context = new SkiaStrideContext();
            context.Initialize(graphicsDevice);
            _context = context;
            _graphicsDevice = graphicsDevice;
        }

        /// <summary>
        /// Called by the <see cref="SkiaRenderTarget2D"/> and <see cref="SkiaDirectCanvas"/> constructors. Auto-initializes against
        /// <paramref name="graphicsDevice"/> if nothing has initialized the renderer yet.
        /// </summary>
        internal static SkiaStrideContext EnsureInitialized(GraphicsDevice graphicsDevice)
        {
            if (_context == null)
            {
                Initialize(graphicsDevice);
            }
            else if (!ReferenceEquals(_graphicsDevice, graphicsDevice))
            {
                throw new InvalidOperationException(
                    "A SkiaRenderTarget2D or SkiaDirectCanvas was constructed with a different GraphicsDevice than the one " +
                    "SkiaRenderer is currently initialized with.");
            }

            return _context!;
        }

        /// <summary>
        /// Disposes the shared context. Dispose any live <see cref="SkiaRenderTarget2D"/>
        /// instances first - this does not track or dispose them for you.
        /// </summary>
        public static void Dispose()
        {
            if (_context == null)
                return;

            var context = _context;
            _context = null;
            _graphicsDevice = null;
            context.Dispose();
        }
    }
}
