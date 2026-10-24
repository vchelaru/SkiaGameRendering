using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.Versioning;
using Sample.Shared;
using SkiaGameRendering;
using SkiaSharp;

namespace Sample.Kni.WebGL;

/// <summary>
/// Draws the shared <see cref="Scene"/> like every other sample. The Skia canvas covers the whole
/// WebGL canvas in physical pixels, so Scene's cells scale with the browser's device pixel ratio.
/// Parameterless on purpose: the page's code-behind calls SkiaRenderer.AttachHost once, and this
/// class only touches SkiaRenderer's shared surface (IsReady, Initialize(GraphicsDevice),
/// CurrentBackend), the same code a host that constructs Game itself would write.
/// The page's draw mode picks how: through a SkiaRenderTarget2D (the default, the blit path), or with
/// SkiaDirectCanvas straight into KNI's back buffer and render targets (<see cref="DrawDirect"/>).
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class Game1 : Game
{
    // Direct mode draws small test squares below the Scene's cells; tests/Tests.Kni.WebGL/tests/direct.spec.js
    // repeats this layout (and the colors) to probe them. Squares are SquareSize physical pixels, in a row
    // starting at x = Gap, SquareSize + Gap apart, at y = cell + Gap where cell is half the canvas's shorter side.
    internal const int SquareSize = 128;
    internal const int Gap = 16;

    private static readonly Color Background = new(20, 30, 200);
    private static readonly Color Before = new(30, 180, 40);
    private static readonly SKColor SkiaStrip = new(210, 50, 90);
    private static readonly Color After = new(240, 220, 10);
    private static readonly Color A = new(40, 90, 160);
    private static readonly SKColor B = new(220, 40, 40);
    private static readonly Color C = new(40, 200, 80);
    private static readonly SKColor D = new(250, 160, 20);
    private static readonly Color E = new(120, 40, 200);

    private readonly GraphicsDeviceManager _graphics;
    private SkiaRenderTarget2D? _canvas;
    private SkiaDirectCanvas? _direct;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _white;
    private RenderTarget2D? _plainTarget;
    private RenderTarget2D? _msaaTarget;
    private int _width = 1280;
    private int _height = 720;
    private string _drawMode = "texture";
    private long _directFrames;
    private long _directFailures;
    private string _directError = "";

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

        if (_drawMode != "texture")
        {
            if (SkiaRenderer.IsInitialized)
                DrawDirect(sceneOnly: _drawMode == "scene");
        }
        else if (SkiaRenderer.IsInitialized && Host?.IsContextLost != true)
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

    /// <summary>
    /// The Scene straight into the back buffer, then SkiaDirectCanvas layered between SpriteBatch draws on the
    /// back buffer, a plain render target and a multisampled one. With <paramref name="sceneOnly"/> only the
    /// Scene, so that no KNI resource is involved: KNI cannot rebuild its own after a lost context, and the
    /// tests use this mode to see Skia's side recover. A lost KNI context makes Begin throw; the frame is
    /// skipped and counted instead of ending the game.
    /// </summary>
    private void DrawDirect(bool sceneOnly)
    {
        try
        {
            _direct ??= new SkiaDirectCanvas(GraphicsDevice);
            if (sceneOnly)
            {
                _direct.Begin();
                Scene.Draw(_direct.Canvas, _width, _height);
                _direct.End();
                _directFrames++;
                return;
            }

            _spriteBatch ??= new SpriteBatch(GraphicsDevice);
            if (_white == null)
            {
                _white = new Texture2D(GraphicsDevice, 1, 1);
                _white.SetData(new[] { Color.White });
            }

            _plainTarget ??= new RenderTarget2D(GraphicsDevice, SquareSize, SquareSize, false, SurfaceFormat.Color,
                DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            _msaaTarget ??= new RenderTarget2D(GraphicsDevice, SquareSize, SquareSize, false, SurfaceFormat.Color,
                DepthFormat.None, 4, RenderTargetUsage.DiscardContents);
            var drawMsaa = _msaaTarget.MultiSampleCount == 4;

            // Unbinding to the back buffer clears it (the engine's DiscardContents), so the targets are
            // drawn first and the back buffer cleared again before anything that has to stay.
            DrawLayersInTarget(_plainTarget);
            if (drawMsaa)
                DrawLayersInTarget(_msaaTarget);
            GraphicsDevice.Clear(Color.Black);

            _direct.Begin();
            Scene.Draw(_direct.Canvas, _width, _height);
            _direct.End();

            var cell = Math.Min(_width, _height) / 2;
            var y = cell + Gap;
            var x = Gap;

            DrawLayers(x, y);
            DrawInterleaving(x + SquareSize + Gap, y);
            DrawTarget(_plainTarget, x + 2 * (SquareSize + Gap), y);
            if (drawMsaa)
                DrawTarget(_msaaTarget, x + 3 * (SquareSize + Gap), y);

            _directFrames++;
        }
        catch (InvalidOperationException exception)
        {
            // KNI's context is lost, or the engine was left bound to a target a pass cannot draw into.
            GraphicsDevice.SetRenderTarget(null);
            _directFailures++;
            _directError = exception.Message;
        }
    }

    private void Fill(int x, int y, int width, int height, Color color)
    {
        _spriteBatch!.Begin();
        _spriteBatch.Draw(_white, new Rectangle(x, y, width, height), color);
        _spriteBatch.End();
    }

    private void FillSkia(int ox, int oy, int x, int y, int width, int height, SKColor color)
    {
        using var paint = new SKPaint { Color = color };
        _direct!.Begin();
        _direct.Canvas.DrawRect(ox + x, oy + y, width, height, paint);
        _direct.End();
    }

    /// <summary>
    /// SpriteBatch under, Skia over it, SpriteBatch over that, like tests/Shared/DirectDrawLayering.cs:
    /// every probe pixel is covered by a known set of layers.
    /// </summary>
    private void DrawLayers(int ox, int oy)
    {
        var s = SquareSize;
        Fill(ox, oy, s, s, Background);
        Fill(ox, oy, s / 2, s / 2, Before);
        // Twice, so the second pass reuses the cached surface.
        for (var i = 0; i < 2; i++)
            FillSkia(ox, oy, s / 4, 0, s / 2, s / 4, SkiaStrip);
        Fill(ox + s / 2, oy + s / 8, s / 2, s * 3 / 8, After);
    }

    /// <summary>KNI, Skia, KNI, Skia, KNI in turn, each overlapping the last.</summary>
    private void DrawInterleaving(int ox, int oy)
    {
        var s = SquareSize;
        Fill(ox, oy, s, s, A);
        FillSkia(ox, oy, 0, 0, s / 2, s, B);
        Fill(ox, oy, s, s / 2, C);
        FillSkia(ox, oy, s / 4, s / 4, s / 2, s / 2, D);
        Fill(ox + s * 3 / 8, oy + s * 3 / 8, s / 4, s / 4, E);
    }

    /// <summary>The layers drawn into <paramref name="target"/>, with Skia drawing straight into it.</summary>
    private void DrawLayersInTarget(RenderTarget2D target)
    {
        GraphicsDevice.SetRenderTarget(target);
        try
        {
            DrawLayers(0, 0);
        }
        finally
        {
            GraphicsDevice.SetRenderTarget(null);
        }
    }

    private void DrawTarget(RenderTarget2D target, int ox, int oy)
    {
        _spriteBatch!.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
        _spriteBatch.Draw(target, new Rectangle(ox, oy, SquareSize, SquareSize), Color.White);
        _spriteBatch.End();
    }

    public void SetBrowserState(int physicalWidth, int physicalHeight, bool diagnosticTexImage, string drawMode)
    {
        if (physicalWidth > 0 && physicalHeight > 0)
        {
            _width = physicalWidth;
            _height = physicalHeight;
            GraphicsDevice.Viewport = new Viewport(0, 0, physicalWidth, physicalHeight);
        }

        _drawMode = drawMode;
        if (Backend != null)
            Backend.Options.UploadMode = diagnosticTexImage
                ? WebGlUploadMode.DiagnosticTexImage2D
                : WebGlUploadMode.DirectCanvasTexSubImage2D;
    }

    /// <summary>
    /// Disposes Skia's side and lets the next Draw initialize a new backend on the same host, the way a
    /// game that replaces its backend or GraphicsDevice would. Called between frames, from the page.
    /// </summary>
    public void RecreateSkia()
    {
        _canvas?.Dispose();
        _canvas = null;
        _direct?.Dispose();
        _direct = null;
        SkiaRenderer.Dispose();
    }

    public string GetDiagnostics()
    {
        var diagnostics = Backend?.Diagnostics;
        var text = $"{Host?.WebGlVersion ?? "starting"} | {_width}x{_height} | " +
            $"{(_drawMode != "texture" ? "SkiaDirectCanvas" : diagnostics?.UploadPath ?? "starting")} | " +
            $"upload {diagnostics?.LastUploadCpuMilliseconds ?? 0:0.00} ms | " +
            $"frames {(_drawMode != "texture" ? _directFrames : diagnostics?.UploadCount ?? 0)} | loss {diagnostics?.ContextLossCount ?? 0}";
        if (_drawMode != "texture")
            text += $" | direct {diagnostics?.DirectDrawCount ?? 0} | directloss {diagnostics?.DirectContextLossCount ?? 0}" +
                $" | skipped {_directFailures} | msaa {_msaaTarget?.MultiSampleCount ?? 0}" +
                (_directError.Length > 0 ? $" | error {_directError}" : "");
        return text;
    }

    protected override void UnloadContent()
    {
        _canvas?.Dispose();
        _direct?.Dispose();
        _plainTarget?.Dispose();
        _msaaTarget?.Dispose();
        _white?.Dispose();
        _spriteBatch?.Dispose();
        SkiaRenderer.Dispose();
        base.UnloadContent();
    }
}
