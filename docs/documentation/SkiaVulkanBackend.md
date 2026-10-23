# SkiaVulkanBackend

## Definition

`SkiaVulkanBackend` is the `SkiaBackend` for MonoGame's native DesktopVK (Vulkan) platform. Skia's
Vulkan backend draws on the same `VkDevice` and `VkQueue` MonoGame renders with, straight into an
image MonoGame then samples. No CPU readback and no reflection.

> **Stay on `SkiaGameRendering.DesktopVK` 0.17.4-preview.** It is built on SkiaSharp 3.119.4 and is not
> republished with the other packages. SkiaSharp 4.x needs Vulkan 1.1, but MonoGame creates its
> `VkInstance` at 1.0, and AMD and Mesa drivers then withhold the core 1.1 device functions Skia loads
> (NVIDIA does not), so `GRContext.CreateVulkan` fails there. Tracked in
> [MonoGame#9588](https://github.com/MonoGame/MonoGame/issues/9588). The source here already asks Skia for 1.1, so republishing needs no code change
> once MonoGame is fixed. An app using 0.17.4-preview cannot also use libraries that need SkiaSharp 4.x
> (Topten.RichTextKit 0.6.170, for one); NuGet reports the conflict at restore.

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

- Needs `MonoGame.Framework.Native` 3.8.6 or later, which added `GetNativeHandles()` and
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
