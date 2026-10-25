using System;
using System.Collections;
using SampleShared;
using SkiaGameRendering;
using SkiaSharp;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Draws the shared <see cref="Scene"/>, a 50% orange square and an opaque mid-grey square into a
/// <see cref="SkiaRenderTarget2D"/> every frame, and shows it twice over a solid blue camera
/// background three ways, one per way Unity displays a texture: through <c>Graphics.DrawTexture</c>
/// in <c>OnGUI</c> with <see cref="SkiaRenderTarget2D.PremultipliedGuiMaterial"/> (top left), on a
/// quad the camera renders (top right) and in a uGUI <c>RawImage</c> (bottom left), both with
/// <see cref="SkiaRenderTarget2D.PremultipliedMaterial"/>. Below each copy's two squares, Unity
/// draws the same two colors itself as references, the same way: with <c>GUI.DrawTexture</c>, as
/// sprites, and as <c>RawImage</c>s.
/// Created automatically on scene load, so the scene itself stays empty. <c>Scene</c> comes
/// precompiled from <c>Scene/Sample.Unity.Scene.csproj</c>, copied into <c>Assets/Plugins/Scene</c>
/// by <c>eng/build-unity-package.ps1</c>.
///
/// The bottom right area is a <see cref="SkiaDirectCanvas"/> check on Direct3D 11, Direct3D 12, Vulkan, OpenGL and OpenGL ES 3: the main camera draws a
/// yellow square, its <c>onPostRender</c> has Skia draw red, red and magenta shapes straight into the back
/// buffer, and a second camera (higher depth, clears nothing but depth) then draws a green square on top.
///
/// --smoke-test renders a few frames and quits with 0 if the texture holds Scene's red circle and
/// blue SVG drop the right way up, and in every copy Skia's squares match Unity's references on
/// screen, or 1 otherwise. Unity's own drawing is the oracle so the check holds in both color spaces:
/// On Direct3D 11, Direct3D 12, Vulkan and OpenGL it also checks the direct canvas area: Skia over what the first camera drew,
/// under what the second drew, the right way up and with red as red. It fails if Skia's premultiplied alpha is blended as straight alpha, or if a Linear project
/// displays Skia's sRGB bytes as linear values.
///
/// It also checks a second direct canvas: a camera with a <c>targetTexture</c> that gets its own yellow
/// square, with Skia drawing red and magenta shapes onto the texture from that camera's <c>onPostRender</c>
/// (the render texture path, which is drawn flipped, unlike the back buffer).
/// <c>--smoke-test --msaa</c> runs the same checks with 4x MSAA on the back buffer and on that texture,
/// on the backends whose direct canvas draws into multisampled targets (Direct3D 11 and OpenGL). On Direct3D 12 and Vulkan,
/// which refuse them, it instead expects the NotSupportedException from the target texture camera's draw and no Skia pixels.
/// On OpenGL the plain run also fails if the screen camera's direct draws stop reaching the default framebuffer (FBO 0).
/// </summary>
public sealed class SkiaSample : MonoBehaviour
{
    // Wider than tall, so the circle's cell and its vertical mirror image land on different content.
    // Five cells across: Scene's three shapes, then the translucent and grey squares.
    const int Width = 640;
    const int Height = 256;
    const int Cell = Height / 2;
    const int SquareX = 3 * Cell;
    const int GreyX = 4 * Cell;
    // Screen positions of the quad and RawImage copies; the GUI copy sits at 0, 0.
    const int QuadX = Width;
    const int UiY = Height;
    // The direct canvas area: three Cell-wide squares in a row at the bottom right.
    const int DirectX = QuadX;
    const int DirectY = UiY;
    // The target texture area: two Cells wide, one tall, drawn by a camera placed far off screen.
    const int TargetWidth = 2 * Cell;
    const int TargetOriginX = -4000;
    static readonly Color32 Yellow = new Color32(255, 255, 0, 255);
    static readonly Color32 Green = new Color32(0, 255, 0, 255);
    static readonly Color Background = Color.blue;
    static readonly Color32 Grey = new Color32(128, 128, 128, 255);
    // Translucent, and with a channel between 0 and 255, where Gamma and Linear blending disagree.
    static readonly Color32 Translucent = new Color32(255, 128, 0, 128);

    // Recreated in OnEnable, since a script reload in play mode loses them (see OnDisable).
    SkiaRenderTarget2D _target;
    SKPaint _translucent;
    SKPaint _grey;
    // Unity objects, so they survive a script reload and OnEnable can point them at a new texture.
    Material _quadMaterial;
    RawImage _rawImage;
    Texture2D _greyReference;
    Texture2D _translucentReference;
    static bool DirectCanvasSupported =>
        SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;

