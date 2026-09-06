using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Benchmarks.WebGLOptionA.Pages;

[SupportedOSPlatform("browser")]
public partial class Index
{
    private BenchGame? _game;
    private OptionAFrameRunner? _runner;
    private DotNetObjectReference<Index>? _selfReference;
    private string _status = "Initializing WebGL + KNI + Skia context bridge...";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var imported = await JS.InvokeAsync<IJSObjectReference>("import", "./js/context-bridge.js");
        var module = imported as IJSInProcessObjectReference
            ?? throw new PlatformNotSupportedException("Synchronous WebGL interop requires Blazor WebAssembly.");

        // KNI's BlazorGameWindow binds to #theCanvas by convention and creates the real WebGL2
        // context as a side effect of Run() - see BenchGame.cs. _width/_height come from this
        // page's ?w=&h= query parameters (set in OnInitialized, before this render), matching the
        // canvas element's own width/height attribute set by the markup above.
        _game = new BenchGame(_width, _height);
        _game.Run();

        _runner = OptionAFrameRunner.Create(module, _game);
        if (_runner == null)
        {
            _status = "FAILED to initialize the Option A context bridge - see browser console for detail.";
            StateHasChanged();
            return;
        }

        _selfReference = DotNetObjectReference.Create(this);
        _status = "Ready.";
        StateHasChanged();
        await JS.InvokeVoidAsync("optionABenchmark.init", _selfReference, _runner.ContextUid, _width, _height);
    }

    // Called synchronously (instance.invokeMethod, not Async) from wwwroot/js/optionA-benchmark.js's
    // per-frame loop - see that file for why synchronous matters (it lets a GPU timer query
    // begin/end call bracket exactly this call).
    [JSInvokable]
    public double RunFrame(int width, int height) => _runner!.RunFrame(width, height);

    [JSInvokable]
    public int[] ReadCenterPixel(int width, int height) => _runner!.ReadCenterPixel(width, height);

    public ValueTask DisposeAsync()
    {
        _selfReference?.Dispose();
        _game?.Dispose();
        return ValueTask.CompletedTask;
    }
}
