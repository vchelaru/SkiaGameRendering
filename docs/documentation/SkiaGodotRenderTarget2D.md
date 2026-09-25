# SkiaGodotRenderTarget2D

## Definition

`SkiaGodotRenderTarget2D` is a GPU texture that SkiaSharp renders into and Godot displays like any
other `Texture2D`. It mirrors the other engines' Begin/Canvas/End shape, over `SkiaGameRendering.Core.VK`
when Godot runs on Vulkan, `SkiaGameRendering.Core.D3D12` on D3D12, and `SkiaGameRendering.Core.OGL`
on the Compatibility renderer (the backend is chosen at run time from
`RenderingServer.GetCurrentRenderingDriverName()`). Like the raylib and
Stride adapters it is a standalone class: it does **not** go through `SkiaBackend`/`SkiaRenderer`,
which are typed to MonoGame's `Texture2D`/`GraphicsDevice`. In Godot the scene tree draws the
texture, so unlike the other adapters there is no composite step - `End()` only submits.

Namespace: `SkiaGameRendering.Godot`

Assembly/package: `SkiaGameRendering.Godot`

```csharp
public sealed class SkiaGodotRenderTarget2D : IDisposable
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaGodotRenderTarget2D(int width, int height, SKColorType colorType = SKColorType.Rgba8888)` | Auto-initializes `SkiaGodotRenderer` on first use and allocates a fixed-size texture. Must be called on the render thread (the main thread under the default "Safe" thread model). On the RenderingDevice drivers it stalls the GPU once, briefly, to hand the texture to Godot in a known state - create targets up front. The Compatibility renderer accepts `Rgba8888` only. |

## Members

| Member | Description |
| --- | --- |
| `Texture` | A `Texture2D` (a `Texture2DRD` viewing the RD texture; an `ImageTexture` on the Compatibility renderer) to assign to a `Sprite2D`, `TextureRect`, material, etc. Updates in place; nothing needs re-assigning after `End()`. |
| `TextureRid` | The `RenderingDevice` texture RID, for **fragment-shader sampling** at the RD level (e.g. a `SamplerWithTexture` uniform in your own shader). Owned by this object; do not free it, copy to/from it, clear it, or bind it as a storage image - Godot would move it out of the sampled state this object keeps it in. Invalid on the Compatibility renderer. |
| `Width`, `Height` | The fixed size given to the constructor. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`. |
| `Begin(bool clear = true)` | Starts a render pass; with `clear` false the previous contents are kept. Throws off the render thread. |
| `End()` | Submits Skia's GPU work and hands the texture back in the state Godot expects (Vulkan: a layout barrier; D3D12: a `CopyResource` into Godot's texture with barriers around it; OpenGL: just a flush on the shared context), without waiting on the GPU. No composite. |
| `Dispose()` | Frees Skia's resources. On the RenderingDevice drivers it also detaches the `Texture2DRD` view and frees the RD texture, so nodes still showing `Texture` go blank; on the Compatibility renderer the `ImageTexture` keeps its last contents. Must not be called between `Begin`/`End`. `SkiaGodotRenderer.Dispose()` disposes every target still alive. Under the "Separate" thread model the GPU half is handed to the render thread and completes a frame or so later. |
| `static CreatePremultipliedAlphaMaterial()` | A `CanvasItemMaterial` with `BlendMode = PremultAlpha`, matching Skia's premultiplied output. Assign to the displaying node when the canvas has transparent areas. |

## Related types

| Type | Role |
| --- | --- |
| `SkiaGodotRenderer` | Static holder for the shared backend, mirroring `SkiaRaylibRenderer`/`SkiaStrideVulkanRenderer`. `Initialize(RenderingDevice? = null)` is optional - call it to fail fast on an unsupported driver. `Driver` reports `"vulkan"`, `"d3d12"` or `"opengl3"`, `IsZeroCopy` whether Skia draws straight into Godot's texture, `D3D12UsesEnhancedBarriers` which state-tracking mode Godot's D3D12 device runs in. `Dispose()` releases the backend and every target still alive on it; a target used afterward throws `ObjectDisposedException`. `Initialize` accepts only Godot's global `RenderingDevice`. |
| `SkiaGameRendering.Core.VK.VkImageLayoutTransitioner` | Added for this adapter: queues `vkCmdPipelineBarrier` layout transitions on the host's queue through a ring of command buffers, resolving its entry points through `vkGetDeviceProcAddr`. Any Vulkan host whose engine tracks image layouts needs it. |
| `SkiaGameRendering.Core.D3D12.D3D12ResourceTransitioner` | Its D3D12 twin: queues resource-state transitions and a `CopyResource` bracketed by transitions through a ring of command lists, over raw COM vtables. |

## Example

```csharp
using SkiaGameRendering.Godot;
using SkiaSharp;

var skia = new SkiaGodotRenderTarget2D(512, 512);
AddChild(new Sprite2D { Texture = skia.Texture, Centered = false });

