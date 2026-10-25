using Raylib_cs;
using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using RaylibApi = Raylib_cs.Raylib;

namespace Tests.Raylib.OGL;

/// <summary>
/// A game that closes its window before disposing Skia must not crash or throw: with the window gone
/// there is no context to free Skia's GL objects on, so they go with the process.
/// </summary>
[Collection(RaylibWindowCollection.Name)]
public sealed class RaylibShutdownTests
{
    [NotMacOsFact]
    public void DisposingAfterCloseWindow_DoesNotThrow()
    {
        VendoredOpenGl.PreloadIfPresent();
        RaylibApi.SetConfigFlags(ConfigFlags.HiddenWindow);
        RaylibApi.InitWindow(64, 64, "Tests.Raylib.OGL");
        SkiaRenderTarget2D canvas;
        try
        {
            SkiaRenderer.Initialize();
            canvas = new SkiaRenderTarget2D(64, 64);
            canvas.Begin();
            canvas.Canvas.Clear(SkiaSharp.SKColors.Red);
            canvas.EndWithoutDrawing();
        }
        finally
        {
            RaylibApi.CloseWindow();
        }

        canvas.Dispose();
        SkiaRenderer.Dispose();
    }
}
