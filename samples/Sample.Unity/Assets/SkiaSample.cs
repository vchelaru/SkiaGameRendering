using System;
using System.Collections;
using Sample.Shared;
using SkiaGameRendering.Unity;
using SkiaSharp;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the shared <see cref="Scene"/>, a 50% orange square and an opaque mid-grey square into a
/// <see cref="SkiaUnityRenderTarget"/> every frame, and shows it twice over a solid blue camera
/// background three ways, one per way Unity displays a texture: through <c>Graphics.DrawTexture</c>
/// in <c>OnGUI</c> with <see cref="SkiaUnityRenderTarget.PremultipliedGuiMaterial"/> (top left), on a
/// quad the camera renders (top right) and in a uGUI <c>RawImage</c> (bottom left), both with
/// <see cref="SkiaUnityRenderTarget.PremultipliedMaterial"/>. Below each copy's two squares, Unity
/// draws the same two colors itself as references, the same way: with <c>GUI.DrawTexture</c>, as
/// sprites, and as <c>RawImage</c>s.
/// Created automatically on scene load, so the scene itself stays empty. <c>Scene</c> comes
/// precompiled from <c>Scene/Sample.Unity.Scene.csproj</c>, copied into <c>Assets/Plugins/Scene</c>
/// by <c>eng/build-unity-package.ps1</c>.
///
/// --smoke-test renders a few frames and quits with 0 if the texture holds Scene's red circle and
/// blue SVG drop the right way up, and in every copy Skia's squares match Unity's references on
/// screen, or 1 otherwise. Unity's own drawing is the oracle so the check holds in both color spaces:
/// it fails if Skia's premultiplied alpha is blended as straight alpha, or if a Linear project
/// displays Skia's sRGB bytes as linear values.
/// </summary>
public sealed class SkiaSample : MonoBehaviour
{
    // Wider than tall, so the circle's cell and its vertical mirror image land on different content.
    const int Width = 512;
    const int Height = 256;
    const int Cell = Height / 2;
    // Screen positions of the quad and RawImage copies; the GUI copy sits at 0, 0.
    const int QuadX = Width;
    const int UiY = Height;
    static readonly Color Background = Color.blue;
    static readonly Color32 Grey = new Color32(128, 128, 128, 255);
    // Translucent, and with a channel between 0 and 255, where Gamma and Linear blending disagree.
    static readonly Color32 Translucent = new Color32(255, 128, 0, 128);

    SkiaUnityRenderTarget _target;
    SKPaint _translucent;
    SKPaint _grey;
    Texture2D _greyReference;
    Texture2D _translucentReference;
    bool _smokeTest;
    int _frame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => new GameObject(nameof(SkiaSample)).AddComponent<SkiaSample>();

