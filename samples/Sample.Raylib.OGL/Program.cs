using Raylib_cs;
using SampleShared;
using SkiaGameRendering;
using SkiaSharp;

const int Width = 800;
const int Height = 600;

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

while (!Raylib.WindowShouldClose())
{
    if (direct != null && Raylib.IsKeyPressed(KeyboardKey.D))
        useDirect = !useDirect;

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);

    if (useDirect)
    {
        direct!.Begin();
        direct.Canvas.Clear(SKColors.CornflowerBlue);
        Scene.Draw(direct.Canvas, Width, Height);
        direct.End();
    }
    else
    {
        // canvas.End() composites the whole surface itself via Raylib.DrawTexture, with no manual flip
        // or draw call needed here.
        canvas.Begin();
        canvas.Canvas.Clear(SKColors.CornflowerBlue);
        Scene.Draw(canvas.Canvas, Width, Height);
        canvas.End();
    }

    Raylib.DrawText(useDirect ? "SkiaDirectCanvas (D to switch)"
        : direct != null ? "SkiaRenderTarget2D (D to switch)" : "SkiaRenderTarget2D", 10, 10, 20, Color.White);
    Raylib.DrawFPS(10, Height - 30);
    Raylib.EndDrawing();
}

direct?.Dispose();
canvas.Dispose();
SkiaRenderer.Dispose();
Raylib.CloseWindow();
