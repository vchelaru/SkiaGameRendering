using System;
using SkiaGameRendering.Unity;
using SkiaSharp;
using UnityEngine;

/// <summary>
/// Draws a red circle into a <see cref="SkiaUnityRenderTarget"/> every frame and shows it with
/// <c>GUI.DrawTexture</c>. Created automatically on scene load, so the scene itself stays empty.
///
/// --smoke-test renders a few frames, reads the texture back, checks the circle is where Skia put
/// it (which also checks the image isn't upside down), and quits with 0 on success or 1 on failure.
/// </summary>
public sealed class SkiaSample : MonoBehaviour
{
    // Wider than tall, so the circle's cell and its vertical mirror image land on different content.
    const int Width = 512;
    const int Height = 256;
    const float Cell = Height / 2f;

    SkiaUnityRenderTarget _target;
    SKPaint _paint;
    bool _smokeTest;
    int _frame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => new GameObject(nameof(SkiaSample)).AddComponent<SkiaSample>();

    void Start()
    {
        _smokeTest = Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0;
        _target = new SkiaUnityRenderTarget(Width, Height);
        _paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
    }

    void Update()
    {
        _target.Begin();
        _target.Canvas.Clear(SKColors.CornflowerBlue);
        _target.Canvas.DrawCircle(Cell / 2f, Cell / 2f, Cell * 0.4f, _paint);
        _target.End();

        if (_smokeTest && ++_frame == 5)
            RunSmokeTest();
    }

    void OnGUI() => GUI.DrawTexture(new Rect(0, 0, Width, Height), _target.Texture);

    void OnDestroy()
    {
        _target?.Dispose();
        _paint?.Dispose();
    }

    void RunSmokeTest()
    {
        var readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = _target.Texture;
        readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        RenderTexture.active = previous;

        // Texture2D rows count up from the bottom, Skia's count down from the top.
        int x = (int)(Cell / 2f);
        var circle = readback.GetPixel(x, Height - 1 - x);
        var mirrored = readback.GetPixel(x, x);
        Destroy(readback);

        bool pass = circle.r > 0.9f && circle.g < 0.1f && circle.b < 0.1f
            && !(mirrored.r > 0.9f && mirrored.g < 0.1f && mirrored.b < 0.1f);
        Debug.Log($"SMOKE TEST {(pass ? "PASSED" : "FAILED")}: circle={circle} mirrored={mirrored}");
        Application.Quit(pass ? 0 : 1);
    }
}
