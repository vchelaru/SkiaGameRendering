using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    /// <summary>
    /// The render target KNI has bound, for <see cref="SkiaSharedGlBackend"/>'s direct drawing. Linked as
    /// source into every KNI GL backend (DesktopGL and Android).
    /// </summary>
    internal sealed class KniBoundRenderTarget
    {
        // KNI's HiDef profile allows four simultaneous targets; GetRenderTargets needs room for all of them.
        private readonly RenderTargetBinding[] _bindings = new RenderTargetBinding[4];

        /// <summary>The one bound <see cref="RenderTarget2D"/>, or null for the back buffer.</summary>
        internal RenderTarget2D? Get(GraphicsDevice graphicsDevice)
        {
            var count = graphicsDevice.GetRenderTargets(_bindings);
            var first = _bindings[0].RenderTarget;
            Array.Clear(_bindings);

            if (count == 0)
                return null;
            if (count > 1)
                throw new NotSupportedException("SkiaDirectCanvas needs a single bound render target, not " + count + ".");
            return first as RenderTarget2D
                ?? throw new NotSupportedException("SkiaDirectCanvas supports a RenderTarget2D, not a cube or 3D target.");
        }
    }
}
