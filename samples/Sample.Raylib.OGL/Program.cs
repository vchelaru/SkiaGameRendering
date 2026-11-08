using Raylib_cs;
using SampleShared;
using SkiaGameRendering;
using SkiaSharp;

const int Width = Scene.Width;
const int Height = Scene.Height;

// --screenshot <path> hides the window, saves frame 10 (Scene on its background, no text) to a PNG and exits.
var screenshotAt = Array.IndexOf(args, "--screenshot");
var screenshotPath = screenshotAt >= 0 && screenshotAt + 1 < args.Length ? args[screenshotAt + 1] : null;
var background = new Color((int)Scene.Background.Red, (int)Scene.Background.Green, (int)Scene.Background.Blue, 255);
if (screenshotPath != null)
    Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
Raylib.InitWindow(Width, Height, "Sample: raylib + SkiaGameRendering.Raylib.OGL");
Raylib.SetTargetFPS(60);

// SkiaRenderTarget2D auto-initializes SkiaRenderer.EnsureInitialized() on first use,
// but initializing explicitly here fails fast (with a clear stack) if the shared GL context can't
// be created, rather than surfacing the failure lazily on first draw.
SkiaRenderer.Initialize();

// Not a `using` declaration: it must be disposed before SkiaRenderer.Dispose() tears down
// the GL context it depends on, which top-level statements' end-of-scope disposal order can't
// guarantee relative to the explicit SkiaRenderer.Dispose() call below.
var canvas = new SkiaRenderTarget2D(Width, Height);

// D toggles SkiaDirectCanvas, which draws straight into the back buffer with no texture.
var direct = SkiaRenderer.SupportsDirectCanvas ? new SkiaDirectCanvas() : null;
var useDirect = false;

var frames = 0;
while (!Raylib.WindowShouldClose())
{
    if (direct != null && Raylib.IsKeyPressed(KeyboardKey.D))
        useDirect = !useDirect;

    Raylib.BeginDrawing();
    Raylib.ClearBackground(background);

    if (useDirect)
    {
        direct!.Begin();
        Scene.Draw(direct.Canvas);
        direct.End();
    }
    else
    {
        // canvas.End() composites the whole surface itself via Raylib.DrawTexture, with no manual flip
        // or draw call needed here.
        canvas.Begin();
        Scene.Draw(canvas.Canvas);
        canvas.End();
    }

    if (screenshotPath == null)
    {
        Raylib.DrawText(useDirect ? "SkiaDirectCanvas (D to switch)"
            : direct != null ? "SkiaRenderTarget2D (D to switch)" : "SkiaRenderTarget2D", 10, 10, 20, Color.White);
        Raylib.DrawFPS(10, Height - 30);
    }
    Raylib.EndDrawing();
    if (screenshotPath != null && ++frames == 10)
    {
        Raylib.ExportImage(Raylib.LoadImageFromScreen(), screenshotPath);
        break;
    }
}

direct?.Dispose();
canvas.Dispose();
SkiaRenderer.Dispose();
Raylib.CloseWindow();
