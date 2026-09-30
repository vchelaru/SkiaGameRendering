using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.Versioning;
using Sample.Shared;
using SkiaGameRendering;
using SkiaGameRendering.Kni.WebGL;
using SkiaGameRendering.Kni.WebGL.Components;

namespace Sample.Kni.WebGL;

/// <summary>
/// Draws the shared <see cref="Scene"/> like every other sample. The Skia canvas covers the whole
/// WebGL canvas in physical pixels, so Scene's cells scale with the browser's device pixel ratio.
/// Parameterless on purpose: the page's code-behind calls SkiaRenderer.AttachHost once, and this
/// class only touches SkiaRenderer's shared surface (IsReady, Initialize(GraphicsDevice),
/// CurrentBackend), the same code a host that constructs Game itself would write.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SkiaRenderTarget2D? _canvas;
    private int _width = 1280;
    private int _height = 720;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferredBackBufferWidth = _width,
            PreferredBackBufferHeight = _height,
        };
    }

    private SkiaWebGlBackend? Backend => SkiaRenderer.CurrentBackend as SkiaWebGlBackend;
    private SkiaGameWebGlHost? Host => Backend?.Host;

    protected override void Draw(GameTime gameTime)
    {
        if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
            SkiaRenderer.Initialize(GraphicsDevice);

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        if (SkiaRenderer.IsInitialized && Host?.IsContextLost != true)
        {
            // SkiaRenderTarget2D is fixed-size for its lifetime, so a browser resize or DPR change
            // means a new one.
            if (_canvas == null || _canvas.Texture.Width != _width || _canvas.Texture.Height != _height)
            {
                _canvas?.Dispose();
                _canvas = new SkiaRenderTarget2D(GraphicsDevice, _width, _height);
            }

            _canvas.Begin();
            Scene.Draw(_canvas.Canvas, _width, _height);
            _canvas.End();
        }

        base.Draw(gameTime);
    }

    public void SetBrowserState(int physicalWidth, int physicalHeight, bool diagnosticTexImage)
    {
        if (physicalWidth > 0 && physicalHeight > 0)
        {
            _width = physicalWidth;
            _height = physicalHeight;
            GraphicsDevice.Viewport = new Viewport(0, 0, physicalWidth, physicalHeight);
        }

        if (Backend != null)
            Backend.Options.UploadMode = diagnosticTexImage
                ? WebGlUploadMode.DiagnosticTexImage2D
                : WebGlUploadMode.DirectCanvasTexSubImage2D;
    }

    public string GetDiagnostics()
    {
        var diagnostics = Backend?.Diagnostics;
        return $"{Host?.WebGlVersion ?? "starting"} | {_width}x{_height} | " +
            $"{diagnostics?.UploadPath ?? "starting"} | upload {diagnostics?.LastUploadCpuMilliseconds ?? 0:0.00} ms | " +
            $"frames {diagnostics?.UploadCount ?? 0} | loss {diagnostics?.ContextLossCount ?? 0}";
    }

    protected override void UnloadContent()
    {
        _canvas?.Dispose();
        SkiaRenderer.Dispose();
        base.UnloadContent();
    }
}
