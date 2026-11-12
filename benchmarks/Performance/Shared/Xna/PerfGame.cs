using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    /// <summary>
    /// The MonoGame and KNI host for <see cref="PerfRunner"/>: an uncapped window (no vsync, no fixed timestep)
    /// whose Draw runs one runner frame, and which exits once the runner has written its results. Subclasses
    /// only list their entries and draw them. The arguments are <see cref="PerfOptions"/>.
    /// </summary>
    public abstract class PerfGame : Game, IPerfHost
    {
        public const int Width = PerfRunner.Width;
        public const int Height = PerfRunner.Height;

        protected PerfRunner Runner { get; }
        protected PerfOptions Options => Runner.Options;

        /// <summary>True once every entry has been measured and the results written.</summary>
        public bool Finished => Runner.Finished;

        protected PerfGame(string label, string[] args)
        {
            var options = PerfOptions.Parse(args);
            var graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = Width,
                PreferredBackBufferHeight = Height,
                SynchronizeWithVerticalRetrace = false,
                GraphicsProfile = GraphicsProfile.HiDef,
                PreferMultiSampling = options.MultiSampleCount > 0,
            };
            if (options.MultiSampleCount > 0)
                graphics.PreparingDeviceSettings += (_, e) =>
                    e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = options.MultiSampleCount;
            IsFixedTimeStep = false;
            // Otherwise MonoGame sleeps 20 ms per frame while the window is in the background.
            InactiveSleepTime = TimeSpan.Zero;
            IsMouseVisible = true;
            Runner = new PerfRunner(label, options, this);
        }

        protected override void LoadContent()
        {
            LoadRenderer();
            Runner.Start(Entries);
            ExitIfFinished();
        }

        /// <summary>Creates the renderer's GPU resources.</summary>
        protected abstract void LoadRenderer();

        /// <summary>The entries to measure for <paramref name="scene"/>, in order; none when this renderer has nothing to draw for it.</summary>
        protected abstract IEnumerable<PerfEntry> Entries(PerfScene scene);

        /// <inheritdoc cref="IPerfHost.DrawEntry"/>
        protected abstract void DrawEntry(PerfEntry entry, float t);

        /// <inheritdoc cref="IPerfHost.PrepareEntry"/>
        protected virtual void PrepareEntry(PerfEntry entry) { }

        /// <summary>Platform and GPU lines for the results header.</summary>
        protected abstract string GpuDescription { get; }

        /// <summary>What the back buffer got; subclasses add what their own render targets got.</summary>
        protected virtual string MsaaDescription =>
            Options.MultiSampleCount == 0
                ? "off"
                : $"{Options.MultiSampleCount}x requested, back buffer {GraphicsDevice.PresentationParameters.MultiSampleCount}x";

        protected override void Draw(GameTime gameTime)
        {
            Runner.Frame();
            ExitIfFinished();
            base.Draw(gameTime);
        }

        private void ExitIfFinished()
        {
            // A page has no process to end; it reads Finished.
            if (Runner.Finished && PerfEnvironment.Publish == null)
                Exit();
        }

        /// <summary>Saves what is on the back buffer as a PNG.</summary>
        protected virtual void SaveScreenshot(PerfEntry entry, string path)
        {
            var data = new Color[Width * Height];
            GraphicsDevice.GetBackBufferData(data);
            using var texture = new Texture2D(GraphicsDevice, Width, Height);
            texture.SetData(data);
            using var stream = File.Create(path);
            texture.SaveAsPng(stream, Width, Height);
        }

        void IPerfHost.PrepareEntry(PerfEntry entry) => PrepareEntry(entry);
        void IPerfHost.DrawEntry(PerfEntry entry, float t) => DrawEntry(entry, t);
        void IPerfHost.SaveScreenshot(PerfEntry entry, string path) => SaveScreenshot(entry, path);
        bool IPerfHost.IsActive => IsActive;
        void IPerfHost.SetTitle(string title) => Window.Title = title;
        string IPerfHost.GpuDescription => GpuDescription;
        string IPerfHost.MsaaDescription => MsaaDescription;
    }
}
