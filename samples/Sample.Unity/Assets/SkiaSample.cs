using System;
using Sample.Shared;
using SkiaGameRendering.Unity;
using SkiaSharp;
using UnityEngine;

/// <summary>
/// Draws the shared <see cref="Scene"/> into a <see cref="SkiaUnityRenderTarget"/> every frame and
/// shows it with <c>GUI.DrawTexture</c>. Created automatically on scene load, so the scene itself
/// stays empty. <c>Scene</c> comes precompiled from <c>Scene/Sample.Unity.Scene.csproj</c>, copied
/// into <c>Assets/Plugins/Scene</c> by <c>eng/build-unity-package.ps1</c>.
///
/// --smoke-test renders a few frames, reads the texture back, and quits with 0 if Scene's first two
/// cells hold the red circle and the blue SVG drop and the circle's vertical mirror is still the
/// black clear color (so the image isn't upside down), or 1 otherwise.
/// </summary>
public sealed class SkiaSample : MonoBehaviour
{
    // Wider than tall, so the circle's cell and its vertical mirror image land on different content.
    const int Width = 512;
    const int Height = 256;
    const int Cell = Height / 2;

    SkiaUnityRenderTarget _target;
    bool _smokeTest;
    int _frame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => new GameObject(nameof(SkiaSample)).AddComponent<SkiaSample>();

    void Start()
    {
        _smokeTest = Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0;
        _target = new SkiaUnityRenderTarget(Width, Height);
    }

    void Update()
    {
        _target.Begin();
        _target.Canvas.Clear(SKColors.Black);
        Scene.Draw(_target.Canvas, Width, Height);
        _target.End();

        if (_smokeTest && ++_frame == 5)
            RunSmokeTest();
    }

    void OnGUI() => GUI.DrawTexture(new Rect(0, 0, Width, Height), _target.Texture);

    void OnDestroy() => _target?.Dispose();

    void RunSmokeTest()
    {
        var readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = _target.Texture;
        readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        RenderTexture.active = previous;

        // Texture2D rows count up from the bottom, Skia's count down from the top.
        Color32 SkiaPixel(int x, int y) => readback.GetPixel(x, Height - 1 - y);
        int center = Cell / 2;
        var circle = SkiaPixel(center, center);
        var drop = SkiaPixel(Cell + center, center);
        var mirrored = SkiaPixel(center, Height - 1 - center);
        Destroy(readback);

        bool pass = circle.r > 200 && circle.g < 50 && circle.b < 50
            && drop.r < 100 && drop.b > 150
            && mirrored.r < 50 && mirrored.g < 50 && mirrored.b < 50;
        Debug.Log($"SMOKE TEST {(pass ? "PASSED" : "FAILED")}: circle={circle} drop={drop} mirrored={mirrored}");
        Application.Quit(pass ? 0 : 1);
    }
}
