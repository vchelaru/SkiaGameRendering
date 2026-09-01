using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using System.Runtime.Versioning;
using SkiaGameRendering.Kni.WebGL;
using SkiaGameRendering.Kni.WebGL.Components;

namespace Sample.Kni.WebGL.Pages;

[SupportedOSPlatform("browser")]
public partial class Index
{
    private SkiaGameWebGlHost? _skiaHost;
    private SkiaWebGlBackend? _backend;
    private DotNetObjectReference<Index>? _selfReference;
    private Game1? _game;
    private string _status = "Initializing WebGL...";
    private int _frame;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        // Constructing the backend doesn't require the host to be ready - only calling
        // SkiaRenderer.Initialize (inside Game1.Initialize) does. Awaiting backend.Ready here
        // (rather than a WebGL-specific host.Ready) is the same step every backend needs before
        // its Game can run - desktop backends just complete it immediately.
        _backend = new SkiaWebGlBackend(_skiaHost!, new SkiaWebGlOptions
        {
            RequireWebGl2 = true,
            EnableDiagnostics = true,
            FlipY = false,
            PremultiplyAlpha = true,
            DisableColorSpaceConversion = true,
        });
        await _backend.Ready;
        _status = $"{_skiaHost!.WebGlVersion} | {_skiaHost.Renderer}";
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
            _game = new Game1(_skiaHost!, _backend!);
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
