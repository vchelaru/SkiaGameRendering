using Microsoft.Xna.Framework;
using SkiaGameRendering;
using SkiaSharp;

namespace Performance
{
    /// <summary>
    /// The same scenes drawn through SkiaGameRendering, once per mode:
    /// <list type="bullet">
    /// <item><b>Skia render target</b>: one screen-sized <see cref="SkiaRenderTarget2D"/>, composited with
    /// <see cref="SkiaRenderTarget2D.End"/>. This is the original path.</item>
    /// <item><b>Skia direct</b>: a <see cref="SkiaDirectCanvas"/> drawing straight into the back buffer, with no
    /// intermediate texture and no composite.</item>
    /// </list>
    /// Everything a game using the library pays per frame is in the measurement: the context switch,
    /// Skia's flush and, for the render target, the composite.
    /// </summary>
    public sealed class SkiaGame : PerfGame
    {
        private SkiaRenderTarget2D? _target;
        private string? _targetUnavailable;
        private SkiaDirectCanvas? _direct;
        private string? _directUnavailable;
        private SkiaSceneDrawer _drawer = null!;

        public SkiaGame(string label, string[] args) : base(label, args) { }

        protected override void LoadRenderer()
        {
            try
            {
                // --msaa reaches the render target too; backends without multisampled targets refuse it.
                _target = new SkiaRenderTarget2D(GraphicsDevice, Width, Height, SKColorType.Rgba8888, Options.MultiSampleCount);
            }
            catch (Exception exception)
            {
                _targetUnavailable = $"{exception.GetType().Name}: {exception.Message}";
            }
            try
            {
                _direct = new SkiaDirectCanvas(GraphicsDevice);
            }
            catch (Exception exception)
            {
                _directUnavailable = $"{exception.GetType().Name}: {exception.Message}";
            }
            _drawer = new SkiaSceneDrawer(Options.Antialias);
        }

        protected override IEnumerable<PerfEntry> Entries(PerfScene scene) => SkiaEntries.For(scene, direct: true);

        protected override void DrawEntry(PerfEntry entry, float t)
        {
            GraphicsDevice.Clear(Color.Black);

            var variant = SkiaEntries.VariantOf(entry);
            if (SkiaEntries.IsDirect(entry))
            {
                if (_direct == null)
                    throw new NotSupportedException(_directUnavailable ?? "SkiaDirectCanvas is not supported here.");

                _direct.Begin();
                try
                {
                    _drawer.Prepare(ContextOf(_direct.Canvas), entry.Scene);
                    _drawer.Draw(_direct.Canvas, entry.Scene, t, variant);
                }
                finally
                {
                    _direct.End();
                }
            }
            else
            {
                if (_target == null)
                    throw new NotSupportedException(_targetUnavailable ?? "SkiaRenderTarget2D is not supported here.");

                _target.Begin();
                try
                {
                    // Uploads need Skia's GPU context current, which it only is between Begin and End.
                    _drawer.Prepare(ContextOf(_target.Canvas), entry.Scene);
                    _drawer.Draw(_target.Canvas, entry.Scene, t, variant);
                }
                finally
                {
                    _target.End();
                }
            }
        }

        // The context the canvas draws on: the browser backend uses a different one for each mode.
        private static GRContext ContextOf(SKCanvas canvas) =>
            canvas.Context as GRContext ?? SkiaRenderer.CurrentBackend!.GRContext!;

        protected override string GpuDescription => GpuInfo.Query(GraphicsDevice);

        protected override string MsaaDescription =>
            Options.MultiSampleCount == 0
                ? base.MsaaDescription
                : $"{base.MsaaDescription}, Skia render target {(_target != null ? $"{Options.MultiSampleCount}x" : "refused")}";

        protected override void UnloadContent()
        {
            _drawer.Dispose();
            _direct?.Dispose();
            _target?.Dispose();
            SkiaRenderer.Dispose();
        }
    }
}
