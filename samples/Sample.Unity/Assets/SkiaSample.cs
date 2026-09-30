using System;
using System.Collections;
using Sample.Shared;
using SkiaGameRendering.Unity;
using SkiaSharp;
using UnityEngine;

/// <summary>
/// Draws the shared <see cref="Scene"/>, plus a 50% white square, into a
/// <see cref="SkiaUnityRenderTarget"/> every frame and shows it over a solid blue camera background
/// with <see cref="SkiaUnityRenderTarget.PremultipliedMaterial"/>.
/// Created automatically on scene load, so the scene itself stays empty. <c>Scene</c> comes
/// precompiled from <c>Scene/Sample.Unity.Scene.csproj</c>, copied into <c>Assets/Plugins/Scene</c>
/// by <c>eng/build-unity-package.ps1</c>.
///
/// --smoke-test renders a few frames and quits with 0 if the texture holds Scene's red circle and
/// blue SVG drop the right way up, and the square composites over the blue background correctly
/// (which fails if Skia's premultiplied alpha is blended as straight alpha), or 1 otherwise.
/// </summary>
public sealed class SkiaSample : MonoBehaviour
{
    // Wider than tall, so the circle's cell and its vertical mirror image land on different content.
    const int Width = 512;
    const int Height = 256;
    const int Cell = Height / 2;
    static readonly Color Background = Color.blue;

    SkiaUnityRenderTarget _target;
    SKPaint _translucent;
    bool _smokeTest;
    int _frame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => new GameObject(nameof(SkiaSample)).AddComponent<SkiaSample>();

    void Start()
    {
        _smokeTest = Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0;
        _target = new SkiaUnityRenderTarget(Width, Height);
        _translucent = new SKPaint { Color = SKColors.White.WithAlpha(128) };

        var camera = Camera.main;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Background;
    }

    void Update()
    {
        _target.Begin();
        Scene.Draw(_target.Canvas, Width, Height);
        _target.Canvas.DrawRect(2 * Cell, 0, Cell, Cell, _translucent);
        _target.End();

        if (_smokeTest && ++_frame == 5)
            StartCoroutine(RunSmokeTest());
    }

    void OnGUI()
    {
        if (Event.current.type == EventType.Repaint)
            Graphics.DrawTexture(new Rect(0, 0, Width, Height), _target.Texture, SkiaUnityRenderTarget.PremultipliedMaterial);
    }

    void OnDestroy()
    {
        _target?.Dispose();
        _translucent?.Dispose();
    }

    IEnumerator RunSmokeTest()
    {
        // The screen only holds the composited frame once everything, OnGUI included, has drawn.
        yield return new WaitForEndOfFrame();
        var previous = RenderTexture.active;
        RenderTexture.active = null;
        var screen = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        screen.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);

        var readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        RenderTexture.active = _target.Texture;
        readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        RenderTexture.active = previous;

        // Texture2D rows count up from the bottom; Skia's and the GUI's count down from the top.
        Color32 SkiaPixel(int x, int y) => readback.GetPixel(x, Height - 1 - y);
        Color32 ScreenPixel(int x, int y) => screen.GetPixel(x, screen.height - 1 - y);
        int center = Cell / 2;
        var circle = SkiaPixel(center, center);
        var drop = SkiaPixel(Cell + center, center);
        var mirrored = SkiaPixel(center, Height - 1 - center);
        var square = ScreenPixel(2 * Cell + center, center);
        Destroy(readback);
        Destroy(screen);

        // 50% white over pure blue, in the project's Gamma color space.
        var expected = Color32.Lerp(Background, Color.white, 0.5f);
        bool squareOk = Math.Abs(square.r - expected.r) < 12 && Math.Abs(square.g - expected.g) < 12
            && Math.Abs(square.b - expected.b) < 12;
        bool pass = circle.r > 200 && circle.g < 50 && circle.b < 50
            && drop.r < 100 && drop.b > 150
            && mirrored.a < 10
            && squareOk;
        Debug.Log($"SMOKE TEST {(pass ? "PASSED" : "FAILED")}: circle={circle} drop={drop} mirrored={mirrored} " +
            $"square={square} expected={expected}");
        Application.Quit(pass ? 0 : 1);
    }
}
