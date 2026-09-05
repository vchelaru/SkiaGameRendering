using SkiaSharp;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace SkiaGameRendering.Stride
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
    /// var canvas = new SkiaStrideRenderTarget2D(game.GraphicsDevice, 200, 200);
    /// var renderer = new SkiaStrideSceneRenderer { Canvas = canvas };
    /// renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
    /// game.AddSceneRenderer(renderer);
    /// </code>
    /// </para>
    /// </summary>
    public class SkiaStrideSceneRenderer : SceneRendererBase
    {
        /// <summary>The render target drawn into and composited every frame. No-op while null.</summary>
        public SkiaStrideRenderTarget2D? Canvas { get; set; }

        /// <summary>
        /// Raised once per frame, between <see cref="SkiaStrideRenderTarget2D.Begin"/> and
        /// <c>End</c>, with the canvas to draw on. No-op while unset.
        /// </summary>
        public event Action<SKCanvas>? SkiaDraw;

        protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
        {
            if (Canvas == null || SkiaDraw == null)
                return;

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
