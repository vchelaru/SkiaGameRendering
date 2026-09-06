using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Benchmarks.WebGLOptionA.Pages;

[SupportedOSPlatform("browser")]
public partial class Index
{
    private BenchGame? _game;
    private OptionAFrameRunner? _runner;
    private OptionDFrameRunner? _runnerD;
    private DotNetObjectReference<Index>? _selfReference;
    private string _status = "Initializing WebGL + KNI + Skia context bridge...";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var imported = await JS.InvokeAsync<IJSObjectReference>("import", "./js/context-bridge.js");
        var module = imported as IJSInProcessObjectReference
            ?? throw new PlatformNotSupportedException("Synchronous WebGL interop requires Blazor WebAssembly.");
        // A SEPARATE module object from `module` above - each dynamically-imported ES module gets
        // its own reference, and IJSInProcessObjectReference.Invoke only finds functions exported
        // by the specific reference it's called on (not a global lookup across every imported
        // module) - this must be passed to OptionDFrameRunner.Create below, not `module`.
        var importedD = await JS.InvokeAsync<IJSObjectReference>("import", "./js/option-d-live.js");
        var moduleD = importedD as IJSInProcessObjectReference
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

        _runnerD = OptionDFrameRunner.Create(moduleD, _game, _runner.ContextUid);
        var hasOptionD = _runnerD != null;
        if (!hasOptionD)
            Console.WriteLine("[optionD] Live Option D path failed to initialize - continuing with Option A only. See preceding [optionD] log lines for why.");

        _selfReference = DotNetObjectReference.Create(this);
        _status = hasOptionD ? "Ready. (Option A + Option D, live)" : "Ready. (Option A only - Option D live init failed, see console)";
        StateHasChanged();
        await JS.InvokeVoidAsync("optionABenchmark.init", _selfReference, _runner.ContextUid, _width, _height, hasOptionD);
    }

    // Called synchronously (instance.invokeMethod, not Async) from wwwroot/js/optionA-benchmark.js's
    // per-frame loop - see that file for why synchronous matters (it lets a GPU timer query
    // begin/end call bracket exactly this call).
    [JSInvokable]
    public FrameTiming RunFrame(int width, int height) => _runner!.RunFrame(width, height);

    // Only called from JS when Index.razor.cs told it hasOptionD=true at init - _runnerD is
    // guaranteed non-null in that case.
    [JSInvokable]
    public OptionDFrameTiming RunOptionDFrame(int width, int height) => _runnerD!.RunFrame(width, height);

    [JSInvokable]
    public int[] ReadCenterPixel(int width, int height) => _runner!.ReadCenterPixel(width, height);

    public ValueTask DisposeAsync()
    {
        _selfReference?.Dispose();
        _game?.Dispose();
        return ValueTask.CompletedTask;
    }
}
