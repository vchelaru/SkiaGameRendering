using SkiaGameRendering.Core.D3D12;
using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering.Stride.D3D12
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaStrideD3D12RenderTarget2D"/>: the Stride
    /// <see cref="Texture"/>, the wrapped-resource state, and the <see cref="SKSurface"/>/
    /// <see cref="GRBackendRenderTarget"/> wrapping it. Zero-copy: Skia draws straight into Stride's
    /// own <c>ID3D12Resource</c>, which Stride creates with a typed format (only depth formats go
    /// typeless in <c>Texture.ConvertToNativeDescription2D</c>), unlike Godot's typeless textures that
    /// force the Godot D3D12 backend to copy.
    /// <para>
    /// <b>Linear-color-space compensation</b> is the same as <c>SkiaStrideVulkanTarget</c>'s (see its
    /// doc comment): under Stride's default Linear pipeline the back buffer is sRGB-formatted, so the
    /// surface is tagged <c>SKColorSpace.CreateSrgbLinear()</c> to cancel the hardware encode-on-write.
    /// </para>
    /// </summary>
    internal sealed class SkiaStrideD3D12Target : IDisposable
    {
        readonly SkiaStrideD3D12Context _context;
        Texture? _texture;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        bool _disposed;

        internal SkiaStrideD3D12Target(
            SkiaStrideD3D12Context context, GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType)
        {
            _context = context;

            _texture = Texture.New2D(
                graphicsDevice, width, height, ToPixelFormat(colorType),
                TextureFlags.RenderTarget | TextureFlags.ShaderResource);

            try
            {
                BeginDraw();
                try
                {
                    var state = _context.CreateTextureState(_texture);
                    var skiaColorSpace = graphicsDevice.ColorSpace == ColorSpace.Linear
                        ? SKColorSpace.CreateSrgbLinear()
                        : null;
                    (_surface, _renderTarget) = _context.CreateSurface(state, width, height, colorType, skiaColorSpace);
                }
                finally
                {
                    EndDraw();
                }
            }
            catch
            {
                _surface?.Dispose();
                _renderTarget?.Dispose();
                _context.WaitForIdle();
                _texture.Dispose();
                throw;
            }
        }

        internal Texture Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaStrideD3D12Target));

        internal SKSurface Surface => _surface ?? throw new ObjectDisposedException(nameof(SkiaStrideD3D12Target));

        /// <summary>
        /// Takes Stride's queue lock and hands the texture to Skia in <c>RENDER_TARGET</c>. Paired with
        /// <see cref="EndDraw"/>; see <see cref="SkiaStrideD3D12Context"/> for the handoff.
        /// </summary>
        internal void BeginDraw()
        {
            _context.BeginDraw();
            try
            {
                _context.AcquireForSkia(Texture);
            }
            catch
            {
                _context.EndDraw();
                throw;
            }
        }

        /// <summary>
        /// Submits whatever Skia recorded, releases the queue lock, and hands the texture back to
        /// Stride in <c>COMMON</c>.
        /// </summary>
        internal void EndDraw()
        {
            try
            {
                _surface?.Flush();
            }
            finally
            {
                _context.EndDraw();
            }
            _context.ReleaseToStride(Texture);
        }

        /// <summary>Same map as the other Stride adapters. Stride's <see cref="PixelFormat"/> values are DXGI formats.</summary>
        static PixelFormat ToPixelFormat(SKColorType colorType) => colorType switch
        {
            SKColorType.Rgba1010102 => PixelFormat.R10G10B10A2_UNorm,
            SKColorType.Rgba16161616 => PixelFormat.R16G16B16A16_UNorm,
            SKColorType.Bgra8888 => PixelFormat.B8G8R8A8_UNorm,
            SKColorType.Rgba8888 => PixelFormat.R8G8B8A8_UNorm,
            // Only the four formats Core.D3D12 names, the same set the Godot D3D12 backend accepts.
            _ => throw new NotSupportedException($"SkiaGameRendering.Stride.D3D12 does not support SKColorType.{colorType}."),
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
                // Skia drops its reference to the wrapped resource once its own work is done.
                _context.GRContext.PurgeResources();
            }
            finally
            {
                _context.EndDraw();
            }

            // The transitioner's queued barriers hold no reference to the resource (Skia's do), so
            // drain them before Stride releases its own.
            _context.WaitForIdle();
            _texture?.Dispose();
            _texture = null;
        }
    }
}
