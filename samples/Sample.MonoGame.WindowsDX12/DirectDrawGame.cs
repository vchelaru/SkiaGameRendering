using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;
using Tests.Shared;

namespace Sample;

/// <summary>
/// --direct-smoke-test: draws with <see cref="SkiaDirectCanvas"/> into MonoGame's back buffer and into a
/// <see cref="RenderTarget2D"/> for a number of frames, and exits with 0 only if every pixel it reads back is
/// where the layers say it should be. SkiaDx12Backend cuts MonoGame's command list in two around each Skia
/// pass, so besides the layer order (<see cref="DirectDrawLayering"/>) this checks what a cut can lose:
/// state MonoGame set once and does not set again, and the blend factor.
/// </summary>
sealed class DirectDrawGame : Game
{
    const int Frames = 14;

    readonly List<string> _errors = new();
    readonly bool _multisampled;
    int _frame;

    public int ExitCode { get; private set; } = 1;

    /// <summary>An extra pass/fail check Program adds (the D3D12 debug layer's), run once the frames are done.</summary>
    public Func<GraphicsDevice, bool> ExtraCheck { get; set; }

    /// <param name="multisampled">Run on a multisampled back buffer, which SkiaDirectCanvas refuses.</param>
    public DirectDrawGame(bool multisampled = false)
    {
        _multisampled = multisampled;
        new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 320, PreferredBackBufferHeight = 240, PreferMultiSampling = multisampled };
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_frame == 0)
        {
            // Constructing a canvas initializes the renderer, and says why a backend refuses.
            try
            {
                using var _ = new SkiaDirectCanvas(GraphicsDevice);
            }
            catch (NotSupportedException exception)
            {
                Finish($"Direct smoke test FAILED: {exception.Message}");
                return;
            }
            Console.WriteLine($"SkiaRenderer.SupportsDirectCanvas={SkiaRenderer.SupportsDirectCanvas}");
        }

        if (_multisampled)
        {
            Check("multisampled back buffer", MultisampledBackBuffer());
            GraphicsDevice.SetRenderTarget(null);
            base.Draw(gameTime);
            if (++_frame == 3)
                Finish(null);
            return;
        }

        Check("back buffer", DirectDrawLayering.BackBuffer(GraphicsDevice));
        Check("render target", DirectDrawLayering.RenderTarget(GraphicsDevice, 0));
        if (_frame == 4)
            Check("resize control (no Skia)", ResizeControl());
        if (_frame == 5)
        {
            BindBackBuffer();
            Check("back buffer after a resize", DirectDrawLayering.BackBufferAfterResize(GraphicsDevice));
        }
        Check("state set once (control, no Skia)", StateSetOnce(false));
        Check("state set once", StateSetOnce(true));
        Check("blend factor (control, no Skia)", BlendFactorAfterCut(false));
        Check("blend factor", BlendFactorAfterCut(true));
        Check("unsupported targets", UnsupportedTargets());

        GraphicsDevice.SetRenderTarget(null);
        base.Draw(gameTime);

        if (++_frame == Frames)
            Finish(null);
    }

    void Check(string what, string error)
    {
        if (error != "")
            _errors.Add($"frame {_frame}, {what}: {error}");
    }

    void Finish(string failure)
    {
        var passed = failure == null && _errors.Count == 0;
        if (failure != null)
            Console.WriteLine(failure);
        foreach (var error in _errors.Take(10))
            Console.WriteLine("Direct smoke test error: " + error);
        if (passed && ExtraCheck != null)
            passed = ExtraCheck(GraphicsDevice);

        Console.WriteLine($"Direct smoke test {(passed ? "passed" : "FAILED")} after {_frame} frame(s)");
        ExitCode = passed ? 0 : 1;
        Exit();
    }

    /// <summary>
    /// A textured BasicEffect pass is applied once and an indexed quad is drawn before and after a Skia pass with
    /// nothing set in between. MonoGame sees no change, so it sends nothing; the second quad is only drawn
    /// correctly if the cut made the native layer re-record the pipeline state, buffers, constants, texture and
    /// sampler tables, and viewport.
    /// </summary>
    string StateSetOnce(bool withSkia)
    {
        BindBackBuffer();
        GraphicsDevice.Clear(Color.Black);

        var viewport = GraphicsDevice.Viewport;
        using var white = new Texture2D(GraphicsDevice, 1, 1);
        white.SetData(new[] { Color.White });
        using var effect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = true,
            TextureEnabled = true,
            Texture = white,
            LightingEnabled = false,
            View = Matrix.Identity,
            World = Matrix.Identity,
            Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, 1),
        };
        // Two quads in one buffer: the left half is drawn before the Skia pass, the right half after.
        var vertices = new VertexPositionColorTexture[8];
        Quad(vertices, 0, 20, 160, 60, 60, Color.Lime);
        Quad(vertices, 4, 200, 160, 60, 60, Color.Lime);
        using var vertexBuffer = new VertexBuffer(GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
        vertexBuffer.SetData(vertices);
        using var indexBuffer = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, 12, BufferUsage.WriteOnly);
        indexBuffer.SetData(new short[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 });

        GraphicsDevice.SetVertexBuffer(vertexBuffer);
        GraphicsDevice.Indices = indexBuffer;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        effect.CurrentTechnique.Passes[0].Apply();
        GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);

        if (withSkia)
        {
            using var skia = new SkiaDirectCanvas(GraphicsDevice);
            using var paint = new SKPaint { Color = new SKColor(210, 50, 90) };
            skia.Begin();
            skia.Canvas.DrawRect(120, 20, 40, 40, paint);
            skia.End();
        }

        GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 6, 2);

        var pixels = new Color[GraphicsDevice.PresentationParameters.BackBufferWidth * GraphicsDevice.PresentationParameters.BackBufferHeight];
        GraphicsDevice.GetBackBufferData(pixels);
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var errors = new List<string>();
        void Probe(string what, int x, int y, Color expected)
        {
            if (pixels[y * width + x] != expected)
                errors.Add($"{what} at ({x}, {y}): expected {expected}, got {pixels[y * width + x]}");
        }
        Probe("quad before the Skia pass", 50, 190, Color.Lime);
        if (withSkia)
            Probe("Skia", 140, 40, new Color(210, 50, 90));
        Probe("quad after the Skia pass, with nothing set again", 230, 190, Color.Lime);
        Probe("background", 140, 190, Color.Black);
        return string.Join("; ", errors);
    }

    /// <summary>
    /// MonoGame records the blend factor straight onto its command list when the blend state is applied. A
    /// SpriteBatch drawn with the same BlendState object before and after a Skia pass mixes both rectangles with
    /// the background; with the factor lost to the cut, the second one would come out as its own color.
    /// </summary>
    string BlendFactorAfterCut(bool withSkia)
    {
        BindBackBuffer();
        GraphicsDevice.Clear(Color.Black);

        var half = new BlendState
        {
            ColorSourceBlend = Blend.BlendFactor,
            ColorDestinationBlend = Blend.InverseBlendFactor,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.Zero,
            BlendFactor = new Color(128, 128, 128, 255),
        };
        using var white = new Texture2D(GraphicsDevice, 1, 1);
        white.SetData(new[] { Color.White });
        using var spriteBatch = new SpriteBatch(GraphicsDevice);

        spriteBatch.Begin(SpriteSortMode.Deferred, half);
        spriteBatch.Draw(white, new Rectangle(10, 10, 50, 50), Color.White);
        spriteBatch.End();

        if (withSkia)
        {
            using var skia = new SkiaDirectCanvas(GraphicsDevice);
            using var paint = new SKPaint { Color = SKColors.Blue };
            skia.Begin();
            skia.Canvas.DrawRect(100, 10, 50, 50, paint);
            skia.End();
        }

        spriteBatch.Begin(SpriteSortMode.Deferred, half);
        spriteBatch.Draw(white, new Rectangle(200, 10, 50, 50), Color.White);
        spriteBatch.End();

        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var pixels = new Color[width * GraphicsDevice.PresentationParameters.BackBufferHeight];
        GraphicsDevice.GetBackBufferData(pixels);
        var errors = new List<string>();
        // Half of white over black.
        foreach (var (what, x) in new[] { ("before the Skia pass", 30), ("after the Skia pass", 220) })
        {
            var actual = pixels[30 * width + x];
            if (Math.Abs(actual.R - 128) > 2 || Math.Abs(actual.G - 128) > 2 || Math.Abs(actual.B - 128) > 2)
                errors.Add($"blend {what}: expected about (128, 128, 128), got {actual}");
        }
        if (withSkia && pixels[30 * width + 120] != Color.Blue)
            errors.Add($"Skia: expected blue, got {pixels[30 * width + 120]}");
        return string.Join("; ", errors);
    }

    /// <summary>MonoGame resizing its back buffer with no Skia involved, to tell its own faults from ours.</summary>
    string ResizeControl()
    {
        BindBackBuffer();
        var parameters = GraphicsDevice.PresentationParameters.Clone();
        parameters.BackBufferWidth += 32;
        parameters.BackBufferHeight += 16;
        GraphicsDevice.Reset(parameters);
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        using var white = new Texture2D(GraphicsDevice, 1, 1);
        white.SetData(new[] { Color.White });
        using var spriteBatch = new SpriteBatch(GraphicsDevice);
        spriteBatch.Begin();
        spriteBatch.Draw(white, new Rectangle(0, 0, 10, 10), Color.Red);
        spriteBatch.End();
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var pixels = new Color[width * GraphicsDevice.PresentationParameters.BackBufferHeight];
        GraphicsDevice.GetBackBufferData(pixels);
        return pixels[5 * width + 5] == Color.Red ? "" : $"got {pixels[5 * width + 5]}";
    }

    /// <summary>
    /// A multisampled back buffer is refused with NotSupportedException (Skia cannot draw into a multisampled
    /// resource), and MonoGame draws on as before: the rectangles on either side of the refused Begin both land.
    /// </summary>
    string MultisampledBackBuffer()
    {
        BindBackBuffer();
        GraphicsDevice.Clear(Color.Black);
        var errors = new List<string>();
        if (GraphicsDevice.PresentationParameters.MultiSampleCount < 2)
            return "the back buffer is not multisampled; MonoGame ignored PreferMultiSampling";

        using var white = new Texture2D(GraphicsDevice, 1, 1);
        white.SetData(new[] { Color.White });
        using var spriteBatch = new SpriteBatch(GraphicsDevice);
        spriteBatch.Begin();
        spriteBatch.Draw(white, new Rectangle(0, 0, 20, 20), Color.Red);
        spriteBatch.End();

        using (var skia = new SkiaDirectCanvas(GraphicsDevice))
        {
            try
            {
                skia.Begin();
                errors.Add("a multisampled back buffer was accepted");
                skia.End();
            }
            catch (NotSupportedException exception)
            {
                if (!exception.Message.Contains("multisampled"))
                    errors.Add("the refusal does not say why: " + exception.Message);
            }
        }

        spriteBatch.Begin();
        spriteBatch.Draw(white, new Rectangle(40, 0, 20, 20), Color.Lime);
        spriteBatch.End();

        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var pixels = new Color[width * GraphicsDevice.PresentationParameters.BackBufferHeight];
        GraphicsDevice.GetBackBufferData(pixels);
        if (pixels[10 * width + 10] != Color.Red)
            errors.Add($"the rectangle before the refusal gave {pixels[10 * width + 10]}");
        if (pixels[10 * width + 50] != Color.Lime)
            errors.Add($"the rectangle after the refusal gave {pixels[10 * width + 50]}");
        return string.Join("; ", errors);
    }

    /// <summary>Refusals leave MonoGame's command list alone: a draw right after one still lands.</summary>
    string UnsupportedTargets()
    {
        BindBackBuffer();
        GraphicsDevice.Clear(Color.Black);
        var errors = new List<string>();

        using (var skia = new SkiaDirectCanvas(GraphicsDevice))
        {
            // Several bound targets.
            using var first = new RenderTarget2D(GraphicsDevice, 16, 16);
            using var second = new RenderTarget2D(GraphicsDevice, 16, 16);
            GraphicsDevice.SetRenderTargets(first, second);
            try
            {
                skia.Begin();
                errors.Add("two bound targets were accepted");
                skia.End();
            }
            catch (NotSupportedException)
            {
            }
            GraphicsDevice.SetRenderTarget(null);
        }

        using (var skia = new SkiaDirectCanvas(GraphicsDevice))
        {
            // A 10-bit target, which Skia is not asked to encode for.
            using var tenBit = new RenderTarget2D(GraphicsDevice, 16, 16, false, SurfaceFormat.Rgba1010102, DepthFormat.None);
            GraphicsDevice.SetRenderTarget(tenBit);
            try
            {
                skia.Begin();
                errors.Add("a 10-bit target was accepted");
                skia.End();
            }
            catch (NotSupportedException)
            {
            }
            GraphicsDevice.SetRenderTarget(null);
        }

        // The refusals must not have cut MonoGame's list: it still draws.
        using var white = new Texture2D(GraphicsDevice, 1, 1);
        white.SetData(new[] { Color.White });
        using var spriteBatch = new SpriteBatch(GraphicsDevice);
        spriteBatch.Begin();
        spriteBatch.Draw(white, new Rectangle(0, 0, 10, 10), Color.Red);
        spriteBatch.End();
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var pixels = new Color[width * GraphicsDevice.PresentationParameters.BackBufferHeight];
        GraphicsDevice.GetBackBufferData(pixels);
        if (pixels[5 * width + 5] != Color.Red)
            errors.Add($"a draw after the refusals gave {pixels[5 * width + 5]}");
        return string.Join("; ", errors);
    }

    /// <summary>
    /// Makes the back buffer MonoGame's bound target in its command list. MonoGame's own GetBackBufferData and
    /// GetData restart the list without rebinding the target, and SetRenderTarget(null) does nothing while the
    /// back buffer is already the managed target, so a test that follows a readback has to bind it afresh.
    /// </summary>
    void BindBackBuffer()
    {
        using var other = new RenderTarget2D(GraphicsDevice, 16, 16);
        GraphicsDevice.SetRenderTarget(other);
        GraphicsDevice.SetRenderTarget(null);
    }

    static void Quad(VertexPositionColorTexture[] vertices, int start, float x, float y, float width, float height, Color color)
    {
        vertices[start] = new VertexPositionColorTexture(new Vector3(x, y, 0), color, new Vector2(0, 0));
        vertices[start + 1] = new VertexPositionColorTexture(new Vector3(x + width, y, 0), color, new Vector2(1, 0));
        vertices[start + 2] = new VertexPositionColorTexture(new Vector3(x, y + height, 0), color, new Vector2(0, 1));
        vertices[start + 3] = new VertexPositionColorTexture(new Vector3(x + width, y + height, 0), color, new Vector2(1, 1));
    }
}
