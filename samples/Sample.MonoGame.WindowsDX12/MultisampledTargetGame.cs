using Microsoft.Xna.Framework;
using SkiaGameRendering;

namespace Sample;

/// <summary>
/// --render-target-smoke-test-msaa: on a live MonoGame native device, a SkiaRenderTarget2D with multiSampleCount 4
/// must throw NotSupportedException naming the backend, and a plain one built right after must still work.
/// </summary>
sealed class MultisampledTargetGame : Game
{
    public int ExitCode { get; private set; } = 1;

    public MultisampledTargetGame()
    {
        new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 320, PreferredBackBufferHeight = 240 };
    }

    protected override void Draw(GameTime gameTime)
    {
        string error = null;
        try
        {
            try
            {
                using var target = new SkiaRenderTarget2D(GraphicsDevice, 64, 64, multiSampleCount: 4);
                error = "multiSampleCount 4 was accepted";
            }
            catch (NotSupportedException exception)
            {
                Console.WriteLine("Refused as expected: " + exception.Message);
                if (!exception.Message.Contains("multisampled"))
                    error = "the refusal does not say why: " + exception.Message;
            }

            if (error == null)
            {
                using var plain = new SkiaRenderTarget2D(GraphicsDevice, 64, 64);
                plain.Begin();
                plain.Canvas.Clear(SkiaSharp.SKColors.Red);
                plain.End();
            }
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }

        Console.WriteLine(error == null ? "Render target MSAA smoke test passed" : "Render target MSAA smoke test FAILED: " + error);
        ExitCode = error == null ? 0 : 1;
        Exit();
    }
}
