using System.Runtime.Versioning;
using Microsoft.JSInterop;
using SkiaGameRendering;

namespace Performance.Pages;

[SupportedOSPlatform("browser")]
public partial class Index
{
    private SkiaGameWebGlHost? _skiaHost;
    private DotNetObjectReference<Index>? _self;
    private SkiaGame? _game;
    private string _browser = "Browser";
    private string[] _args = Array.Empty<string>();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        // Attach once, page-lifetime, like samples/Sample.Kni.WebGL.
        SkiaRenderer.AttachHost(_skiaHost!, new SkiaWebGlOptions
        {
            RequireWebGl2 = true,
            EnableDiagnostics = false,
            FlipY = false,
            PremultiplyAlpha = true,
            DisableColorSpaceConversion = true,
        });
        await _skiaHost!.Ready;

        (_browser, _args) = PerfPage.Configure(JS);
        _self = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("perfLoop.start", _self);
    }

    /// <summary>One frame. True once every entry has been measured and the results published.</summary>
    [JSInvokable]
    public bool Tick()
    {
        if (_game == null)
        {
            _game = new SkiaGame($"KNI WebGL ({_browser}) Skia", _args);
            _game.Run();
        }

        _game.Tick();
        return _game.Finished;
    }
}
