using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaRenderTarget2D"/>: the Stride
    /// <see cref="Texture"/>, the ANGLE render state, and the <see cref="SKSurface"/>/
    /// <see cref="GRBackendRenderTarget"/> wrapping it.
    /// <para>
    /// <b>Linear color space</b> is handled as in <c>SkiaStrideVulkanTarget</c> (see its doc comment): the texture
    /// keeps sRGB-encoded bytes and is composited through an sRGB view, so Stride's encode-on-write cancels the
    /// view's decode on sample. D3D11 allows that view only of a typeless resource, so under Linear the texture is
    /// created typeless with a UNORM default view, which ANGLE wraps as UNORM.
    /// </para>
    /// </summary>
    internal sealed class SkiaStrideTarget : IDisposable
    {
        readonly SkiaStrideContext _context;
        Texture? _texture;
        Texture? _sampleView;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        AngleTextureState? _renderState;
        bool _disposed;

        internal SkiaStrideTarget(
            SkiaStrideContext context, GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType)
        {
            _context = context;

            // TextureFlags.RenderTarget sets D3D11_BIND_RENDER_TARGET, which ANGLE requires - unlike
            // MonoGame WindowsDX's Texture2D (SkiaAngleBackend.CreateTexture substitutes
            // RenderTarget2D for this), Stride's Texture.New2D takes the flag directly. Stride also
            // allocates the D3D11 resource eagerly, so (also unlike SkiaAngleBackend) no lazy-
            // allocation workaround is needed to force GPU resource creation before reflecting it out.
            var format = ToPixelFormat(colorType);
            var srgbFormat = format.ToSRgb();
            if (graphicsDevice.ColorSpace == ColorSpace.Linear && srgbFormat != format)
            {
                // Under Stride's Linear pipeline the back buffer is sRGB-format and encodes on write.
                // Sampling this gamma-encoded texture through an sRGB view decodes it first, so the
                // encode cancels it and the bytes Skia drew land unchanged. D3D11 only allows that view
                // of a typeless resource, so the texture is typeless with a plain UNORM default view.
                var typeless = format == PixelFormat.B8G8R8A8_UNorm ? PixelFormat.B8G8R8A8_Typeless : PixelFormat.R8G8B8A8_Typeless;
                var flags = TextureFlags.RenderTarget | TextureFlags.ShaderResource;
                _texture = Texture.New(
                    graphicsDevice,
                    TextureDescription.New2D(width, height, typeless, flags),
                    new TextureViewDescription { Format = format, Type = ViewType.Single, Flags = flags });
                _sampleView = _texture.ToTextureView(new TextureViewDescription
                {
                    Format = srgbFormat,
                    Type = ViewType.Single,
                    ArraySlice = 0,
                    MipLevel = 0,
                    Flags = TextureFlags.ShaderResource,
                });
            }
            else
            {
                _texture = Texture.New2D(graphicsDevice, width, height, format, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
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

        internal Texture Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaStrideTarget));

        internal SKSurface Surface => _surface ?? throw new ObjectDisposedException(nameof(SkiaStrideTarget));

        internal AngleTextureState RenderState => _renderState ?? throw new ObjectDisposedException(nameof(SkiaStrideTarget));

        /// <summary>Mirrors <c>SkiaBackend.ToSurfaceFormat</c> (MonoGame's SKColorType -> SurfaceFormat map).</summary>
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

            _context.DisposeSurface(_renderState, _surface, _renderTarget);
            _surface = null;
            _renderTarget = null;
            _renderState = null;

            _sampleView?.Dispose();
            _sampleView = null;
            _texture?.Dispose();
            _texture = null;
        }
    }
}
