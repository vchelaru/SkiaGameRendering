# SkiaVulkanBackend

## Definition

`SkiaVulkanBackend` is the `SkiaBackend` for MonoGame's native DesktopVK (Vulkan) platform. Skia's
Vulkan backend draws on the same `VkDevice` and `VkQueue` MonoGame renders with, straight into an
image MonoGame then samples. No CPU readback and no reflection.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.DesktopVK` (prerelease on NuGet: `dotnet add package SkiaGameRendering.DesktopVK --prerelease`, because MonoGame's native platform is itself prerelease)

```csharp
public class SkiaVulkanBackend : SkiaBackend
```

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Initialize(GraphicsDevice)` | Reads the instance, physical device, device and queue from `GraphicsDevice.GetNativeHandles()` and creates Skia's Vulkan context on them. Throws if the device is not on the Vulkan backend. |
| `Dispose()` | Releases the Skia context and any images still waiting to be freed. |

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
  `RenderTarget2D.FromNativeHandle()`. The game also references the native runtime for its OS,
  `MonoGame.Runtime.Windows.Vulkan` or `MonoGame.Runtime.Linux.Vulkan`.
- Windows and Linux. SkiaSharp's macOS native build has no Vulkan backend, so MoltenVK is out.
- `SKColorType.Rgba8888` (the default) and `SKColorType.Bgra8888` only.
- Each target is a `VkImage` this backend allocates (`VkImageAllocator` in `Core.VK`) and wraps with
  `RenderTarget2D.FromNativeHandle`. MonoGame expects the image in `SHADER_READ_ONLY_OPTIMAL`, so it
  is moved there after every draw and back to `COLOR_ATTACHMENT_OPTIMAL` before the next one.
- Skia, this backend and MonoGame submit to one queue, and MonoGame exposes no lock for it: draw from
  the thread that runs `Draw`.
- A disposed target's image is destroyed a few draws later, since MonoGame may still have frames in
  flight that sample it.
