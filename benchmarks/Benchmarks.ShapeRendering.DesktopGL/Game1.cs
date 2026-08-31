using System.Diagnostics;
using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaGameRendering;
using SkiaSharp;

namespace Benchmarks.ShapeRendering
{
    public enum RendererKind
    {
        Skia,
        AposShapes,
    }

    /// <summary>
    /// Side-by-side, uncapped-FPS comparison of drawing many animated shapes per frame with
    /// <see cref="SkiaGameRendering"/> (SkiaSharp canvas rendered into a screen-sized
    /// <see cref="SkiaRenderTarget2D"/>, then blitted with <see cref="SpriteBatch"/>) versus
    /// <see cref="Apos.Shapes"/>' <see cref="ShapeBatch"/> (drawn straight to the back buffer).
    ///
    /// The Skia render-target-plus-blit cost is intentionally included every frame - that overhead
    /// is part of the real cost of using this library, not an artifact to optimize away. The
    /// correct backend (OGL vs ANGLE) is auto-detected at runtime based on which library assembly
    /// is referenced, same as the other samples in this repo.
    ///
    /// Controls: 1-8 pick a scene, Up/Down toggle the renderer, B runs an automated benchmark
    /// sweep across every scene/renderer pair and writes benchmark-results.md next to the exe,
    /// Escape quits.
    /// </summary>
    public class Game1 : Game
    {
        private const int BackBufferWidth = 1600;
        private const int BackBufferHeight = 900;

        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch = null!;
        private SpriteFont _font = null!;

        private SkiaRenderTarget2D _skiaCanvas = null!;
        private SKPaint _fillPaint = null!;
        private SKPaint _strokePaint = null!;
        private SKPath _trianglePath = null!;

        private ShapeBatch _shapeBatch = null!;

        private Scene[] _scenes = null!;
        private int _sceneIndex;
        private RendererKind _renderer = RendererKind.Skia;

        private readonly FrameStats _frameStats = new();
        private readonly Stopwatch _wallClock = new();
        private double _lastFrameMs;
        private double _lastRenderMs;
        private double _lastBlitMs;

        private readonly AutoBenchmark _benchmark = new();
        private BenchmarkPhase _previousBenchmarkPhase = BenchmarkPhase.Idle;

        private KeyboardState _previousKeyboard;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = BackBufferWidth,
                PreferredBackBufferHeight = BackBufferHeight,
                GraphicsProfile = GraphicsProfile.HiDef, // required by Apos.Shapes
                SynchronizeWithVerticalRetrace = false,
            };
            IsFixedTimeStep = false;

            Window.AllowUserResizing = false;
            Window.Title = "Skia vs Apos.Shapes";

            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("DefaultFont");

            _skiaCanvas = new SkiaRenderTarget2D(GraphicsDevice, BackBufferWidth, BackBufferHeight);
            _fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            _strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
            _trianglePath = new SKPath();

            _shapeBatch = new ShapeBatch(GraphicsDevice, Content);

            _scenes = Scene.BuildAll(BackBufferWidth, BackBufferHeight);

