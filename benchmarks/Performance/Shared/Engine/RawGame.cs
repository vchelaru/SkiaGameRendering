namespace Performance
{
    /// <summary>The baseline app: the engine's own drawing, no Skia anywhere in the process.</summary>
    public sealed class RawGame : PerfGame
    {
        private EngineRenderer _engine = null!;

        public RawGame(string label, string[] args) : base(label, args) { }

        protected override void LoadRenderer() => _engine = new EngineRenderer(GraphicsDevice);

        protected override IEnumerable<PerfEntry> Entries(PerfScene scene) =>
            EngineRenderer.Renderers(scene).Select(renderer => new PerfEntry(scene, "engine", renderer));

        protected override void PrepareEntry(PerfEntry entry) => _engine.PrepareText(entry.Scene);

        protected override void DrawEntry(PerfEntry entry, float t) => _engine.Draw(entry.Scene, entry.Renderer, t);

        protected override string GpuDescription => GpuInfo.Query(GraphicsDevice);

        protected override void UnloadContent() => _engine.Dispose();
    }
}
