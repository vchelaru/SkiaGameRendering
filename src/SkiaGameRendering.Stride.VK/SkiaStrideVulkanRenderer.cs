using Stride.Graphics;

namespace SkiaGameRendering.Stride.VK
{
    /// <summary>
    /// Holds the shared <see cref="SkiaStrideVulkanContext"/> for a Stride <see cref="GraphicsDevice"/>.
    /// Vulkan analog of <c>SkiaGameRendering.Stride.D3D11</c>'s D3D11 <c>SkiaStrideRenderer</c>. Most code
    /// never calls this directly - constructing a <see cref="SkiaStrideVulkanRenderTarget2D"/>
    /// auto-initializes it. Call <see cref="Initialize"/> explicitly only to make initialization
    /// (and any failure) happen at a known point rather than lazily on first render target
    /// construction.
    /// </summary>
    public static class SkiaStrideVulkanRenderer
    {
        static SkiaStrideVulkanContext? _context;
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
                    "SkiaStrideVulkanRenderer is already initialized. Call SkiaStrideVulkanRenderer.Dispose() before switching GraphicsDevice.");
            }

            var context = new SkiaStrideVulkanContext();
            context.Initialize(graphicsDevice);
            _context = context;
            _graphicsDevice = graphicsDevice;
        }

        /// <summary>
        /// Called by <see cref="SkiaStrideVulkanRenderTarget2D"/>'s constructor. Auto-initializes
        /// against <paramref name="graphicsDevice"/> if nothing has initialized the renderer yet.
        /// </summary>
        internal static SkiaStrideVulkanContext EnsureInitialized(GraphicsDevice graphicsDevice)
        {
            if (_context == null)
            {
                Initialize(graphicsDevice);
            }
            else if (!ReferenceEquals(_graphicsDevice, graphicsDevice))
            {
                throw new InvalidOperationException(
                    "A SkiaStrideVulkanRenderTarget2D was constructed with a different GraphicsDevice than the one " +
                    "SkiaStrideVulkanRenderer is currently initialized with.");
            }

            return _context!;
        }

        /// <summary>
        /// Disposes the shared context. Dispose any live <see cref="SkiaStrideVulkanRenderTarget2D"/>
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
