# Sample.MonoGame.WindowsDX12

Proof-of-concept for [MonoGame/MonoGame#9536](https://github.com/MonoGame/MonoGame/pull/9536)
("Exposing Native GPU Handles") and [MonoGame/MonoGame#9535](https://github.com/MonoGame/MonoGame/pull/9535)
("Wrapping External Texture in RenderTarget2D") together. Both are needed: #9536 gets us MonoGame's
own `ID3D12Device`/`ID3D12CommandQueue` so we can create a D3D12 resource on the *same* device
MonoGame uses; #9535 is what lets us hand that resource back to MonoGame as a `RenderTarget2D` it
can actually draw with `SpriteBatch` — the native `WindowsDX12` `Texture2D` has no reflectable field
to inject an externally-created resource into the way the legacy D3D11 `WindowsDX` path does. This
is the pair of PRs SkiaGameRendering's WindowsDX12 glue is blocked on (issue #67).

`Game1.cs` renders `Sample.Shared.Scene` (the same red-circle scene every other sample in this repo
draws) with SkiaSharp directly into a texture every frame, with no CPU readback, and MonoGame draws
that same texture via `SpriteBatch`. If it works, you'll see the same red circle on black every
other sample shows, in a normal MonoGame window.

## Status

Both PRs shipped in MonoGame `3.8.6-preview.2`. The sample builds and renders the shared Scene (red circle and
blue drop). The native `mgruntime.dll` is a separate package, `MonoGame.Runtime.Windows.DX12`; without
it the app dies at startup with `DllNotFoundException: mgruntime`.

## Known gap: no cross-call synchronization primitive

Neither PR exposes a fence/semaphore. This sample relies on `D3D12SkiaSurfaceFactory.EndDraw()`'s
synchronous flush (`GRContext.Flush(submit: true, synchronous: true)`, which blocks the CPU until
the GPU finishes) plus single-threaded call order: our draw always completes, on the CPU timeline,
before MonoGame's own `Draw()` submits anything to the same queue. That's sufficient here, but it's
a real GPU stall every frame and isn't something a production backend should ship as-is — a
lighter-weight signal (e.g. a shared `ID3D12Fence` MonoGame waits on before sampling) would be
worth discussing if this interop shape is adopted.

## What's already verified independently

`tests/Tests.Core.D3D12/` proves the Skia-side D3D12 interop itself works (WARP device, golden-image
draw test) — this sample is only exercising the two new MonoGame APIs on top of that, not
re-verifying Skia/D3D12 from scratch.
