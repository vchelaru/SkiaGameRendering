namespace Performance
{
    /// <summary>The Skia apps' entries and renderer labels, the same on every engine so result rows line up.</summary>
    public static class SkiaEntries
    {
        public const string TargetLabel = "Skia render target";
        public const string DirectLabel = "Skia direct";
        public const string PreRasterizedSuffix = " (pre-rasterized SVG)";

        /// <param name="direct">False where the host has no <c>SkiaDirectCanvas</c> entry.</param>
        /// <param name="targetSizes">
        /// True where the host draws <see cref="SceneKind.GuideTargetSize"/> scenes into a target of the scene's
        /// <see cref="PerfScene.TargetWidth"/> by <see cref="PerfScene.TargetHeight"/>. Elsewhere those scenes are left out.
        /// </param>
        public static IEnumerable<PerfEntry> For(PerfScene scene, bool direct, bool targetSizes = false)
        {
            // Guide scenes measure a Skia technique rather than the canvas, so the render target row is enough.
            if (scene.SkiaOnly)
            {
                if (scene.Kind != SceneKind.GuideTargetSize || targetSizes)
                    yield return new PerfEntry(scene, "skia", TargetLabel);
                yield break;
            }
            yield return new PerfEntry(scene, "skia", TargetLabel);
            if (direct)
                yield return new PerfEntry(scene, "skia", DirectLabel);
            if (scene.Kind == SceneKind.Svg)
            {
                yield return new PerfEntry(scene, "skia", TargetLabel + PreRasterizedSuffix);
                if (direct)
                    yield return new PerfEntry(scene, "skia", DirectLabel + PreRasterizedSuffix);
            }
        }

        public static bool IsDirect(PerfEntry entry) => entry.Renderer.StartsWith(DirectLabel);

        public static SkiaVariant VariantOf(PerfEntry entry) =>
            entry.Renderer.EndsWith(PreRasterizedSuffix) ? SkiaVariant.PreRasterizedSvg : SkiaVariant.Default;
    }
}
