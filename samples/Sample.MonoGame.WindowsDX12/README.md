# Sample.MonoGame.WindowsDX12

Renders the shared `Sample.Shared.Scene` through `SkiaRenderTarget2D` on MonoGame's native
WindowsDX12 (D3D12) platform. `Game1.cs` is the same file the other MonoGame samples link.

It needs MonoGame `3.8.6`, the first stable release with `GraphicsDevice.GetNativeHandles()`
([MonoGame#9536](https://github.com/MonoGame/MonoGame/pull/9536)) and
`RenderTarget2D.FromNativeHandle()` ([MonoGame#9535](https://github.com/MonoGame/MonoGame/pull/9535)).
The native `mgruntime.dll` is a separate package, `MonoGame.Runtime.Windows.DX12`; without it the app
dies at startup with `DllNotFoundException: mgruntime`.

The library ships on NuGet as `SkiaGameRendering.WindowsDX12`; this sample references the project directly.

`--smoke-test` renders a few frames, checks the back buffer for the scene, and exits 0 or 1.

`--direct-smoke-test` draws with `SkiaDirectCanvas` into the back buffer and a render target for 14 frames and checks
every layer (`DirectDrawGame.cs`); `--direct-smoke-test-msaa` runs on a multisampled back buffer, which it must refuse.
`SKIAGAMERENDERING_D3D12_DEBUG=1` turns the D3D12 debug layer on for either, and CI does for these.

See `docs/documentation/SkiaDx12Backend.md` for how the backend shares the device and queue, and
`SkiaGameRendering-Notes.md` section 9 for what building it turned up.
