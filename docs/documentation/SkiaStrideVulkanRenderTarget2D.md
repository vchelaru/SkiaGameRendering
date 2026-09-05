# SkiaStrideVulkanRenderTarget2D

## Definition

`SkiaStrideVulkanRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It mirrors
`SkiaStrideRenderTarget2D`'s Begin/Canvas/End shape via Vulkan (the interop `SkiaGameRendering.Core.VK`
provides), but is a standalone class: like the D3D11 Stride adapter, it does **not** go through
`SkiaBackend`/`SkiaRenderer` - Stride has neither `Texture2D` nor `GraphicsDevice` typed the way
MonoGame's are, so `src/SkiaGameRendering.Stride.VK/` is a hand-rolled sibling typed to Stride's own
`Texture`/`GraphicsDevice` throughout, duplicated from (not shared with) the D3D11 adapter -
see `SkiaStrideRenderTarget2D.md` for why this repo duplicates rather than generalizes across
Stride's two graphics APIs.

Namespace: `SkiaGameRendering.Stride.VK`

Assembly/package: `SkiaGameRendering.Stride.VK`

```csharp
public sealed class SkiaStrideVulkanRenderTarget2D : IDisposable
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaStrideVulkanRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888)` | Auto-initializes `SkiaStrideVulkanRenderer` on first use and allocates a fixed-size target. |

## Members

| Member | Description |
| --- | --- |
| `Texture` | The Stride `Texture`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Acquires Stride's Vulkan queue lock so Skia's and Stride's `vkQueueSubmit` calls stay externally synchronized. |
| `End(GraphicsContext graphicsContext)` | Flushes Skia's queued GPU work and submits it to the shared `VkQueue` (releasing the queue lock), then composites the whole surface at native size and the origin via a `SpriteBatch`. Needs a `GraphicsContext` (unlike the other backends' parameterless `End()`) because Stride's `SpriteBatch` requires one - normally `RenderDrawContext.GraphicsContext` from inside a `SceneRendererBase.DrawCore` (see `SkiaStrideVulkanSceneRenderer` below). |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the texture and Skia surface. Must not be called between `Begin`/`End`. |

## Related types

| Type | Role |
| --- | --- |
| `SkiaStrideVulkanRenderer` | Static holder for the shared `SkiaStrideVulkanContext`, mirroring `SkiaStrideRenderer`. `Initialize(GraphicsDevice)` is optional - call it explicitly only to fail fast on Vulkan handle setup instead of lazily on first render target. `Dispose()` releases it. |
| `SkiaStrideVulkanSceneRenderer` | A `SceneRendererBase` you add to Stride's `GraphicsCompositor` (e.g. via the Stride Community Toolkit's `Game.AddSceneRenderer`). Stride drives rendering through the compositor rather than a user-owned `Draw()` call, so this is the hook that runs `Begin`/`End` every frame - set its `Canvas` and subscribe to its `SkiaDraw` event. |

## Example

```csharp
using SkiaGameRendering.Stride.VK;
using SkiaSharp;

var canvas = new SkiaStrideVulkanRenderTarget2D(game.GraphicsDevice, 200, 200);
var renderer = new SkiaStrideVulkanSceneRenderer { Canvas = canvas };
renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
game.AddSceneRenderer(renderer);
```

## Remarks

- Requires Stride 4.4.0-beta5 or newer, built with `StrideGraphicsApi=Vulkan` (Stride's default on
  Linux/macOS; must be forced explicitly on Windows - see `docs/stride/vulkan-quickstart.md`).
- Reaches Stride Vulkan internals via reflection: `GraphicsDevice.NativeInstance`/
  `NativePhysicalDevice`/`NativeDevice` (properties) and `NativeCommandQueue`/`QueueLock` (fields),
  plus `Texture.NativeImage`/`NativeLayout`/`NativeFormat` (fields) - all `internal` in Stride's
  `GraphicsDevice.Vulkan.cs`/`Texture.Vulkan.cs`. Every one is pinned by
  `tests/Tests.Stride.VK/StrideVulkanReflectionTests.cs`.
- Unlike the D3D11 adapter, there is no separate GL-style context to bind/unbind: Skia's Vulkan
  backend draws directly against Stride's shared `VkDevice`/`VkQueue`, so `Begin`/`End` only need to
  bracket the queue lock (`SkiaStrideVulkanContext.BeginDraw`/`EndDraw`), not a context switch.
- Skia's Vulkan backend requires `VK_IMAGE_USAGE_TRANSFER_SRC_BIT`/`_DST_BIT` on any wrapped image.
  Stride's own `Texture.Vulkan.cs` sets both unconditionally on every image it creates, so this
  adapter needs no extra texture-creation flags at the Stride API level to satisfy that requirement -
  see `SkiaStrideVulkanContext`'s doc comment for the full analysis (repo issue #54).
- See also the [Stride Vulkan quick start](../stride/vulkan-quickstart.md).
