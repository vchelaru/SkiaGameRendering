# SkiaDx12Backend

## Definition

`SkiaDx12Backend` is the `SkiaBackend` for MonoGame's native WindowsDX12 (D3D12) platform. Skia's
Direct3D backend draws on the same `ID3D12Device` and `ID3D12CommandQueue` MonoGame renders with,
straight into a texture MonoGame then samples. No CPU readback and no reflection.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.WindowsDX12`

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

- Needs `MonoGame.Framework.Native` 3.8.6-preview.2 or later, which added `GetNativeHandles()` and
  `RenderTarget2D.FromNativeHandle()`. The game also references `MonoGame.Runtime.Windows.DX12`, the
  package that carries the native `mgruntime.dll`; without it the app fails at startup with
  `DllNotFoundException: mgruntime`.
- `SKColorType.Rgba8888` (the default) and `SKColorType.Bgra8888` only.
- Each target is a resource this backend creates and wraps with `RenderTarget2D.FromNativeHandle`.
  MonoGame tracks the resource's state itself, so every draw after the first is wrapped in a pair of
  transitions that keep its record and the real state equal. That assumes the texture is sampled
  between two draws, which `SkiaRenderTarget2D.End()` does.
- Skia, this backend and MonoGame submit to one queue, and MonoGame exposes no lock for it: draw from
  the thread that runs `Draw`.
- A disposed target's resource is released a few draws later, since MonoGame may still have frames
  in flight that sample it.
