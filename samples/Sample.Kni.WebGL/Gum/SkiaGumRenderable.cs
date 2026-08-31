using System.Diagnostics;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RenderingLibrary;
using SkiaGameRendering;
using SkiaSharp;
using Topten.RichTextKit;

namespace Sample.Kni.WebGL.Gum;

internal sealed class SkiaGumRenderable : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly int _width;
    private readonly int _height;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly SKPaint _accentPaint = new() { Color = new SKColor(238, 84, 74), IsAntialias = true };
    private SkiaRenderTarget2D? _canvas;
    private ContainerRuntime? _root;
    private RectangleRuntime? _button;
    private TextRuntime? _buttonText;
    private TextRuntime? _editableText;
    private ContainerRuntime? _scrollContent;
    private Matrix _screenToUi = Matrix.Identity;
    private bool _pressed;
    private bool _hovered;
    private float _scrollOffset;
    private bool _backendRecreateRequested;
    private FontMapper? _previousFontMapper;
    private EmbeddedFontMapper? _fontMapper;

    public SkiaGumRenderable(GraphicsDevice graphicsDevice, int width, int height)
    {
        _graphicsDevice = graphicsDevice;
        _width = width;
        _height = height;
    }

    public Texture2D? Texture => _canvas?.Texture;
    public float Dpi { get; set; } = 1;
    private int TargetWidth => Math.Max(1, (int)MathF.Round(_width * Dpi));
    private int TargetHeight => Math.Max(1, (int)MathF.Round(_height * Dpi));

    public void Draw()
    {
        RecreateCanvasIfNeeded();

        _canvas!.Begin();
        DrawToSurface(_canvas.Canvas);
        _canvas.End();
    }

    // SkiaRenderTarget2D is fixed-size for its lifetime - a DPI change means a new render target,
    // not a resize. Explicit here since the library won't do it silently anymore.
    private void RecreateCanvasIfNeeded()
    {
        var width = TargetWidth;
        var height = TargetHeight;
        if (_canvas != null && _canvas.Texture.Width == width && _canvas.Texture.Height == height)
            return;

        _canvas?.Dispose();
        _canvas = new SkiaRenderTarget2D(_graphicsDevice, width, height);
    }

    private void DrawToSurface(SKCanvas canvas)
    {
        if (_root == null)
        {
            try
            {
                InitializeGum(canvas);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(exception.ToString(), exception);
            }
        }

        canvas.Scale(Dpi);
        SystemManagers.Default.Canvas = canvas;
        _button!.FillColor = _pressed
            ? new SKColor(185, 55, 49)
            : _hovered ? new SKColor(74, 141, 192) : new SKColor(54, 122, 178);
        _scrollContent!.Y = _scrollOffset;
        _root!.AnimateSelf(1.0 / 60.0);
        try
        {
            SystemManagers.Default.Draw();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(exception.ToString(), exception);
        }

        var x = 420 + MathF.Sin((float)_clock.Elapsed.TotalSeconds * 2) * 22;
        canvas.DrawCircle(x, 42, 12, _accentPaint);
    }

    private void InitializeGum(SKCanvas canvas)
    {
        _fontMapper = new EmbeddedFontMapper();
        _previousFontMapper = FontMapper.Default;
        FontMapper.Default = _fontMapper;
        SystemManagers.Default = new SystemManagers { Canvas = canvas };
        SystemManagers.Default.Initialize();
        SystemManagers.Default.Renderer.ClearsCanvas = false;
        GraphicalUiElement.CanvasWidth = _width;
        GraphicalUiElement.CanvasHeight = _height;

        _root = new ContainerRuntime
        {
            Width = _width,
            Height = _height,
            Name = "WebGL Gum Root",
        };

        var panel = new RectangleRuntime
        {
            X = 12,
            Y = 12,
            Width = _width - 24,
            Height = _height - 24,
            FillColor = new SKColor(245, 247, 249, 238),
            IsFilled = true,
            CornerRadius = 8,
        };
        _root.Children.Add(panel);

        panel.Children.Add(new TextRuntime
        {
            X = 18,
            Y = 14,
            Text = "SKIA + GUM / CURRENT FRAME",
            Red = 29,
            Green = 35,
            Blue = 41,
        });

        _button = new RectangleRuntime
        {
            X = 18,
            Y = 52,
            Width = 205,
            Height = 42,
            FillColor = new SKColor(54, 122, 178),
            IsFilled = true,
            CornerRadius = 5,
        };
        panel.Children.Add(_button);
        _buttonText = new TextRuntime
        {
            X = 16,
            Y = 10,
            Text = "Interactive Gum button",
            Red = 255,
            Green = 255,
            Blue = 255,
        };
        _button.Children.Add(_buttonText);

        var editFrame = new RectangleRuntime
        {
            X = 18,
            Y = 105,
            Width = 260,
            Height = 38,
            FillColor = SKColors.White,
            IsFilled = true,
            StrokeColor = new SKColor(90, 102, 113),
            StrokeWidth = 1,
            CornerRadius = 3,
        };
        panel.Children.Add(editFrame);
        _editableText = new TextRuntime
        {
            X = 10,
            Y = 8,
            Text = "KNI WebGL:",
            Red = 32,
            Green = 38,
            Blue = 44,
        };
        editFrame.Children.Add(_editableText);

        var scrollViewport = new RectangleRuntime
        {
            X = 18,
            Y = 154,
            Width = 285,
            Height = 96,
            FillColor = new SKColor(225, 230, 234),
            IsFilled = true,
            ClipsChildren = true,
            CornerRadius = 3,
        };
        panel.Children.Add(scrollViewport);
        _scrollContent = new ContainerRuntime
        {
            X = 10,
            Y = 0,
            Width = 260,
            Height = 200,
            ChildrenLayout = ChildrenLayout.TopToBottomStack,
            StackSpacing = 5,
        };
        scrollViewport.Children.Add(_scrollContent);
        for (var i = 0; i < 9; i++)
        {
            _scrollContent.Children.Add(new TextRuntime
            {
                Text = $"Scrollable Gum row {i + 1}",
                Red = 46,
                Green = 55,
                Blue = 63,
            });
        }

        _root.AddToManagers(SystemManagers.Default);
        _root.UpdateLayout();
    }

    public void SetPresentationTransform(Matrix screenToPresentation)
    {
        Matrix.Invert(ref screenToPresentation, out _screenToUi);
    }

    public void HandlePointer(float screenX, float screenY, bool down, int wheelDelta)
    {
        var ui = Vector2.Transform(new Vector2(screenX, screenY), _screenToUi);
        _hovered = ui.X >= 30 && ui.X <= 235 && ui.Y >= 64 && ui.Y <= 106;
        if (_hovered && _pressed && !down && _buttonText != null)
        {
            _buttonText.Text = "Recreating WebGL backend...";
            _backendRecreateRequested = true;
        }
        _pressed = down && _hovered;

        if (wheelDelta != 0 && ui.X >= 30 && ui.X <= 315 && ui.Y >= 166 && ui.Y <= 262)
            _scrollOffset = Math.Clamp(_scrollOffset + Math.Sign(wheelDelta) * 18, -100, 0);
    }

    public void HandleText(string text)
    {
        if (_editableText == null)
            return;

        foreach (var character in text)
        {
            if (character == '\b')
            {
                const string prefix = "KNI WebGL:";
                var currentText = _editableText.Text ?? string.Empty;
                if (currentText.Length > prefix.Length)
                    _editableText.Text = currentText[..^1];
            }
            else if (!char.IsControl(character))
            {
                _editableText.Text += character;
            }
        }
    }

    public bool ConsumeBackendRecreateRequest()
    {
        var requested = _backendRecreateRequested;
        _backendRecreateRequested = false;
        return requested;
    }

    public void ResetTexture()
    {
        _canvas?.Dispose();
        _canvas = null;
    }

    public void Dispose()
    {
        _canvas?.Dispose();
        _canvas = null;
        if (ReferenceEquals(FontMapper.Default, _fontMapper) && _previousFontMapper != null)
            FontMapper.Default = _previousFontMapper;
        _fontMapper?.Dispose();
        _fontMapper = null;
        _accentPaint.Dispose();
        _clock.Stop();
    }

    private sealed class EmbeddedFontMapper : FontMapper, IDisposable
    {
        private readonly SKTypeface _typeface;

        internal EmbeddedFontMapper()
        {
            using var stream = typeof(SkiaGumRenderable).Assembly.GetManifestResourceStream("NotoSans.ttf")
                ?? throw new InvalidOperationException("Embedded Noto Sans font was not found.");
            _typeface = SKTypeface.FromStream(stream)
                ?? throw new InvalidOperationException("Embedded Noto Sans font could not be decoded.");
        }

        public override SKTypeface TypefaceFromStyle(IStyle style, bool ignoreFontVariants) => _typeface;

        public void Dispose() => _typeface.Dispose();
    }
}
