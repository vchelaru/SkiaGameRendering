using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaRenderTarget2D"/>.
    /// </summary>
    internal abstract class SkiaTarget : IDisposable
    {
        public abstract Texture2D Texture { get; }

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
