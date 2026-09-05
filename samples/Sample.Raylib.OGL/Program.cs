using Raylib_cs;
using SkiaGameRendering.Raylib.OGL;
using SkiaSharp;

const int Width = 800;
const int Height = 600;

Raylib.InitWindow(Width, Height, "Sample: raylib + SkiaGameRendering.Raylib.OGL");
Raylib.SetTargetFPS(60);

// SkiaRaylibRenderTarget2D auto-initializes SkiaRaylibRenderer.EnsureInitialized() on first use,
// but initializing explicitly here fails fast (with a clear stack) if the shared GL context can't
// be created, rather than surfacing the failure lazily on first draw.
SkiaRaylibRenderer.Initialize();

// Not a `using` declaration: it must be disposed before SkiaRaylibRenderer.Dispose() tears down
// the GL context it depends on, which top-level statements' end-of-scope disposal order can't
// guarantee relative to the explicit SkiaRaylibRenderer.Dispose() call below.
var canvas = new SkiaRaylibRenderTarget2D(Width, Height);
using var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

float angle = 0f;

while (!Raylib.WindowShouldClose())
{
    angle += Raylib.GetFrameTime() * 90f;

    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);

    // canvas.End() composites the whole surface itself via Raylib.DrawTexture - no manual flip or
    // draw call needed here. SkiaRaylibRenderTarget2D bakes the bottom-left-origin flip into the
    // Skia surface itself (see SkiaRaylibContext.CreateSurface), so it samples correctly.
    canvas.Begin();
    canvas.Canvas.Clear(SKColors.CornflowerBlue);
    canvas.Canvas.Save();
    canvas.Canvas.RotateDegrees(angle, Width / 2f, Height / 2f);
    canvas.Canvas.DrawRect(SKRect.Create(Width / 2f - 100, Height / 2f - 100, 200, 200), paint);
    canvas.Canvas.Restore();
    canvas.End();

    Raylib.DrawText("Skia-rendered square (raylib backend)", 10, 10, 20, Color.White);
    Raylib.DrawFPS(10, Height - 30);
    Raylib.EndDrawing();
}

canvas.Dispose();
SkiaRaylibRenderer.Dispose();
Raylib.CloseWindow();
