using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using System.Runtime.Versioning;
using SkiaGameRendering;
using SkiaGameRendering.Kni.WebGL;
using SkiaGameRendering.Kni.WebGL.Components;

namespace Sample.Kni.WebGL.Pages;

[SupportedOSPlatform("browser")]
public partial class Index
{
    private SkiaGameWebGlHost? _skiaHost;
    private DotNetObjectReference<Index>? _selfReference;
    private Game1? _game;
    private string _status = "Initializing WebGL...";
    private int _frame;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        // Attach once, page-lifetime. Game1 itself never awaits anything or takes a constructor
        // argument - it just polls SkiaRenderer.IsReady from Update() before calling
        // SkiaRenderer.Initialize(GraphicsDevice). This is the one and only place a WebGL-specific
        // type gets named; everything Game1 touches is on SkiaRenderer's shared, platform-agnostic
        // surface, so the same Game1 code is what a host that constructs Game itself (with no
        // constructor hook - e.g. an in-browser fiddle) would also write.
        SkiaRenderer.AttachHost(_skiaHost!, new SkiaWebGlOptions
        {
            RequireWebGl2 = true,
            EnableDiagnostics = true,
            FlipY = false,
            PremultiplyAlpha = true,
            DisableColorSpaceConversion = true,
        });

        await _skiaHost!.Ready;
        _status = $"{_skiaHost.WebGlVersion} | {_skiaHost.Renderer}";
        StateHasChanged();
        _selfReference = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("skiaKniSample.start", _selfReference);
    }

    [JSInvokable]
    public string? Tick(
        double devicePixelRatio,
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
        if (_game == null)
        {
            _game = new Game1();
            _game.Run();
        }

        _game.DevicePixelRatio = devicePixelRatio;
        _game.SetBrowserState(
            physicalWidth, physicalHeight, pointerX, pointerY, pointerDown,
            wheelDelta, textInput, pointerType, diagnosticTexImage);
        _game.Tick();
        return ++_frame % 30 == 0 ? _game.GetDiagnostics() : null;
    }

    public async ValueTask DisposeAsync()
    {
        await JS.InvokeVoidAsync("skiaKniSample.stop");
        _game?.Dispose();
        _game = null;
        _selfReference?.Dispose();
        if (_skiaHost != null)
            await _skiaHost.DisposeAsync();
    }
}
