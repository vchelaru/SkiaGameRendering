# SkiaDx12Backend

## Definition

`SkiaDx12Backend` is the `SkiaBackend` for MonoGame's native WindowsDX12 (D3D12) platform. Skia's
Direct3D backend draws on the same `ID3D12Device` and `ID3D12CommandQueue` MonoGame renders with,
straight into a texture MonoGame then samples. No CPU readback and no reflection.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.WindowsDX12` (prerelease on NuGet: `dotnet add package SkiaGameRendering.WindowsDX12 --prerelease`, because MonoGame's native platform is itself prerelease)

```csharp
public class SkiaDx12Backend : SkiaBackend
```

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Initialize(GraphicsDevice)` | Reads the device, adapter and queue from `GraphicsDevice.GetNativeHandles()` and creates Skia's D3D12 context on them. Throws if the device is not on the DirectX12 backend. |
| `Dispose()` | Releases the Skia context and any target resources still waiting to be freed. |

`SkiaRenderer.Initialize(GraphicsDevice)` and `new SkiaRenderTarget2D(...)` pick this backend on their own.

## Example

```csharp
using SkiaGameRendering;

var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
canvas.Begin();
canvas.Canvas.DrawCircle(100, 100, 100, paint);
canvas.End();
```

## Remarks

- Needs `MonoGame.Framework.Native` 3.8.6 or later, which added `GetNativeHandles()` and
  `RenderTarget2D.FromNativeHandle()`. The game also references `MonoGame.Runtime.Windows.DX12`, the
  package that carries the native `mgruntime.dll`; without it the app fails at startup with
  `DllNotFoundException: mgruntime`.
- `SKColorType.Rgba8888` (the default) and `SKColorType.Bgra8888` only.
- Each target is a resource this backend creates and wraps with `RenderTarget2D.FromNativeHandle`.
  MonoGame tracks the resource's state itself, so every draw after the first is wrapped in a pair of
  transitions that keep its record and the real state equal. That assumes the texture is sampled
  between two draws, which `SkiaRenderTarget2D.End()` does.
- `SkiaDirectCanvas` works here (`SkiaRenderer.SupportsDirectCanvas`) for the back buffer or one bound
  `RenderTarget2D`, single-sample, `SurfaceFormat.Color` or `Bgra32`. MonoGame exposes neither its current
  target's `ID3D12Resource` nor a way to submit what it has recorded, so the backend reads MonoGame's native
  objects by offset and cuts MonoGame's command list in two around each pass. That is only done for an
  `mgruntime.dll` whose layout has been verified (3.8.6); with any other build
  `SupportsDirectCanvas` is false and the constructor's `NotSupportedException` says which. Each pass costs a
  submission, so use one per frame, not one per widget, and do not draw with MonoGame between `Begin` and
  `End`. A multisampled back buffer or target is refused. See `SkiaGameRendering-Notes.md` section 12.
- Skia, this backend and MonoGame submit to one queue, and MonoGame exposes no lock for it: draw from
  the thread that runs `Draw`.
- A disposed target's resource is released a few draws later, since MonoGame may still have frames
  in flight that sample it.
