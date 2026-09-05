using Raylib_cs;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using RaylibTexture2D = Raylib_cs.Texture2D;

namespace SkiaGameRendering.Raylib.OGL
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaRaylibRenderTarget2D"/>: the raylib
    /// texture, the FBO/renderbuffer Skia renders into, and the <see cref="SKSurface"/>/
    /// <see cref="GRBackendRenderTarget"/> wrapping it.
    /// </summary>
    internal sealed class SkiaRaylibTarget : IDisposable
    {
        private readonly SkiaRaylibContext _context;
        private RaylibTexture2D _texture;
        private SKSurface? _surface;
        private GRBackendRenderTarget? _renderTarget;
        private GlFramebufferState? _renderState;
        private bool _disposed;

        internal SkiaRaylibTarget(SkiaRaylibContext context, int width, int height, SKColorType colorType)
        {
            _context = context;

            // Allocated on whatever context is current (the engine context, right after
            // InitWindow/each render target's construction) since SetTextureFilter is a
            // high-level rlgl call, not a raw GL call safe to issue on the Skia context.
            uint textureId;
            unsafe
            {
                textureId = Rlgl.LoadTexture(null, width, height, PixelFormat.UncompressedR8G8B8A8, 1);
            }

            _texture = new RaylibTexture2D
            {
                Id = textureId,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8,
            };
            global::Raylib_cs.Raylib.SetTextureFilter(_texture, TextureFilter.Point);

            _context.BeginDraw();
            try
            {
                var result = _context.CreateSurface((int)textureId, width, height, colorType, out var renderState);
                _surface = result.surface;
                _renderTarget = result.renderTarget;
                _renderState = renderState;
            }
            catch
            {
                _context.EndDraw();
                Rlgl.UnloadTexture(textureId);
                throw;
            }
            _context.EndDraw();
        }

        public RaylibTexture2D Texture => _texture;

        internal SKSurface Surface => _surface ?? throw new ObjectDisposedException(nameof(SkiaRaylibTarget));
        internal GlFramebufferState RenderState => _renderState ?? throw new ObjectDisposedException(nameof(SkiaRaylibTarget));

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

            Rlgl.UnloadTexture(_texture.Id);
        }
    }
}
