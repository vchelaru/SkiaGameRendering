using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaRenderTarget2D"/>.
    /// </summary>
    internal abstract class SkiaTarget : IDisposable
    {
        public abstract Texture2D Texture { get; }

        /// <summary>Reads pixels back through Skia's own surface. Only valid mid-draw.</summary>
        internal virtual bool ReadPixels(SKImageInfo dstInfo, IntPtr dstPixels, int dstRowBytes, int srcX, int srcY) =>
            throw new NotSupportedException($"{GetType().Name} does not support reading pixels back through Skia.");

        internal abstract void DisposeSkiaResources();
        internal abstract void DisposeGraphicsResources();

        public void Dispose()
        {
            DisposeSkiaResources();
            DisposeGraphicsResources();
            GC.SuppressFinalize(this);
        }
    }
}
