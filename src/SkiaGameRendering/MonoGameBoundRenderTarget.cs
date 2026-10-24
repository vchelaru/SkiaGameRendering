using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// The render target MonoGame has bound, for <see cref="SkiaDirectCanvas"/>. Linked as source into
    /// every MonoGame backend that reads it through public API (DesktopGL, WindowsDX and Android).
    /// </summary>
    internal sealed class MonoGameBoundRenderTarget
    {
        // MonoGame's HiDef profile allows four simultaneous targets.
        private readonly RenderTargetBinding[] _bindings = new RenderTargetBinding[4];

        /// <summary>The one bound <see cref="RenderTarget2D"/>, or null for the back buffer.</summary>
        internal RenderTarget2D? Get(GraphicsDevice graphicsDevice)
        {
            var count = graphicsDevice.RenderTargetCount;
            if (count == 0)
                return null;
            if (count > 1)
                throw new NotSupportedException("SkiaDirectCanvas needs a single bound render target, not " + count + ".");

            graphicsDevice.GetRenderTargets(_bindings);
            var renderTarget = _bindings[0].RenderTarget as RenderTarget2D;
            Array.Clear(_bindings);
            return renderTarget
                ?? throw new NotSupportedException("SkiaDirectCanvas supports a RenderTarget2D, not a cube or 3D target.");
        }
    }
}
