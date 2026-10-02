using Microsoft.JSInterop;
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

        // Attach once, page-lifetime. This is the only place a WebGL-specific type gets named;
        // Game1 polls SkiaRenderer.IsReady before calling SkiaRenderer.Initialize(GraphicsDevice).
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
    public string? Tick(int physicalWidth, int physicalHeight, bool diagnosticTexImage)
    {
        if (_game == null)
        {
            _game = new Game1();
            _game.Run();
        }

        _game.SetBrowserState(physicalWidth, physicalHeight, diagnosticTexImage);
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
