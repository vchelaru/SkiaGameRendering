using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering.Stride
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaStrideRenderTarget2D"/>: the Stride
    /// <see cref="Texture"/>, the ANGLE render state, and the <see cref="SKSurface"/>/
    /// <see cref="GRBackendRenderTarget"/> wrapping it.
    /// </summary>
    internal sealed class SkiaStrideTarget : IDisposable
    {
        readonly SkiaStrideContext _context;
        Texture? _texture;
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

            _context.BeginDraw();
            try
            {
                _surface?.Dispose();
                _surface = null;
                _renderTarget?.Dispose();
                _renderTarget = null;

                if (_renderState != null)
                {
                    _context.DisposeRenderState(_renderState);
                    _renderState = null;
                }
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
