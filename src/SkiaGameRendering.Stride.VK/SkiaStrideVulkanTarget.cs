using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaRenderTarget2D"/>: the Stride
    /// <see cref="Texture"/>, the wrapped-image state, and the <see cref="SKSurface"/>/
    /// <see cref="GRBackendRenderTarget"/> wrapping it. Mirrors <c>SkiaGameRendering.Stride.D3D11</c>'s
    /// D3D11 <c>SkiaStrideTarget</c>, minus the ANGLE-specific bind/unbind step - Vulkan has no
    /// separate GL-style context to make current, so there is nothing to bind beyond the queue lock
    /// <see cref="SkiaStrideVulkanContext.BeginDraw"/>/<c>EndDraw</c> already handle.
    /// <b>Linear color space.</b> Stride's <c>GraphicsDeviceManager</c> defaults to <c>ColorSpace.Linear</c>, where
    /// the back buffer is sRGB-format and the GPU gamma-encodes whatever is written to it. Skia's bytes are already
    /// sRGB-encoded, so compositing the texture as is encodes them twice and every mid-tone comes out light.
    /// The texture therefore keeps plain sRGB-encoded bytes (Skia blends and interpolates gradients in sRGB, as on
    /// every other runtime), and <see cref="SampleTexture"/> is an sRGB view of it that <see cref="SkiaRenderTarget2D"/>
    /// composites with: the GPU decodes on sample and re-encodes on write, so the bytes land unchanged.
    /// Tagging the Skia surface as linear instead (the earlier approach) cancels the encode too, but then Skia blends
    /// and interpolates in linear space, which lightens gradients and translucent fills, and 8-bit linear storage
    /// bands dark gradients (the shared scene's gradient cell read 196,28,160 against 180,23,158 elsewhere).
    /// Under <c>ColorSpace.Gamma</c> the back buffer is plain and no view is made.
    /// <para>
    /// Like <see cref="SkiaDirectCanvas"/>'s view of the swap chain, the view of this image is a format other than
    /// the one it was created with, which Stride creates without <c>VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT</c>; drivers
    /// accept it for same-size formats (checked on an NVIDIA GPU, not under the validation layer).
    /// </para>
    /// </summary>
    internal sealed class SkiaStrideVulkanTarget : IDisposable
    {
        readonly SkiaStrideVulkanContext _context;
        Texture? _texture;
        Texture? _sampleView;
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

            // Under Stride's Linear pipeline the back buffer is sRGB-format and encodes on write. Sampling
            // this gamma-encoded texture through an sRGB view decodes it first, so the encode cancels it
            // and the bytes Skia drew land unchanged. See the class doc comment.
            if (graphicsDevice.ColorSpace == ColorSpace.Linear)
            {
                var srgb = _texture.ViewFormat.ToSRgb();
                if (srgb != _texture.ViewFormat)
                    _sampleView = _texture.ToTextureView(new TextureViewDescription
                    {
                        Format = srgb,
                        Type = ViewType.Single,
                        ArraySlice = 0,
                        MipLevel = 0,
                        Flags = TextureFlags.ShaderResource,
                    });
            }

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

        /// <summary>What to sample when drawing this target in Stride: <see cref="Texture"/> itself under the Gamma pipeline, an sRGB view of it under Linear.</summary>
        internal Texture SampleTexture => _sampleView ?? Texture;

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

            _sampleView?.Dispose();
            _sampleView = null;
            _texture?.Dispose();
            _texture = null;
        }
    }
}
