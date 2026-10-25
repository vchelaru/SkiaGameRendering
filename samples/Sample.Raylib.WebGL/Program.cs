using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Runtime.Versioning;
using Microsoft.JSInterop;
using Raylib_cs;
using Sample.Shared;
using SkiaGameRendering;
using SkiaSharp;

namespace Sample.RaylibWebGL;

// raylib has no main loop of its own in the browser: the page calls Frame from requestAnimationFrame (wwwroot/js/game-loop.js).
[SupportedOSPlatform("browser")]
internal static class Program
{
    private static async Task Main(string[] args)
    {
        Game.Start();
        await WebAssemblyHostBuilder.CreateDefault(args).Build().RunAsync();
    }
}

// Draws the shared Scene like every other sample, in one of two modes picked by the page's ?mode= query:
// "target" through a SkiaRenderTarget2D, "direct" with SkiaDirectCanvas, layered between raylib's own draws
// (tests/Tests.Raylib.WebGL/tests/scene.spec.js repeats this layout and its colors to probe it).
[SupportedOSPlatform("browser")]
public static class Game
{
    private const int Width = 800;
    private const int Height = 600;

    // Direct mode: raylib's green rectangle, a Skia strip over its right half, a raylib rectangle over the strip.
    private static readonly Color Green = new(30, 180, 40, 255);
    private static readonly SKColor Pink = new(210, 50, 90);
    private static readonly Color Yellow = new(240, 220, 10, 255);

    // Direct mode into a RenderTexture2D: raylib's clear, a Skia square, a raylib square over it.
    private static readonly Color RtClear = new(40, 90, 160, 255);
    private static readonly SKColor RtSkia = new(220, 40, 40);
    private static readonly Color RtAfter = new(40, 200, 80, 255);

    private static SkiaRenderTarget2D? _target;
    private static SkiaDirectCanvas? _direct;
    private static RenderTexture2D _renderTexture;
    private static bool _ready;
    private static long _frames;
    private static string _error = "";

    internal static void Start()
    {
        Raylib.InitWindow(Width, Height, "Sample: raylib + SkiaGameRendering.Raylib.WebGL");
        SkiaRenderer.Initialize();
        _target = new SkiaRenderTarget2D(Width, Height);
        _direct = new SkiaDirectCanvas();
        _renderTexture = Raylib.LoadRenderTexture(160, 120);
        _ready = true;
    }

    [JSInvokable]
    public static string? Frame(string mode)
    {
        if (!_ready)
            return null;

        try
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            // ?mode=raylib draws raylib alone, so tests can count the GL calls raylib makes on its own.
            if (mode == "direct")
                DrawDirect();
            else if (mode == "raylib")
                Raylib.DrawRectangle(40, 40, 200, 100, Color.Green);
            else
                DrawTarget();
            Raylib.DrawText(mode == "direct" ? "SkiaDirectCanvas" : mode == "raylib" ? "raylib only" : "SkiaRenderTarget2D", 10, 10, 20, Color.White);
            Raylib.EndDrawing();
            _frames++;
        }
        catch (Exception exception)
        {
            _error = exception.GetType().Name + ": " + exception.Message;
            throw;
        }

        return $"{(mode == "direct" ? "SkiaDirectCanvas" : mode == "raylib" ? "raylib only" : "SkiaRenderTarget2D")} frames {_frames} errors {(_error.Length == 0 ? "none" : _error)}";
    }

    // Tests: what creating a SkiaRenderTarget2D with this SKColorType does.
    [JSInvokable]
    public static string TryColorType(int colorType)
    {
        try
        {
            using var target = new SkiaRenderTarget2D(16, 16, (SKColorType)colorType);
            return "ok";
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ": " + exception.Message;
        }
    }

    private static void DrawTarget()
    {
        _target!.Begin();
        Scene.Draw(_target.Canvas, Width, Height);
        _target.End();
    }

    private static void DrawDirect()
    {
        // Into a render texture first: raylib clears it, Skia draws a square, raylib draws one over it.
        Raylib.BeginTextureMode(_renderTexture);
        Raylib.ClearBackground(RtClear);
        _direct!.Begin();
        using (var paint = new SKPaint { Color = RtSkia })
            _direct.Canvas.DrawRect(20, 20, 60, 60, paint);
        _direct.End();
        Raylib.DrawRectangle(50, 50, 40, 40, RtAfter);
        Raylib.EndTextureMode();

        Raylib.DrawRectangle(620, 20, 160, 120, Green);

        _direct.Begin();
        Scene.Draw(_direct.Canvas, Width, Height);
        using (var paint = new SKPaint { Color = Pink })
            _direct.Canvas.DrawRect(700, 20, 80, 120, paint);
        _direct.End();

        Raylib.DrawRectangle(740, 20, 40, 60, Yellow);

        // A render texture is upside down in raylib; the negative height flips it back.
        Raylib.DrawTextureRec(_renderTexture.Texture, new Rectangle(0, 0, 160, -120), new System.Numerics.Vector2(620, 160), Color.White);
    }
}
