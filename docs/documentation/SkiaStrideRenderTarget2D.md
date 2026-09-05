# SkiaStrideRenderTarget2D

## Definition

`SkiaStrideRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It mirrors
`SkiaRenderTarget2D`'s Begin/Canvas/End shape via ANGLE/D3D11 (the same interop
`SkiaAngleBackend`/`SkiaKniAngleBackend` use), but is a standalone class: Stride support does
**not** go through `SkiaBackend`/`SkiaRenderer`. Those types are typed directly to MonoGame's
`Texture2D`/`GraphicsDevice`, and Stride has neither type, so `src/SkiaGameRendering.Stride.D3D11/` is a
hand-rolled sibling typed to Stride's own `Texture`/`GraphicsDevice` throughout - the same choice
`SkiaRaylibRenderTarget2D` made for raylib.

Namespace: `SkiaGameRendering.Stride.D3D11`

Assembly/package: `SkiaGameRendering.Stride.D3D11`

```csharp
public sealed class SkiaStrideRenderTarget2D : IDisposable
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaStrideRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888)` | Auto-initializes `SkiaStrideRenderer` on first use and allocates a fixed-size target. |

## Members

| Member | Description |
| --- | --- |
| `Texture` | The Stride `Texture`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Switches to the ANGLE GL context and binds this target's FBO. |
| `End(GraphicsContext graphicsContext)` | Flushes Skia's queued GPU work, switches back to Stride's D3D11 state, and composites the whole surface at native size and the origin via a `SpriteBatch`. Needs a `GraphicsContext` (unlike the other backends' parameterless `End()`) because Stride's `SpriteBatch` requires one - normally `RenderDrawContext.GraphicsContext` from inside a `SceneRendererBase.DrawCore` (see `SkiaStrideSceneRenderer` below). |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the texture and Skia surface. Must not be called between `Begin`/`End`. |

## Related types

| Type | Role |
| --- | --- |
| `SkiaStrideRenderer` | Static holder for the shared `SkiaStrideContext`, mirroring `SkiaRenderer`/`SkiaRaylibRenderer`. `Initialize(GraphicsDevice)` is optional - call it explicitly only to fail fast on ANGLE/EGL setup instead of lazily on first render target. `Dispose()` releases it. |
| `SkiaStrideSceneRenderer` | A `SceneRendererBase` you add to Stride's `GraphicsCompositor` (e.g. via the Stride Community Toolkit's `Game.AddSceneRenderer`). Stride drives rendering through the compositor rather than a user-owned `Draw()` call, so this is the hook that runs `Begin`/`End` every frame - set its `Canvas` and subscribe to its `SkiaDraw` event. |

## Example

```csharp
using SkiaGameRendering.Stride;
using SkiaSharp;

var canvas = new SkiaStrideRenderTarget2D(game.GraphicsDevice, 200, 200);
var renderer = new SkiaStrideSceneRenderer { Canvas = canvas };
renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
game.AddSceneRenderer(renderer);
```

## Remarks

- Windows and D3D11 only. For Vulkan (Windows/Linux/macOS), see `SkiaGameRendering.Stride.VK` /
  [SkiaStrideVulkanRenderTarget2D](SkiaStrideVulkanRenderTarget2D.md) instead.
- Requires Stride 4.4.0-beta5 or newer. Stride's D3D11 backend became Silk.NET (from SharpDX) in
  this release, which is what makes `GraphicsDevice.NativeDevice` a public
  `ComPtr<ID3D11Device>` - the current stable release (4.3.0.2507) still wraps D3D11 in SharpDX and
  keeps `NativeDevice` non-public. This is a prerelease dependency; expect it to move as Stride 4.4
  stabilizes.
- Reaches exactly one Stride internal via reflection: `GraphicsResourceBase.nativeResource` (a
  private `ID3D11Resource*` field) - the public `NativeResource` property wrapping it is `protected
  internal`, visible to a subclass but not to this adapter. Everything else (`NativeDevice`,
  `TextureFlags.RenderTarget`) is public. Compare to `SkiaAngleBackend`'s three reflected MonoGame
  fields.
- Stride's immediate D3D11 context isn't public either, but rather than reflect it, the adapter asks
  the device for it directly over COM (`ID3D11Device::GetImmediateContext`, vtable slot 40 - see
  `SkiaGameRendering.Core.ANGLE`'s `D3D11Com`).
- Unlike `SkiaAngleBackend`'s MonoGame `Texture2D` (which needs a `RenderTarget2D` substitution plus
  a one-time `SetData` call to force lazy D3D11 allocation), Stride's `Texture.New2D` takes
  `TextureFlags.RenderTarget` directly and allocates the D3D11 resource eagerly.
- See also the [Stride quick start](../stride/quickstart.md).
