using System.Runtime.Versioning;
using Microsoft.JSInterop;

namespace Performance.Pages;

[SupportedOSPlatform("browser")]
public partial class Index
{
    private DotNetObjectReference<Index>? _self;
    private RawGame? _game;
    private string _browser = "Browser";
    private string[] _args = Array.Empty<string>();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

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
            _game = new RawGame($"KNI WebGL ({_browser}) Raw", _args);
            _game.Run();
        }

        _game.Tick();
        return _game.Finished;
    }
}
