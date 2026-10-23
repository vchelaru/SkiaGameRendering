using SkiaSharp;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace SkiaGameRendering.Stride.D3D12
{
    /// <summary>
    /// The <see cref="SceneRendererBase"/> hook Stride needs to run a Skia draw every frame. D3D12
    /// analog of <c>SkiaGameRendering.Stride.D3D11</c>'s <c>SkiaStrideSceneRenderer</c>. Add it to the
    /// compositor, e.g. with the Stride Community Toolkit's <c>Game.AddSceneRenderer</c>:
    /// <code>
    /// var canvas = new SkiaStrideD3D12RenderTarget2D(game.GraphicsDevice, 200, 200);
    /// var renderer = new SkiaStrideD3D12SceneRenderer { Canvas = canvas };
    /// renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
    /// game.AddSceneRenderer(renderer);
    /// </code>
    /// </summary>
    public class SkiaStrideD3D12SceneRenderer : SceneRendererBase
    {
        /// <summary>
        /// The render target drawn into and composited every frame. Set this or <see cref="DirectCanvas"/>,
        /// not both. No-op while both are null.
        /// </summary>
        public SkiaStrideD3D12RenderTarget2D? Canvas { get; set; }

        /// <summary>
        /// Draws straight into the compositor's current output every frame, with no intermediate texture.
        /// Set this or <see cref="Canvas"/>, not both.
        /// </summary>
        public SkiaStrideD3D12DirectCanvas? DirectCanvas { get; set; }

        /// <summary>
        /// Raised once per frame, between <c>Begin</c> and <c>End</c> of whichever canvas is set, with the
        /// canvas to draw on. No-op while unset.
        /// </summary>
        public event Action<SKCanvas>? SkiaDraw;

        protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
        {
            if (SkiaDraw == null)
                return;
            if (Canvas != null && DirectCanvas != null)
                throw new InvalidOperationException(
                    "SkiaStrideD3D12SceneRenderer has both Canvas and DirectCanvas set; set only one.");

            if (DirectCanvas != null)
            {
                DirectCanvas.Begin(drawContext.GraphicsContext);
                try
                {
                    SkiaDraw.Invoke(DirectCanvas.Canvas);
                }
                finally
                {
                    DirectCanvas.End();
                }
            }
            else if (Canvas != null)
            {
                Canvas.Begin();
                try
                {
                    SkiaDraw.Invoke(Canvas.Canvas);
                }
                finally
                {
                    Canvas.End(drawContext.GraphicsContext);
                }
            }
        }
    }
}