            _wallClock.Start();
        }

        protected override void Update(GameTime gameTime)
        {
            var keyboard = Keyboard.GetState();

            if (keyboard.IsKeyDown(Keys.Escape))
                Exit();

            bool benchmarkActive = _benchmark.Phase is BenchmarkPhase.Warmup or BenchmarkPhase.Measuring;

            if (!benchmarkActive)
            {
                if (KeyPushed(keyboard, Keys.D1)) _sceneIndex = 0;
                else if (KeyPushed(keyboard, Keys.D2)) _sceneIndex = 1;
                else if (KeyPushed(keyboard, Keys.D3)) _sceneIndex = 2;
                else if (KeyPushed(keyboard, Keys.D4)) _sceneIndex = 3;
                else if (KeyPushed(keyboard, Keys.D5)) _sceneIndex = 4;
                else if (KeyPushed(keyboard, Keys.D6)) _sceneIndex = 5;
                else if (KeyPushed(keyboard, Keys.D7)) _sceneIndex = 6;
                else if (KeyPushed(keyboard, Keys.D8)) _sceneIndex = 7;

                if (KeyPushed(keyboard, Keys.Up) || KeyPushed(keyboard, Keys.Down))
                    _renderer = _renderer == RendererKind.Skia ? RendererKind.AposShapes : RendererKind.Skia;
            }

            if (KeyPushed(keyboard, Keys.B) && _benchmark.Phase is BenchmarkPhase.Idle or BenchmarkPhase.Finished)
                _benchmark.Start(_scenes.Length);

            if (benchmarkActive)
            {
                _sceneIndex = _benchmark.SceneIndex;
                _renderer = _benchmark.Renderer;
            }

            _previousKeyboard = keyboard;
            base.Update(gameTime);
        }

        private bool KeyPushed(KeyboardState current, Keys key) =>
            current.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

        protected override void Draw(GameTime gameTime)
        {
            double wallFrameMs = _wallClock.Elapsed.TotalMilliseconds;
            _wallClock.Restart();
            _lastFrameMs = wallFrameMs;
            _frameStats.AddSample(wallFrameMs);

            var scene = _scenes[_sceneIndex];
            float t = (float)gameTime.TotalGameTime.TotalSeconds;

            var sw = Stopwatch.StartNew();

            if (_renderer == RendererKind.Skia)
            {
                GraphicsDevice.SetRenderTarget(null);

                _skiaCanvas.Begin();
                ShapeRenderers.DrawSceneSkia(_skiaCanvas.Canvas, scene, t, _fillPaint, _strokePaint, _trianglePath);
                _skiaCanvas.End();
                _lastRenderMs = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                // Opaque + full-screen coverage: no separate backbuffer clear needed.
                _spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque);
                _spriteBatch.Draw(_skiaCanvas, Vector2.Zero, Color.White);
                _spriteBatch.End();
                _lastBlitMs = sw.Elapsed.TotalMilliseconds;
            }
            else
            {
                GraphicsDevice.SetRenderTarget(null);
                GraphicsDevice.Clear(Color.Black);

                _shapeBatch.Begin();
                ShapeRenderers.DrawSceneApos(_shapeBatch, scene, t);
                _shapeBatch.End();
                _lastRenderMs = sw.Elapsed.TotalMilliseconds;
                _lastBlitMs = 0;
            }

            DrawHud(scene);

            if (_benchmark.Phase is BenchmarkPhase.Warmup or BenchmarkPhase.Measuring)
                _benchmark.Tick(_lastFrameMs, _lastRenderMs, _lastBlitMs, scene.Name, scene.Count);

            if (_previousBenchmarkPhase != BenchmarkPhase.Finished && _benchmark.Phase == BenchmarkPhase.Finished)
                WriteBenchmarkReport();
            _previousBenchmarkPhase = _benchmark.Phase;

            base.Draw(gameTime);
        }

        private void DrawHud(Scene scene)
        {
            string rendererName = _renderer == RendererKind.Skia ? "Skia (SkiaGameRendering)" : "Apos.Shapes";
            string blitLine = _renderer == RendererKind.Skia ? $"Blit (CPU submit):   {_lastBlitMs:0.000} ms\n" : "";

            string text =
                $"Renderer: {rendererName}\n" +
                $"Scene: {scene.Name}  ({scene.Count} shapes)\n" +
                $"FPS (avg/120): {_frameStats.AverageFps:0.0}\n" +
                $"Frame time (real): {_lastFrameMs:0.000} ms\n" +
                $"Draw (CPU submit):   {_lastRenderMs:0.000} ms\n" +
                blitLine +
                "\n" +
                "1-8 scene | Up/Down renderer | B auto-benchmark | Esc quit\n" +
                _benchmark.StatusLine;

            _spriteBatch.Begin();
            _spriteBatch.DrawString(_font, text, new Vector2(20, 20), Color.White);
            _spriteBatch.End();
        }

        private void WriteBenchmarkReport()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "benchmark-results.md");
            File.WriteAllText(path, _benchmark.ToMarkdownTable());
            Console.WriteLine($"Benchmark complete. Results written to {path}");
            Console.WriteLine(_benchmark.ToMarkdownTable());
        }
    }
}
