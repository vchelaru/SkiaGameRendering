using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.Versioning;
using Sample.Kni.WebGL.Gum;
using SkiaGameRendering;
using SkiaGameRendering.Kni.WebGL;
using SkiaGameRendering.Kni.WebGL.Components;

namespace Sample.Kni.WebGL;

// Parameterless on purpose: nothing here comes through the constructor. This proves the pattern a
// host that constructs Game itself (e.g. via Activator.CreateInstance, with no constructor hook -
// an in-browser fiddle) needs: the page's code-behind calls SkiaRenderer.AttachHost once, and this
// class only ever touches SkiaRenderer's shared, platform-agnostic surface (IsReady,
// Initialize(GraphicsDevice), CurrentBackend) - no WebGL-specific type appears anywhere below.
[SupportedOSPlatform("browser")]
internal sealed class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch? _batch;
    private Texture2D? _pixel;
    private RenderTarget2D? _uiTarget;
    private BasicEffect? _shader;
    private SkiaGumRenderable? _gum;
    private Matrix _screenTransform = Matrix.Identity;
    private float _pointerX;
    private float _pointerY;
    private bool _pointerDown;
    private float _wheelDelta;
    private string _textInput = string.Empty;
    private string _pointerType = "none";
    private int _backendRecreateCount;
    private readonly VertexPositionColorTexture[] _shaderVertices =
    {
        new(new Vector3(930, 40, 0), Color.White, new Vector2(0, 0)),
        new(new Vector3(1230, 40, 0), Color.White, new Vector2(1, 0)),
        new(new Vector3(930, 228, 0), Color.White, new Vector2(0, 1)),
        new(new Vector3(1230, 228, 0), Color.White, new Vector2(1, 1)),
    };

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        IsMouseVisible = true;
    }

    public double DevicePixelRatio { get; set; } = 1;

    // Only meaningful once SkiaRenderer.IsInitialized - null beforehand.
    private SkiaWebGlBackend? Backend => SkiaRenderer.CurrentBackend as SkiaWebGlBackend;
    private SkiaGameWebGlHost? Host => Backend?.Host;

    protected override void Initialize()
    {
        _gum = new SkiaGumRenderable(GraphicsDevice, 480, 300);
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _uiTarget = new RenderTarget2D(GraphicsDevice, 480, 300, false, SurfaceFormat.Color, DepthFormat.None);
        _shader = new BasicEffect(GraphicsDevice)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
            World = Matrix.Identity,
            View = Matrix.Identity,
            Projection = Matrix.CreateOrthographicOffCenter(0, 1280, 720, 0, 0, 1),
        };
    }

    protected override void Update(GameTime gameTime)
    {
        if (SkiaRenderer.IsInitialized)
        {
            _gum!.Dpi = Math.Clamp((float)DevicePixelRatio, 1, 3);
            var directTransform = Matrix.CreateScale(0.82f) * Matrix.CreateTranslation(40, 100, 0);
            _gum.SetPresentationTransform(directTransform);
            _gum.HandlePointer(_pointerX, _pointerY, _pointerDown, (int)_wheelDelta);
            if (_textInput.Length > 0)
                _gum.HandleText(_textInput);
            _wheelDelta = 0;
            _textInput = string.Empty;

            if (_gum.ConsumeBackendRecreateRequest())
                RecreateBackend();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
            SkiaRenderer.Initialize(GraphicsDevice);

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(new Color(19, 22, 27));

        BeginScreenBatch(BlendState.AlphaBlend);
        _batch!.Draw(_pixel!, new Rectangle(0, 0, 1280, 720), new Color(25, 29, 35));
        _batch.Draw(_pixel!, new Rectangle(24, 84, 520, 10), new Color(54, 122, 178));
        _batch.End();

        if (SkiaRenderer.IsInitialized)
        {
            var host = Host;
            if (host?.IsContextLost != true)
                _gum!.Draw();

            if (_gum!.Texture != null)
            {
                BeginScreenBatch(BlendState.AlphaBlend, SamplerState.LinearClamp);
                _batch.Draw(_gum.Texture, new Rectangle(40, 100, 394, 246), Color.White);
                _batch.End();

                GraphicsDevice.SetRenderTarget(_uiTarget);
                GraphicsDevice.Clear(Color.Transparent);
                _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
                _batch.Draw(_gum.Texture, new Rectangle(0, 0, 480, 300), Color.White);
                _batch.Draw(_pixel!, new Rectangle(350, 245, 120, 36), new Color(240, 190, 64, 210));
                _batch.End();
                GraphicsDevice.SetRenderTarget(null);

                BeginScreenBatch(BlendState.AlphaBlend, SamplerState.LinearClamp);
                _batch.Draw(_uiTarget!, new Vector2(770, 350), null, Color.White, -0.08f,
                    new Vector2(240, 150), 0.72f, SpriteEffects.None, 0);
                _batch.Draw(_pixel!, new Rectangle(300, 210, 250, 12), new Color(238, 84, 74, 220));
                _batch.End();

                DrawWithShader(_gum.Texture);
            }
        }

        base.Draw(gameTime);
    }

    public void SetBrowserState(
        int physicalWidth,
        int physicalHeight,
        float pointerX,
        float pointerY,
        bool pointerDown,
        float wheelDelta,
        string textInput,
        string pointerType,
        bool diagnosticTexImage)
    {
        if (physicalWidth > 0 && physicalHeight > 0)
        {
            GraphicsDevice.Viewport = new Viewport(0, 0, physicalWidth, physicalHeight);
            _screenTransform = Matrix.CreateScale(physicalWidth / 1280f, physicalHeight / 720f, 1);
        }

        _pointerX = pointerX;
        _pointerY = pointerY;
        _pointerDown = pointerDown;
        _wheelDelta += wheelDelta;
        _textInput += textInput;
        _pointerType = pointerType;
        if (Backend != null)
            Backend.Options.UploadMode = diagnosticTexImage
                ? WebGlUploadMode.DiagnosticTexImage2D
                : WebGlUploadMode.DirectCanvasTexSubImage2D;
    }

    public string GetDiagnostics()
    {
        var diagnostics = Backend?.Diagnostics;
        return $"{Host?.WebGlVersion ?? "starting"} | DPR {DevicePixelRatio:0.##} | {_pointerType} | " +
            $"{diagnostics?.UploadPath ?? "starting"} | upload {diagnostics?.LastUploadCpuMilliseconds ?? 0:0.00} ms | " +
            $"frames {diagnostics?.UploadCount ?? 0} | loss {diagnostics?.ContextLossCount ?? 0} | recreate {_backendRecreateCount}";
    }

    private void BeginScreenBatch(BlendState blendState, SamplerState? samplerState = null)
    {
        _batch!.Begin(
            SpriteSortMode.Deferred,
            blendState,
            samplerState,
            null,
            null,
            null,
            _screenTransform);
    }

    private void RecreateBackend()
    {
        SkiaRenderer.Dispose();
        _gum!.ResetTexture();
        // The page's SkiaRenderer.AttachHost call (Index.razor.cs) is still in effect - Dispose only
        // clears the current backend/GraphicsDevice pairing, not the attached host - so this
        // reconstructs a fresh SkiaWebGlBackend from that same host.
        SkiaRenderer.Initialize(GraphicsDevice);
        _backendRecreateCount++;
    }

    private void DrawWithShader(Texture2D texture)
    {
        _shader!.Texture = texture;
        foreach (var pass in _shader.CurrentTechnique.Passes)
        {
            pass.Apply();
            GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, _shaderVertices, 0, 2);
        }
    }

    protected override void UnloadContent()
    {
        _gum?.Dispose();
        SkiaRenderer.Dispose();
        _shader?.Dispose();
        _uiTarget?.Dispose();
        _pixel?.Dispose();
        _batch?.Dispose();
        base.UnloadContent();
    }
}
