using Stride.Graphics;

namespace SkiaGameRendering.Stride.D3D12
{
    /// <summary>
    /// Holds the shared <see cref="SkiaStrideD3D12Context"/> for a Stride <see cref="GraphicsDevice"/>.
    /// D3D12 analog of <c>SkiaGameRendering.Stride.D3D11</c>'s <c>SkiaStrideRenderer</c>. Constructing a
    /// <see cref="SkiaStrideD3D12RenderTarget2D"/> auto-initializes it; call <see cref="Initialize"/>
    /// explicitly only to make initialization (and any failure) happen at a known point.
    /// </summary>
    public static class SkiaStrideD3D12Renderer
    {
        static SkiaStrideD3D12Context? _context;
        static GraphicsDevice? _graphicsDevice;

        public static bool IsInitialized => _context != null;

        public static void Initialize(GraphicsDevice graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);

            if (_context != null)
            {
                if (ReferenceEquals(_graphicsDevice, graphicsDevice))
                    return;

                throw new InvalidOperationException(
                    "SkiaStrideD3D12Renderer is already initialized. Call SkiaStrideD3D12Renderer.Dispose() before switching GraphicsDevice.");
            }

            var context = new SkiaStrideD3D12Context();
            try
            {
                context.Initialize(graphicsDevice);
            }
            catch
            {
                context.Dispose();
                throw;
            }
            _context = context;
            _graphicsDevice = graphicsDevice;
        }

        /// <summary>
        /// Called by <see cref="SkiaStrideD3D12RenderTarget2D"/>'s constructor. Auto-initializes
        /// against <paramref name="graphicsDevice"/> if nothing has initialized the renderer yet.
        /// </summary>
        internal static SkiaStrideD3D12Context EnsureInitialized(GraphicsDevice graphicsDevice)
        {
            if (_context == null)
            {
                Initialize(graphicsDevice);
            }
            else if (!ReferenceEquals(_graphicsDevice, graphicsDevice))
            {
                throw new InvalidOperationException(
                    "A SkiaStrideD3D12RenderTarget2D was constructed with a different GraphicsDevice than the one " +
                    "SkiaStrideD3D12Renderer is currently initialized with.");
            }

            return _context!;
        }

        /// <summary>
        /// Disposes the shared context. Dispose any live <see cref="SkiaStrideD3D12RenderTarget2D"/>
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