// every frame:
skia.Begin();
skia.Canvas.DrawCircle(256, 256, 200, paint);
skia.End();
```

## Remarks

- **No reflection.** Every handle comes from public API: `RenderingServer.GetRenderingDevice()`
  and `RenderingDevice.GetDriverResource(...)` - `TopmostObject`/`PhysicalDevice`/`LogicalDevice`/
  `CommandQueue`/`QueueFamily` for the `VkInstance`/`VkPhysicalDevice`/`VkDevice`/`VkQueue`/queue
  family on Vulkan, `PhysicalDevice`/`LogicalDevice`/`CommandQueue` for the `IDXGIAdapter1`/
  `ID3D12Device`/`ID3D12CommandQueue` on D3D12, and `Texture`/`TextureDataFormat` for an RD texture's
  native handle and format. A Godot version bump breaks this at compile time, not at runtime, so
  there is no reflection pin test.
- **Godot tracks image layouts and resource states itself, and this adapter keeps both sides
  truthful.** Godot's render graph derives a texture's layout from the last usage it recorded and
  emits exactly one barrier for a texture it only samples, then no more. SkiaSharp 3.119.4 cannot
  be asked which layout or state to leave a resource in. So the constructor runs a one-triangle
  fragment-shader pass that samples the texture and flushes the render graph, so Godot's single
  transition (whose old layout is `UNDEFINED`, which the spec allows to discard contents) happens
  before the texture holds anything, and after every `End()` the backend returns the texture to
  exactly that state. On Vulkan, `End()` queues a `COLOR_ATTACHMENT_OPTIMAL -> SHADER_READ_ONLY_OPTIMAL`
  barrier, `Begin()` re-wraps the `SKSurface` each frame with that layout as Skia's starting point
  (Skia caches the last layout it set and would otherwise skip its own transition), and `Begin()`
  records a no-op 1x1 draw so even a frame with no other Skia work executes a render pass. On
  D3D12, Skia's own resource stays in `RENDER_TARGET` and the per-frame copy transitions Godot's
  texture from and back to the state Godot believes: `PIXEL_SHADER_RESOURCE` on Godot's legacy
  state-tracking path, `ALL_SHADER_RESOURCE` (the legacy equivalent of `D3D12_BARRIER_LAYOUT_SHADER_RESOURCE`)
  when Godot runs with enhanced barriers, decided the same way Godot decides it
  (`D3D12_FEATURE_D3D12_OPTIONS12.EnhancedBarriersSupported`). Confirmed clean under Godot's
  `--gpu-validation` on both RenderingDevice drivers. See `SkiaGodotBackend` and its implementations for the
  source-level trail.
- **The Compatibility renderer** has no `RenderingDevice`, so its backend is the raylib adapter's
  shape instead: a second native GL context (WGL on Windows, GLX on Linux X11 - the raylib
  adapter's own platform code, linked in) shares Godot's GL object namespace, and Skia draws
  through an FBO wrapped around the GL texture behind an ordinary `ImageTexture`
  (`RenderingServer.TextureGetNativeHandle`), with `GRSurfaceOrigin.TopLeft` so Skia's canvas row 0
  lands in texel row 0, which is where Godot puts an image's top row (the raylib adapter needs the
  opposite origin; the scenario suite's asymmetric layouts are what told the two apart). Separate contexts mean Godot's heavily cached GL state and Skia's
  never meet; synchronization is GL's shared-object rule (writer flushes, reader binds). No
  layouts, no priming, no hand-back, a persistent surface. Godot's other GL flavors
  (`opengl3_angle`, `opengl3_es`, Wayland, macOS) are EGL/NSOpenGL contexts and are rejected with
  a message.
- **Why D3D12 copies.** Godot's D3D12 driver allocates every texture with its typeless family
  format so it can create UNORM and sRGB views of it, and Skia's D3D12 backend creates its
  render-target view with a null descriptor, which D3D12 rejects for a typeless resource. Skia
  therefore renders into a typed resource this library allocates and one `CopyResource` per frame
  (same typeless family, GPU-to-GPU) lands it in Godot's texture.
- **Texture creation.** The RD texture is created with `SamplingBit | CanCopyFromBit | CanCopyToBit`
  (plus `ColorAttachmentBit` on Vulkan, where Skia renders into it; the copy bits map to the
  `TRANSFER_SRC`/`TRANSFER_DST` usage Skia's Vulkan backend insists on) and declares its UNORM and
  sRGB formats as shareable, because `Texture2DRD` creates an sRGB view of the image, which on
  Vulkan is only legal on an image created with `VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT` (found via
  `--gpu-validation`, VUID 01762).
- **Threading.** Under the default "Safe" thread model Godot renders on the main thread and
  submits once per frame after `_Process`, so Skia's submits from `_Process` are serialized with
  Godot's by construction; no queue lock is needed. Under "Separate" (experimental in Godot),
  construct, `Begin`/`End` and `SkiaGodotRenderer.Initialize` inside
  `RenderingServer.CallOnRenderThread`; `Dispose` may be called from either thread and splits
  itself across both. Verified against `--render-thread separate`.
- **No CPU stall per frame.** `End()` submits without waiting for the GPU
  (`EndDraw(synchronous: false)` on either Core factory): Godot samples the texture in its own frame
  submit on the same queue, which is queue-ordered behind Skia's work, so no fence wait is needed for
  correctness, and the hand-back submissions go through a ring of command buffers that grows instead of waiting
  when every buffer is still in flight. The one
  place that does wait is `Dispose`, before Godot frees the texture.
- **Color.** The texture is plain UNORM with no Skia color-space tag: Godot's default gamma-space
  2D pipeline displays Skia's sRGB bytes 1:1 (the sample's pure red and CornflowerBlue read back
  exactly on all three drivers). HDR 2D projects are not compensated for.
- See also the [Godot quick start](../godot/quickstart.md).
