using SkiaSharp;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace SkiaGameRendering.Stride.D3D11
{
    /// <summary>
    /// The <see cref="SceneRendererBase"/> hook Stride needs to run a Skia draw every frame. Stride
    /// drives rendering through its <c>GraphicsCompositor</c> rather than a user-owned <c>Draw()</c>
    /// call the way MonoGame/KNI do, so <see cref="SkiaStrideRenderTarget2D.Begin"/>/<c>End</c> have
    /// to run inside <see cref="DrawCore"/> instead of an arbitrary call site.
    /// <para>
    /// Add an instance of this to the compositor so it runs every frame - e.g. via the Stride
    /// Community Toolkit's <c>Game.AddSceneRenderer</c>, which appends it after the existing scene
    /// renderer so it draws on top of everything else:
    /// <code>
    /// var renderer = new SkiaStrideSceneRenderer { DirectCanvas = new SkiaStrideDirectCanvas(game.GraphicsDevice) };
    /// renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
    /// game.AddSceneRenderer(renderer);
    /// </code>
    /// </para>
    /// </summary>
    public class SkiaStrideSceneRenderer : SceneRendererBase
    {
        /// <summary>
        /// The render target drawn into and composited every frame. Set this or <see cref="DirectCanvas"/>,
        /// not both. No-op while both are null.
        /// </summary>
        public SkiaStrideRenderTarget2D? Canvas { get; set; }

        /// <summary>
        /// Draws straight into the compositor's current output every frame, with no intermediate texture.
        /// Set this or <see cref="Canvas"/>, not both.
        /// </summary>
        public SkiaStrideDirectCanvas? DirectCanvas { get; set; }

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
                    "SkiaStrideSceneRenderer has both Canvas and DirectCanvas set; set only one.");

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
