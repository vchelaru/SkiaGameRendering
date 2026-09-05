using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering.Stride.VK
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaStrideVulkanRenderTarget2D"/>: the Stride
    /// <see cref="Texture"/>, the wrapped-image state, and the <see cref="SKSurface"/>/
    /// <see cref="GRBackendRenderTarget"/> wrapping it. Mirrors <c>SkiaGameRendering.Stride</c>'s
    /// D3D11 <c>SkiaStrideTarget</c>, minus the ANGLE-specific bind/unbind step - Vulkan has no
    /// separate GL-style context to make current, so there is nothing to bind beyond the queue lock
    /// <see cref="SkiaStrideVulkanContext.BeginDraw"/>/<c>EndDraw</c> already handle.
    /// </summary>
    internal sealed class SkiaStrideVulkanTarget : IDisposable
    {
        readonly SkiaStrideVulkanContext _context;
        Texture? _texture;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        VkTextureState? _renderState;
        bool _disposed;

        internal SkiaStrideVulkanTarget(
            SkiaStrideVulkanContext context, GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType)
        {
            _context = context;

            // TextureFlags.RenderTarget sets VkImageUsageFlags.ColorAttachment (see
            // SkiaStrideVulkanContext.ComputeImageUsageFlags), which Skia's wrapped surface needs.
            // Stride.Graphics.Texture.New2D allocates the underlying VkImage eagerly, same as the
            // D3D11 path - no lazy-allocation workaround needed before reflecting the image out.
            _texture = Texture.New2D(
                graphicsDevice, width, height, ToPixelFormat(colorType),
                TextureFlags.RenderTarget | TextureFlags.ShaderResource);

            _context.BeginDraw();
            try
            {
                _renderState = _context.CreateTextureState(_texture);
                var result = _context.CreateSurface(_renderState, width, height, colorType);
                _surface = result.surface;
                _renderTarget = result.renderTarget;
            }
            catch
            {
                _context.EndDraw();
                _texture.Dispose();
                throw;
            }
            _context.EndDraw();
        }

        internal Texture Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaStrideVulkanTarget));

        internal SKSurface Surface => _surface ?? throw new ObjectDisposedException(nameof(SkiaStrideVulkanTarget));

        /// <summary>Mirrors <c>SkiaStrideTarget.ToPixelFormat</c> (the D3D11 adapter's SKColorType -> Stride PixelFormat map) - graphics-API-agnostic on the Stride side, so the mapping itself is identical.</summary>
        static PixelFormat ToPixelFormat(SKColorType colorType) => colorType switch
        {
            SKColorType.Rgba1010102 => PixelFormat.R10G10B10A2_UNorm,
            SKColorType.Rgba16161616 => PixelFormat.R16G16B16A16_UNorm,
            SKColorType.Alpha8 => PixelFormat.A8_UNorm,
            SKColorType.Bgra8888 => PixelFormat.B8G8R8A8_UNorm,
            _ => PixelFormat.R8G8B8A8_UNorm,
        };

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _context.BeginDraw();
            try
            {
                _surface?.Dispose();
                _surface = null;
                _renderTarget?.Dispose();
                _renderTarget = null;
                // VkTextureState (unlike ANGLE's AngleTextureState) owns no unmanaged resource of its
                // own - the wrapped VkImage is host-owned and released below when _texture disposes,
                // so there is nothing further to release here beyond dropping the reference.
                _renderState = null;
            }
            finally
            {
                _context.EndDraw();
            }

            _texture?.Dispose();
            _texture = null;
        }
    }
}
