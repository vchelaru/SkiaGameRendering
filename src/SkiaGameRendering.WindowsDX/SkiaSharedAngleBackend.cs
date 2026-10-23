using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// The part every D3D11 backend that draws through ANGLE on the engine's own device has in common:
    /// MonoGame WindowsDX, KNI WindowsDX and FNA's D3D11 driver. A subclass supplies the engine's device,
    /// where the engine keeps a texture's D3D11 resource, and which render target it has bound. The
    /// interop itself lives in <see cref="AngleSkiaSurfaceFactory"/>. Lives in the MonoGame WindowsDX
    /// project and is linked as source into the KNI and FNA ones.
    /// </summary>
    public abstract class SkiaSharedAngleBackend : SkiaBackend
    {
        private protected readonly AngleSkiaSurfaceFactory _factory = new();

        public override GRContext GRContext => _factory.GRContext;

        internal override void BeginDraw() => _factory.BeginDraw();

        internal override void EndDraw() => _factory.EndDraw();

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var state = (AngleTextureState)textureHandle;
            var result = _factory.CreateSurface(state, width, height, colorType);
            renderState = state;
            return result;
        }

        internal override void BindForDrawing(object renderState) => _factory.BindForDrawing((AngleTextureState)renderState);

        internal override void UnbindAfterDrawing() => _factory.UnbindAfterDrawing();

        internal override void DisposeRenderState(object renderState) => _factory.DisposeRenderState((AngleTextureState)renderState);

        internal override void DisposeTargetSkiaResources(SKSurface? surface, GRBackendRenderTarget? renderTarget, object? renderState) =>
            _factory.DisposeSurface((AngleTextureState?)renderState, surface, renderTarget);

        public override void Dispose() => _factory.Dispose();

        // --- Drawing straight into the engine's current target (SkiaDirectCanvas) ---
        //
        // The engine's render target is whatever it has bound on its D3D11 immediate context, which ANGLE
        // shares. AngleSkiaSurfaceFactory reads that binding and wraps the texture behind it, so the engine
        // only has to say which RenderTarget2D it thinks is bound, to check the two agree.

        /// <summary>
        /// The one <see cref="RenderTarget2D"/> the engine has bound, or null for the back buffer. Throws
        /// <see cref="NotSupportedException"/> for several bound targets or a non-2D one.
        /// </summary>
        private protected abstract RenderTarget2D? GetBoundRenderTarget();

        internal override bool SupportsDirectCanvas => true;

        internal override SKCanvas BeginDirectDraw(out object pass)
        {
            var renderTarget = GetBoundRenderTarget();
            int width, height;
            if (renderTarget == null)
            {
                var parameters = GraphicsDevice.PresentationParameters;
                width = parameters.BackBufferWidth;
                height = parameters.BackBufferHeight;
            }
            else
            {
                if (renderTarget.Format != SurfaceFormat.Color)
                    throw new NotSupportedException(
                        $"SkiaDirectCanvas supports SurfaceFormat.Color render targets, not {renderTarget.Format}.");
                width = renderTarget.Width;
                height = renderTarget.Height;
            }

            var canvas = _factory.BeginDirectDraw(width, height, out var anglePass);
            pass = anglePass;
            return canvas;
        }

        internal override void EndDirectDraw(object pass) => _factory.EndDirectDraw((AngleDirectPass)pass);
    }
}