    void Start()
    {
        _smokeTest = Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0;
        // An unfocused player stops updating, and a smoke test launched from a script may never get focus.
        if (_smokeTest)
            Application.runInBackground = true;
        // Room for every copy; the default window can be smaller.
        Screen.SetResolution(2 * Width, 2 * Height, FullScreenMode.Windowed);
        _target = new SkiaUnityRenderTarget(Width, Height);
        _translucent = new SKPaint { Color = new SKColor(Translucent.r, Translucent.g, Translucent.b, Translucent.a) };
        _grey = new SKPaint { Color = new SKColor(Grey.r, Grey.g, Grey.b, Grey.a) };
        _greyReference = SolidTexture(Grey);
        _translucentReference = SolidTexture(Translucent);

        var camera = Camera.main;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Background;
        camera.orthographic = true;

        // Built by hand: CreatePrimitive adds a collider, which needs the Physics module.
        var quad = new GameObject("Skia quad");
        quad.AddComponent<MeshFilter>().mesh = new Mesh
        {
            vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        quad.AddComponent<MeshRenderer>().material = new Material(SkiaUnityRenderTarget.PremultipliedMaterial) { mainTexture = _target.Texture };
        Place(quad.transform, QuadX, 0, Width, Height, 0);
        AddSprite(_translucentReference, QuadX + 2 * Cell);
        AddSprite(_greyReference, QuadX + 3 * Cell);

        var canvas = new GameObject("Canvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        AddRawImage(canvas, _target.Texture, SkiaUnityRenderTarget.PremultipliedMaterial, 0, UiY, Width, Height);
        AddRawImage(canvas, _translucentReference, null, 2 * Cell, UiY + Cell, Cell, Cell);
        AddRawImage(canvas, _greyReference, null, 3 * Cell, UiY + Cell, Cell, Cell);
    }

    static void AddRawImage(Canvas canvas, Texture texture, Material material, float x, float y, float width, float height)
    {
        var image = new GameObject("RawImage").AddComponent<RawImage>();
        image.transform.SetParent(canvas.transform, false);
        image.texture = texture;
        image.material = material;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void AddSprite(Texture2D texture, int x)
    {
        var sprite = new GameObject("Reference").AddComponent<SpriteRenderer>();
        sprite.sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        // In front of the quad, whose texture is transparent here.
        Place(sprite.transform, x, Cell, Cell, Cell, -1);
    }

    // Centers a unit-sized object on a screen-space rectangle.
    static void Place(Transform transform, float x, float y, float width, float height, float z)
    {
        transform.position = new Vector3(x + width / 2, -(y + height / 2), z);
        transform.localScale = new Vector3(width, height, 1);
    }

    void Update()
    {
        // One world unit per screen pixel, with world y = -(screen y from the top). Every frame,
        // since the window can still change size after Start.
        var camera = Camera.main;
        camera.orthographicSize = Screen.height / 2f;
        camera.transform.position = new Vector3(Screen.width / 2f, -Screen.height / 2f, -10);

        _target.Begin();
        Scene.Draw(_target.Canvas, Width, Height);
        _target.Canvas.DrawRect(2 * Cell, 0, Cell, Cell, _translucent);
        _target.Canvas.DrawRect(3 * Cell, 0, Cell, Cell, _grey);
        _target.End();

        if (_smokeTest && ++_frame == 5)
            StartCoroutine(RunSmokeTest());
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
            return;
        Graphics.DrawTexture(new Rect(0, 0, Width, Height), _target.Texture, SkiaUnityRenderTarget.PremultipliedGuiMaterial);
        GUI.DrawTexture(new Rect(2 * Cell, Cell, Cell, Cell), _translucentReference);
        GUI.DrawTexture(new Rect(3 * Cell, Cell, Cell, Cell), _greyReference);
    }

    // An ordinary sRGB texture, which is what Unity decodes to linear in a Linear project.
    static Texture2D SolidTexture(Color32 color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        texture.SetPixels32(new[] { color });
        texture.Apply();
        return texture;
    }

    void OnDestroy()
    {
        _target?.Dispose();
        _translucent?.Dispose();
        _grey?.Dispose();
        Destroy(_greyReference);
        Destroy(_translucentReference);
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
        bool pass = circle.r > 200 && circle.g < 50 && circle.b < 50
            && drop.r < 100 && drop.b > 150
            && mirrored.a < 10
            // GetPixel clamps, so a window too small for every copy would read the wrong pixels.
            && screen.width >= QuadX + Width && screen.height >= UiY + Height;
        string report = $"screen={screen.width}x{screen.height} circle={circle} drop={drop} mirrored={mirrored}";

        foreach (var (name, x, y) in new[] { ("gui", 0, 0), ("quad", QuadX, 0), ("rawimage", 0, UiY) })
        {
            var square = ScreenPixel(x + 2 * Cell + center, y + center);
            var squareReference = ScreenPixel(x + 2 * Cell + center, y + Cell + center);
            var grey = ScreenPixel(x + 3 * Cell + center, y + center);
            var greyReference = ScreenPixel(x + 3 * Cell + center, y + Cell + center);
            // The references are only an oracle if Unity drew them: blue alone would mean it did not.
            bool referencesDrawn = greyReference.r > 20 && greyReference.b < 250 && squareReference.r > 20;
            pass &= referencesDrawn && Matches(square, squareReference) && Matches(grey, greyReference);
            report += $" {name}: square={square} reference={squareReference} grey={grey} reference={greyReference}";
        }
        Destroy(readback);
        Destroy(screen);

        Debug.Log($"SMOKE TEST {(pass ? "PASSED" : "FAILED")} ({QualitySettings.activeColorSpace}): {report}");
        Application.Quit(pass ? 0 : 1);
    }

    static bool Matches(Color32 a, Color32 b) =>
        Math.Abs(a.r - b.r) <= 4 && Math.Abs(a.g - b.g) <= 4 && Math.Abs(a.b - b.b) <= 4;
}
