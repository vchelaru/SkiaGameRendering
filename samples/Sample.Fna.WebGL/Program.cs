using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SampleShared;
using SkiaGameRendering;
using SkiaSharp;

namespace Sample.Fna.WebGL;

/// <summary>
/// FNA in the browser. "texture" draws the Scene through a SkiaRenderTarget2D. "direct" draws it with
/// SkiaDirectCanvas straight into the back buffer, then layers FNA's SpriteBatch and a RenderTarget2D
/// that Skia also draws into. The page drives frames from requestAnimationFrame; tests call Frame()
/// themselves so they can read the canvas in the same task.
/// </summary>
public class SampleGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private SkiaRenderTarget2D? _canvas;
    private SkiaDirectCanvas? _direct;
    private RenderTarget2D? _renderTarget;

    public string Mode { get; set; } = "texture";
    public int Frames { get; private set; }

    public SampleGame(bool multiSampling)
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 800,
            PreferredBackBufferHeight = 800,
            PreferMultiSampling = multiSampling,
        };
        // GraphicsDeviceManager sets MultiSampleCount to 0 when it creates the first device, so PreferMultiSampling
        // alone never requests MSAA. Setting it here does; FNA3D clamps it to what the context supports.
        if (multiSampling)
            _graphics.PreparingDeviceSettings += (_, e) => e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 4;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        if (Mode == "direct")
            DrawDirect();
        else
            DrawTexture();

        Frames++;
        base.Draw(gameTime);
    }

    private void DrawTexture()
    {
        _canvas ??= new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
        _canvas.Begin();
        Scene.Draw(_canvas.Canvas, 200, 200);
        _canvas.End();
    }

    private void DrawDirect()
    {
        // Skia into a render target, which FNA samples below. Done first: FNA3D discards the back buffer's
        // contents when a render target is bound over it (RenderTargetUsage.DiscardContents).
        _direct ??= new SkiaDirectCanvas(GraphicsDevice);
        _renderTarget ??= new RenderTarget2D(GraphicsDevice, 100, 100);
        GraphicsDevice.SetRenderTarget(_renderTarget);
        GraphicsDevice.Clear(Color.Yellow);
        _direct.Begin();
        using (var paint = new SKPaint { Color = SKColors.Blue })
            _direct.Canvas.DrawRect(0, 0, 20, 20, paint);
        using (var paint = new SKPaint { Color = SKColors.Red, IsAntialias = true })
            _direct.Canvas.DrawCircle(60, 60, 30, paint);
        _direct.End();
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        // The Scene straight into the back buffer.
        _direct.Begin();
        Scene.Draw(_direct.Canvas, 200, 200);
        _direct.End();

        // FNA draws after Skia: the cached GL state must still be FNA's.
        _spriteBatch.Begin();
        _spriteBatch.Draw(_pixel, new Rectangle(400, 50, 100, 100), Color.Lime);
        _spriteBatch.Draw(_renderTarget, new Vector2(400, 300), Color.White);
        _spriteBatch.End();

        // And FNA once more over the Skia draws on the back buffer.
        _spriteBatch.Begin();
        _spriteBatch.Draw(_pixel, new Rectangle(600, 600, 50, 50), Color.Magenta);
        _spriteBatch.End();
    }
}

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static SampleGame? _game;
    private static bool _multiSampling;

    public static void Main() { }

    // Must be called before the first SetMode or Frame: it asks FNA3D for a multisampled back buffer.
    [JSExport]
    public static void SetMultiSampling(bool enabled) => _multiSampling = enabled;

    [JSExport]
    public static void SetMode(string mode)
    {
        _game ??= new SampleGame(_multiSampling);
        _game.Mode = mode;
    }

    [JSExport]
    public static void Frame()
    {
        _game ??= new SampleGame(_multiSampling);
        _game.RunOneFrame();
    }

    // Tests: what creating a SkiaRenderTarget2D with this SKColorType does.
    [JSExport]
    public static string TryColorType(int colorType)
    {
        try
        {
            using var target = new SkiaRenderTarget2D(_game!.GraphicsDevice, 16, 16, (SKColorType)colorType);
            return "ok";
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ": " + exception.Message;
        }
    }

    // Tests: what creating a SkiaRenderTarget2D with this multiSampleCount does.
    [JSExport]
    public static string TryMultiSample(int multiSampleCount)
    {
        try
        {
            using var target = new SkiaRenderTarget2D(_game!.GraphicsDevice, 16, 16, SKColorType.Rgba8888, multiSampleCount);
            return "ok";
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ": " + exception.Message;
        }
    }

    // Tests: the MultiSampleCount FNA3D was given for the back buffer.
    [JSExport]
    public static int BackBufferMultiSampleCount() => _game!.GraphicsDevice.PresentationParameters.MultiSampleCount;

    [JSExport]
    public static int Frames() => _game?.Frames ?? 0;
}