    SkiaDirectCanvas _direct;
    SKPaint _red;
    SKPaint _magenta;
    Camera _frontCamera;
    Texture2D _yellowTexture;
    Texture2D _greenTexture;
    bool _smokeTest;
    bool _msaa;
    // Direct3D 12 and Vulkan throw NotSupportedException (logged from the render thread) instead of drawing into a multisampled target.
    bool _expectRefusal;
    int _refusals;
    int _unexpectedFailures;
    int _frame;
    Camera _targetCamera;
    RenderTexture _targetTexture;
    Texture2D _targetYellow;

    // "--smoke-test" on the command line, or on Android, where there is none to type, in the "unity" extra:
    // adb shell am start -n <package>/com.unity3d.player.UnityPlayerGameActivity -e unity --smoke-test
    static bool HasSmokeTestArgument()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--smoke-test") >= 0)
            return true;
#if UNITY_ANDROID && !UNITY_EDITOR
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        using var intent = activity.Call<AndroidJavaObject>("getIntent");
        var extra = intent.Call<string>("getStringExtra", "unity");
        return extra != null && extra.Contains("--smoke-test");
#else
        return false;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap() => new GameObject(nameof(SkiaSample)).AddComponent<SkiaSample>();

    void Start()
    {
        _smokeTest = HasSmokeTestArgument();
        _msaa = Array.IndexOf(Environment.GetCommandLineArgs(), "--msaa") >= 0;
        // An unfocused player stops updating, and a smoke test launched from a script may never get focus.
        if (_smokeTest)
            Application.runInBackground = true;
        _expectRefusal = _msaa && (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan);
        if (_expectRefusal)
            Application.logMessageReceivedThreaded += CountRefusal;
#if !UNITY_ANDROID
        // Room for every copy; the default window can be smaller. Android keeps the device's own screen.
        Screen.SetResolution(2 * Width, 2 * Height, FullScreenMode.Windowed);
#endif
        _greyReference = SolidTexture(Grey);
        _translucentReference = SolidTexture(Translucent);

        var camera = Camera.main;
        // Layer 1 is only for the second camera's green square.
        camera.cullingMask &= ~(1 << 1);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Background;
        camera.orthographic = true;
        // A HDR camera renders into a 16-bit float texture, which SkiaDirectCanvas cannot draw into.
        camera.allowHDR = false;
        // Direct3D 12 and Vulkan cannot draw into a multisampled target, which the project's Ultra quality level would give them.
        QualitySettings.antiAliasing = _msaa ? 4 : 0;

        // Built by hand: CreatePrimitive adds a collider, which needs the Physics module.
        var quad = new GameObject("Skia quad");
        quad.AddComponent<MeshFilter>().mesh = new Mesh
        {
            vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) },
            uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
            triangles = new[] { 0, 2, 1, 2, 3, 1 },
        };
        quad.AddComponent<MeshRenderer>().material = _quadMaterial = new Material(SkiaRenderTarget2D.PremultipliedMaterial) { mainTexture = _target.Texture };
        Place(quad.transform, QuadX, 0, Width, Height, 0);
        AddSprite(_translucentReference, QuadX + SquareX);
        AddSprite(_greyReference, QuadX + GreyX);

        if (DirectCanvasSupported)
        {
            _yellowTexture = SolidTexture(Yellow);
            _greenTexture = SolidTexture(Green);
            // Skia draws red over the right half of this yellow square, so its left half must stay yellow.
            AddSpriteAt(_yellowTexture, DirectX + Cell, DirectY, Cell, Cell, 0);
            // The second camera draws on top of what the first one and Skia drew, without clearing color. URP and HDRP have no such
            // camera (a base camera clears color, an overlay camera is drawn inside its base camera's pass, before Skia), so there Skia
            // draws last and the "front" cell expects its red.
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                var front = new GameObject("Front camera");
                _frontCamera = front.AddComponent<Camera>();
                _frontCamera.CopyFrom(camera);
                _frontCamera.clearFlags = CameraClearFlags.Depth;
                _frontCamera.depth = camera.depth + 1;
                _frontCamera.cullingMask = 1 << 1;
                // Covers the right half of the cell Skia fills with red.
                AddSpriteAt(_greenTexture, DirectX + Cell / 2, DirectY, Cell / 2, Cell, 1).gameObject.layer = 1;
            }

            // Layer 2 is only for the target texture camera's yellow square.
            camera.cullingMask &= ~(1 << 2);
            _targetYellow = SolidTexture(Yellow);
            AddSpriteAt(_targetYellow, TargetOriginX + Cell, 0, Cell, Cell, 2);
            // A Linear project makes ARGB32 an sRGB texture by default, which SkiaDirectCanvas refuses on Direct3D 12 (DXGI_FORMAT_R8G8B8A8_UNORM_SRGB), so ask for a plain one there.
            var readWrite = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default;
            _targetTexture = new RenderTexture(TargetWidth, Cell, 24, RenderTextureFormat.ARGB32, readWrite) { antiAliasing = _msaa ? 4 : 1 };
            var target = new GameObject("Target camera");
            _targetCamera = target.AddComponent<Camera>();
            _targetCamera.CopyFrom(camera);
            _targetCamera.cullingMask = 1 << 2;
            _targetCamera.depth = camera.depth - 1;
            _targetCamera.orthographicSize = Cell / 2f;
            _targetCamera.transform.position = new Vector3(TargetOriginX + TargetWidth / 2f, -Cell / 2f, -10);
            _targetCamera.targetTexture = _targetTexture;
        }

        var canvas = new GameObject("Canvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _rawImage = AddRawImage(canvas, _target.Texture, SkiaRenderTarget2D.PremultipliedMaterial, 0, UiY, Width, Height);
        AddRawImage(canvas, _translucentReference, null, SquareX, UiY + Cell, Cell, Cell);
        AddRawImage(canvas, _greyReference, null, GreyX, UiY + Cell, Cell, Cell);
    }

    static RawImage AddRawImage(Canvas canvas, Texture texture, Material material, float x, float y, float width, float height)
    {
        var image = new GameObject("RawImage").AddComponent<RawImage>();
        image.transform.SetParent(canvas.transform, false);
        image.texture = texture;
        image.material = material;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return image;
    }

    static SpriteRenderer AddSpriteAt(Texture2D texture, float x, float y, float width, float height, int layer)
    {
        var sprite = new GameObject("Direct reference").AddComponent<SpriteRenderer>();
        sprite.sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        sprite.gameObject.layer = layer;
        Place(sprite.transform, x, y, width, height, 0);
        return sprite;
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
        if (_frontCamera != null)
        {
            _frontCamera.orthographicSize = camera.orthographicSize;
            _frontCamera.transform.position = camera.transform.position;
        }

        _target.Begin();
        Scene.Draw(_target.Canvas, Width, Height);
        _target.Canvas.DrawRect(SquareX, 0, Cell, Cell, _translucent);
        _target.Canvas.DrawRect(GreyX, 0, Cell, Cell, _grey);
        _target.End();

        if (_smokeTest && ++_frame == 5)
            StartCoroutine(RunSmokeTest());
    }

    void DrawDirectSrp(ScriptableRenderContext context, Camera camera) => DrawDirect(camera);

    // After the main camera has drawn and before the front camera starts.
    void DrawDirect(Camera camera)
    {
        if (_direct == null)
            return;
        if (camera == _targetCamera)
        {
            DrawTarget();
            return;
        }
        if (camera != Camera.main)
            return;
        _direct.Begin();
        var canvas = _direct.Canvas;
        // The whole cell, under the front camera's green half.
        canvas.DrawRect(DirectX, DirectY, Cell, Cell, _red);
        // Only the right half of the cell with the yellow square behind it.
        canvas.DrawRect(DirectX + Cell + Cell / 2, DirectY, Cell / 2, Cell, _red);
        // The top quarter only, so a vertical flip puts it at the bottom.
        canvas.DrawRect(DirectX + 2 * Cell, DirectY, Cell, Cell / 4, _magenta);
        _direct.End();
    }

    // The target texture camera's pass: the same shapes as the screen's, in the texture's own pixels.
    void DrawTarget()
    {
        _direct.Begin();
        var canvas = _direct.Canvas;
        // Over the blue the camera cleared to.
        canvas.DrawRect(0, 0, Cell / 2, Cell, _red);
        // Over the yellow square, which covers the right half of the texture.
        canvas.DrawRect(Cell + Cell / 2, 0, Cell / 2, Cell, _red);
        // The top eighth only, so a missing flip puts it at the bottom.
        canvas.DrawRect(Cell / 2, 0, Cell / 2, Cell / 4, _magenta);
        _direct.End();
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
            return;
        Graphics.DrawTexture(new Rect(0, 0, Width, Height), _target.Texture, SkiaRenderTarget2D.PremultipliedGuiMaterial);
        GUI.DrawTexture(new Rect(SquareX, Cell, Cell, Cell), _translucentReference);
        GUI.DrawTexture(new Rect(GreyX, Cell, Cell, Cell), _greyReference);
    }

    // An ordinary sRGB texture, which is what Unity decodes to linear in a Linear project.
    static Texture2D SolidTexture(Color32 color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        texture.SetPixels32(new[] { color });
        texture.Apply();
        return texture;
    }

    void OnEnable()
    {
        _target = new SkiaRenderTarget2D(Width, Height);
        if (DirectCanvasSupported)
        {
            _direct = new SkiaDirectCanvas();
            _red = new SKPaint { Color = SKColors.Red };
            _magenta = new SKPaint { Color = SKColors.Magenta };
            // Only the built-in render pipeline raises Camera.onPostRender; URP and HDRP raise endCameraRendering.
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipelineManager.endCameraRendering += DrawDirectSrp;
            else
                Camera.onPostRender += DrawDirect;
        }
        _translucent = new SKPaint { Color = new SKColor(Translucent.r, Translucent.g, Translucent.b, Translucent.a) };
        _grey = new SKPaint { Color = new SKColor(Grey.r, Grey.g, Grey.b, Grey.a) };
        if (_quadMaterial != null)
            _quadMaterial.mainTexture = _target.Texture;
        if (_rawImage != null)
            _rawImage.texture = _target.Texture;
    }

    // Also runs before a script reload in play mode, which drops every field that isn't a Unity
    // object or serializable, so the Skia objects are released here and recreated in OnEnable.
    void OnDisable()
    {
        Camera.onPostRender -= DrawDirect;
        RenderPipelineManager.endCameraRendering -= DrawDirectSrp;
        _direct?.Dispose();
        _direct = null;
        _red?.Dispose();
        _magenta?.Dispose();
        _target?.Dispose();
        _target = null;
        _translucent?.Dispose();
        _grey?.Dispose();
    }

    void CountRefusal(string condition, string stackTrace, LogType type)
    {
        // An InvalidOperationException is the old MSAA back buffer failure (#315), not a refusal.
        if (type == LogType.Exception && condition.Contains("InvalidOperationException") && !condition.Contains("NotSupportedException"))
            System.Threading.Interlocked.Increment(ref _unexpectedFailures);
        if (type == LogType.Exception && condition.Contains("NotSupportedException") && condition.Contains("does not draw into multisampled targets"))
            System.Threading.Interlocked.Increment(ref _refusals);
    }

    // The draw counts the OpenGL backend keeps, which this assembly cannot see: -1 if the field is missing.
    static int GlDrawCount(string field)
    {
        var type = typeof(SkiaDirectCanvas).Assembly.GetType("SkiaGameRendering.OpenGlUnityBackend");
        var info = type?.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return info == null ? -1 : (int)info.GetValue(null);
    }

    void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= CountRefusal;
        Destroy(_greyReference);
        Destroy(_translucentReference);
        if (_yellowTexture != null)
            Destroy(_yellowTexture);
        if (_greenTexture != null)
            Destroy(_greenTexture);
        if (_targetYellow != null)
            Destroy(_targetYellow);
        if (_targetTexture != null)
            _targetTexture.Release();
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
            var square = ScreenPixel(x + SquareX + center, y + center);
            // On Metal in a Linear project IMGUI encodes its own output a second time, so the GUI.DrawTexture
            // references come out too light; the quad's references are what the screen should show there.
            int referenceX = name == "gui" && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal ? QuadX : x;
            var squareReference = ScreenPixel(referenceX + SquareX + center, y + Cell + center);
            var grey = ScreenPixel(x + GreyX + center, y + center);
            var greyReference = ScreenPixel(referenceX + GreyX + center, y + Cell + center);
            // The references are only an oracle if Unity drew them: blue alone would mean it did not.
            bool referencesDrawn = greyReference.r > 20 && greyReference.b < 250 && squareReference.r > 20;
            pass &= referencesDrawn && Matches(square, squareReference) && Matches(grey, greyReference);
            report += $" {name}: square={square} reference={squareReference} grey={grey} reference={greyReference}";
        }
        if (_yellowTexture != null)
        {
            // Pure colors, so a Linear project's sRGB handling cannot move them.
            var fill = ScreenPixel(DirectX + Cell / 4, DirectY + Cell / 2);
            var front = ScreenPixel(DirectX + 3 * Cell / 4, DirectY + Cell / 2);
            var behind = ScreenPixel(DirectX + Cell + Cell / 4, DirectY + Cell / 2);
            var over = ScreenPixel(DirectX + Cell + 3 * Cell / 4, DirectY + Cell / 2);
            var markTop = ScreenPixel(DirectX + 2 * Cell + Cell / 2, DirectY + Cell / 8);
            var markBottom = ScreenPixel(DirectX + 2 * Cell + Cell / 2, DirectY + Cell - Cell / 8);
            // When Skia refuses the target (MSAA on Direct3D 12 and Vulkan) it must draw nothing, leaving the camera's own pixels.
            var skiaRed = _expectRefusal ? (Color32)Background : new Color32(255, 0, 0, 255);
            var skiaMagenta = _expectRefusal ? (Color32)Background : new Color32(255, 0, 255, 255);
            pass &= Matches(fill, skiaRed)                            // channel order: red stays red
                && Matches(front, _frontCamera != null ? Green : skiaRed)  // the second camera draws over Skia
                && Matches(behind, Yellow)                            // Skia leaves what the first camera drew
                && Matches(over, _expectRefusal ? Yellow : skiaRed)   // Skia draws over it
                && Matches(markTop, skiaMagenta)                      // the right way up
                && Matches(markBottom, new Color32(0, 0, 255, 255));
            report += $" direct: fill={fill} front={front} behind={behind} over={over} markTop={markTop} markBottom={markBottom}";
        }
        if (_targetCamera != null)
        {
            var targetReadback = new Texture2D(TargetWidth, Cell, TextureFormat.RGBA32, false);
            RenderTexture.active = _targetTexture;
            targetReadback.ReadPixels(new Rect(0, 0, TargetWidth, Cell), 0, 0);
            RenderTexture.active = previous;
            Color32 TargetPixel(int x, int y) => targetReadback.GetPixel(x, Cell - 1 - y);
            var tFill = TargetPixel(Cell / 4, Cell / 2);
            var tBlue = TargetPixel(Cell / 2 + Cell / 4, Cell / 2);
            var tBehind = TargetPixel(Cell + Cell / 4, Cell / 2);
            var tOver = TargetPixel(Cell + 3 * Cell / 4, Cell / 2);
            var tMarkTop = TargetPixel(Cell / 2 + Cell / 4, Cell / 8);
            Destroy(targetReadback);
            pass &= Matches(tFill, _expectRefusal ? (Color32)Background : new Color32(255, 0, 0, 255)) // Skia draws over the cleared color
                && Matches(tBlue, new Color32(0, 0, 255, 255))      // and leaves the rest of it
                && Matches(tBehind, Yellow)                         // and what the camera drew
                && Matches(tOver, _expectRefusal ? Yellow : new Color32(255, 0, 0, 255))
                && Matches(tMarkTop, _expectRefusal ? (Color32)Background : new Color32(255, 0, 255, 255)); // the right way up
            report += $" target: fill={tFill} blue={tBlue} behind={tBehind} over={tOver} markTop={tMarkTop}";
        }
        if (_expectRefusal)
        {
            // The target texture's draw and the MSAA back buffer's both throw NotSupportedException each frame; and neither may fail with an InvalidOperationException.
            pass &= _refusals >= 1 && _unexpectedFailures == 0;
            report += $" refusals={_refusals} unexpectedFailures={_unexpectedFailures}";
        }
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore)
        {
            // The screen camera's pass must reach the window's default framebuffer (FBO 0), the target camera's a bound FBO.
            int defaultDraws = GlDrawCount("DefaultFramebufferDraws"), attachmentDraws = GlDrawCount("AttachmentDraws");
            // With MSAA Unity renders the screen camera into an FBO of its own, so only the plain run has to reach FBO 0.
            pass &= (_msaa || defaultDraws > 0) && attachmentDraws > 0;
            report += $" gl: fbo0Draws={defaultDraws} attachmentDraws={attachmentDraws}";
        }
        // Unity has no multisampled targets: the flag is false and a non-zero count throws on every graphics API, never falling back to 0.
        bool multiSampleRefused = !SkiaRenderer.SupportsMultiSampledTargets;
        foreach (var count in new[] { 2, 4, 8, 3, -1 })
        {
            try { new SkiaRenderTarget2D(16, 16, multiSampleCount: count).Dispose(); multiSampleRefused = false; }
            catch (NotSupportedException) { }
        }
        pass &= multiSampleRefused;
        report += $" multiSampleRefused={multiSampleRefused}";
        Destroy(readback);
        Destroy(screen);

        Debug.Log($"SMOKE TEST {(pass ? "PASSED" : "FAILED")} ({QualitySettings.activeColorSpace}): {report}");
        Application.Quit(pass ? 0 : 1);
    }

    static bool Matches(Color32 a, Color32 b) =>
        Math.Abs(a.r - b.r) <= 4 && Math.Abs(a.g - b.g) <= 4 && Math.Abs(a.b - b.b) <= 4;
}
