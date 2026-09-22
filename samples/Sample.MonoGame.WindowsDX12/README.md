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

## This does not compile yet

`Sample.MonoGame.WindowsDX12.csproj` references `MonoGame.Framework.Native` `3.8.5.1` - the latest
build on NuGet as of this writing - the same `PackageReference` style every other sample in this
repo uses. `GraphicsDevice.GetNativeHandles()` and `RenderTarget2D.FromNativeHandle()` don't exist
in that version, so this fails to compile until both PRs above merge and ship in a release: bump
the `Version` on both `PackageReference`s in the csproj to that release once it's out.

It is deliberately **not** part of `SkiaGameRendering.sln`, `tests/Tests.proj`, or CI — it doesn't
compile today, so it's a standalone project with its own `.slnx`, same as this repo's other samples.

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
