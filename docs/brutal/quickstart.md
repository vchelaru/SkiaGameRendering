# BRUTAL quickstart

[BRUTAL](https://github.com/ahwootech/BRUTAL) ships Vulkan bindings and no renderer or texture type, so
`SkiaGameRendering.Brutal` gives you a Vulkan image that Skia draws into on the GPU. You sample it in your
own renderer. Windows, Linux and macOS ([below](#macos)), Vulkan 1.3, no CPU readback.

```
dotnet add package SkiaGameRendering.Brutal
```

The package takes raw handles and has no dependency on any BRUTAL package. BRUTAL's wrappers expose them as
`VkHandle`.

```csharp
var context = new SkiaVulkanContext(
    instance.VkHandle, physicalDevice.VkHandle, device.VkHandle, queue.VkHandle, (uint)queue.Family);
var target = new SkiaRenderTarget2D(context, 512, 512);   // Rgba8888 or Bgra8888

target.Begin();
target.Canvas.DrawCircle(256, 256, 200, paint);
target.End();

// Create your own VkImageView over target.VkImage (format target.VkFormat) and sample it.
```

`samples/Sample.Brutal` is a complete headless example, and `samples/Sample.Brutal.Quad` is a BRUTAL renderer
that samples the image on a quad each frame. `--smoke-test` draws, reads one pixel back through
Vulkan and exits 0 if it is right.

## Contracts

- **Layout.** The image is in `SHADER_READ_ONLY_OPTIMAL` whenever you own it: after construction and after
  `End()`. `Begin()` moves it to `COLOR_ATTACHMENT_OPTIMAL` and `End()` moves it back, each as a small submit
  on the context's queue. No barrier is needed before sampling on that queue. For another queue, call
  `End(synchronous: true)` and handle queue ownership yourself.
- **App-managed layout.** `new SkiaRenderTarget2D(context, w, h, appManagesLayout: true)` makes the library record
  no transitions. The image stays in `COLOR_ATTACHMENT_OPTIMAL` between Skia passes, and you record
  COLOR_ATTACHMENT_OPTIMAL to SHADER_READ_ONLY_OPTIMAL before your sampling and back after it, in your own
  command buffer. `samples/Sample.Brutal.Quad` does this (and the library-managed mode) with a BRUTAL dynamic
  rendering pass that samples `VkImage` on a textured quad every frame.
- **Queue lock.** Skia calls `vkQueueSubmit` on the queue you pass. BRUTAL's `Queue.Submit` holds a private
  lock that this package cannot share, so if several threads submit to that queue, take your own lock around
  your submits and pass it as `acquireQueueLock` (it returns an `IDisposable` that releases it). Single-threaded
  rendering needs nothing.
- **Image.** Device-local, single sample, usage `TRANSFER_SRC | TRANSFER_DST | SAMPLED | COLOR_ATTACHMENT`,
  one dedicated allocation per target. Dispose only after the GPU is done with it.
- **Multisampling.** `new SkiaRenderTarget2D(context, w, h, multiSampleCount: 4)` (2, 4 or 8) has Skia draw into a
  second, multisampled image the target owns. `End()` resolves it into `VkImage` with `vkCmdResolveImage`, in the
  submit that would otherwise move the layout, so `VkImage` stays single-sample and both layout modes keep their
  contracts. `Begin()` then submits nothing. Any other count, or one the GPU lacks, throws `NotSupportedException`.
  See [Multisampling](../documentation/SkiaRenderTarget2D.md#multisampling).
- **Color types.** `Rgba8888` or `Bgra8888`; others throw `NotSupportedException`.
- **Color space.** The image is plain UNORM and holds the sRGB bytes you draw. If your pass writes to an sRGB
  target without decoding the sampled value first, colors are encoded twice. Stride has the same issue; see
  its Vulkan quickstart.

### SkiaDirectCanvas (Vulkan)

To draw straight into an image you already have, typically the swapchain image, with no intermediate image and no sampling
pass, use `SkiaDirectCanvas`. Same `Begin()`/`Canvas`/`End()` shape. Vulkan has no bound-target query and BRUTAL has no renderer
type, so `Begin` takes the target:

```csharp
var direct = new SkiaDirectCanvas(context);

// each frame, after submitting your own drawing into the image on the context's queue:
direct.Begin(swapchainImage, swapchainFormat, width, height, currentLayout);
direct.Canvas.DrawCircle(256, 256, 200, paint);
direct.End(finalLayout: presentSrcLayout);   // omit to hand it back in the layout it came in
// then submit your later work / present
```

- **Layout.** `Begin` moves the image from `currentLayout` (`UNDEFINED` if you don't need its contents) to
  `COLOR_ATTACHMENT_OPTIMAL`; `End` moves it to `finalLayout` or back to where it came from. Both are small submits whose barriers
  cover all earlier commands on the queue.
- **Submit order.** Skia submits inside `End`, not into your command buffer. Submit what draws into the image before `Begin` and what
  reads it after `End`; `End(synchronous: true)` blocks, for another queue or the CPU.
- **Image.** `COLOR_ATTACHMENT` usage (checked against the `imageUsage` you pass, default `COLOR_ATTACHMENT | TRANSFER_SRC |
  TRANSFER_DST`; Skia insists on the transfer bits when wrapping, and drawing doesn't use them), the context's queue family,
  8-bit RGBA or BGRA. Anything else throws `NotSupportedException` or `ArgumentException` before anything is submitted.
- **No MSAA.** Pass the image's `sampleCount`; anything but 1 throws `NotSupportedException`.
- **sRGB.** Skia cannot wrap an sRGB `VkImage`, so an sRGB-format image is wrapped as its UNORM twin and Skia writes the bytes as
  given. If the image lacks `MUTABLE_FORMAT`, the validation layer reports it; drivers accept it for same-size formats. See
  [SkiaDirectCanvas](../documentation/SkiaDirectCanvas.md).
- **Cost.** Three queue submits per pass and a Skia surface wrapped per pass. Use it once per frame. The queue-lock contract above applies.

`Sample.Brutal --smoke-test` and `Sample.Brutal.Quad --smoke-test` draw the shared scene through it into an app-owned image and compare it
with the texture path. The `Tests.Brutal` layout tests check the layouts under the validation layer in CI.

## macOS

BRUTAL's Vulkan runs on Apple silicon through MoltenVK. SkiaSharp's macOS native has no Vulkan, so there Skia draws with
Metal into the `MTLTexture` behind each target's `VkImage`, on the `MTLCommandQueue` behind your queue. Everything still
stays on the GPU, and the API is the same.

Install the Vulkan loader and MoltenVK (BRUTAL ships neither for macOS; see the
[BRUTAL macOS notes](https://brutally-honest.junk-tools.com/)):

```
brew install molten-vk vulkan-loader
brew install vulkan-validationlayers vulkan-tools   # optional: validation, and vulkaninfo
```

Your app adds two things: `VK_EXT_metal_objects` on the device, and a `VkExportMetalObjectCreateInfoEXT` for the
MTLDevice and one for the MTLCommandQueue on the instance. Without them the export fails validation or
`SkiaVulkanContext` throws, naming the extension.

```csharp
var exportQueue = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalCommandQueue);
var exportDevice = VkMetalObjectExport.ExportMetalObjectCreateInfo.For(VkMetalObjectExport.ExportObjectTypeMetalDevice, (IntPtr)(&exportQueue));
var instance = Vulkan.CreateInstance(new Instance.CreateInfo
{
    // ... as before, plus portability enumeration
    Flags = VkInstanceCreateFlags.EnumeratePortabilityBitKHR,
    Next = &exportDevice,
});
var device = physicalDevice.CreateDevice(new Device.CreateInfo
{
    Extensions = ["VK_KHR_portability_subset", VkMetalObjectExport.ExtensionName],
    // ...
});
```

`VkMetalObjectExport` is in `SkiaGameRendering.Core.VK`, which the package brings in. `samples/Sample.Brutal` does this.

- **Synchronous submits.** Skia's Metal command buffers and your Vulkan submits are ordered by the order they reach the
  shared `MTLCommandQueue`. That holds because MoltenVK commits each `vkQueueSubmit` on the calling thread, its default
  (`MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS=1`). `SkiaVulkanContext` throws if that variable is `0`. Don't turn it off
  through `VK_EXT_layer_settings` either; the library can't see that.
- **Layout.** A `SkiaRenderTarget2D` image moves to `SHADER_READ_ONLY_OPTIMAL` once at creation and never again, since Skia's writes go
  through Metal, which has no layouts. The contract you see is unchanged. With `appManagesLayout: true` it is moved to
  `COLOR_ATTACHMENT_OPTIMAL` once instead, so your barriers stay valid; they are bookkeeping for MoltenVK.
- **`SkiaDirectCanvas`** exports the image's `MTLTexture` at every `Begin`. For a swapchain image that is the current
  drawable's texture, so call `Begin` after `vkAcquireNextImageKHR`, and `End` before `vkQueuePresentKHR`, as on
  Windows and Linux. Two rules apply on macOS only:
  - Create the swapchain with `VK_IMAGE_USAGE_TRANSFER_SRC_BIT` (or `SAMPLED` or `STORAGE`) in `imageUsage`. Without
    one, MoltenVK makes its textures `framebufferOnly`. Skia's Metal backend has no framebuffer fetch, so a blend mode
    that reads the destination copies from the texture, which Metal forbids there. `Begin` throws on such a texture.
  - Exporting a swapchain image's texture breaks the Vulkan spec. A texture may only be exported from an image created
    with `VkExportMetalObjectCreateInfoEXT`, and a swapchain image can't be. The validation layer reports
    `VUID-VkExportMetalObjectsInfoEXT-pNext-06795`; MoltenVK 1.4 exports the texture anyway. An image you create
    yourself with that struct chained (as `samples/Sample.Brutal` does) breaks nothing.
- **Not yet.** `multiSampleCount` above 0 throws on macOS, and `SkiaDirectCanvas` takes single-sample images only.

## OpenGL

BRUTAL also ships OpenGL bindings (`Ahwoo.BRUTAL.OpenGl`) next to its GLFW and SDL windowing packages. For an app on
an OpenGL 3.3+ context use `SkiaGameRendering.Brutal.OGL`, which draws into a `GL_TEXTURE_2D`. Windows, Linux and
macOS. macOS caps OpenGL at 4.1 core, and Apple has deprecated OpenGL. macOS is verified on a real Mac, not in CI:
GitHub's macOS runners have no hardware OpenGL renderer, so GLFW can't create a context there.

```
dotnet add package SkiaGameRendering.Brutal.OGL
```

Create the context while your GL context is current, passing the window library's proc-address function:

```csharp
var context = new SkiaGlContext(name => Glfw.GetProcAddress(Encoding.ASCII.GetBytes(name + "\0")));
var target = new SkiaRenderTarget2D(context, 512, 512);

target.Begin();
target.Canvas.DrawCircle(256, 256, 200, paint);
target.End();

// Bind target.GlTexture and draw with it.
```

- **Same context.** Skia draws on your GL context, not a second one, so create, `Begin`, `End` and dispose all run on the
  thread that owns it with it current. Dispose targets before the `SkiaGlContext`.
- **State.** The constructor, `Begin()` and `End()` restore only your draw framebuffer binding (and the constructor your
  texture binding); every other piece of GL state is undefined after them. Skia can leave `GL_SCISSOR_TEST` on after a
  clip, so a `glBlitFramebuffer` or `glClear` that does not turn it off touches only that rectangle: set whatever your
  own GL calls rely on before making them.
- **Texture.** `GL_RGBA8`, linear filtering, clamped, premultiplied alpha; canvas row 0 is texel row 0. Only `Rgba8888`.
- **Multisampling.** `new SkiaRenderTarget2D(context, w, h, multiSampleCount: 4)` (2, 4 or 8) has Skia draw into a
  multisampled buffer it owns, and `End()` resolves it into `GlTexture` on the GPU, which stays single-sample. Any other
  count, or one the GPU lacks, throws `NotSupportedException`. See
  [Multisampling](../documentation/SkiaRenderTarget2D.md#multisampling).
- **Package.** It has no dependency on any BRUTAL package.

### SkiaDirectCanvas

To skip the texture and draw into the framebuffer you already have bound (the back buffer, or an FBO of your own), use
`SkiaDirectCanvas`. Same `Begin()`/`Canvas`/`End()` shape:

```csharp
var direct = new SkiaDirectCanvas(context);

// each frame, after your own drawing:
direct.Begin();
direct.Canvas.DrawCircle(256, 256, 200, paint);
direct.End();
// set your GL state again, draw more, swap buffers
```

- **Size.** The current GL viewport. Set it to the whole target before `Begin()`; a partial viewport is treated as a smaller target.
- **No clear.** `Begin()` draws on top of what is there. Canvas row 0 is the top as a window shows it (the last texel row of an FBO texture).
- **Targets.** The back buffer is assumed to be RGBA8. An FBO's color attachment must be a texture or renderbuffer, else `NotSupportedException`.
  An FBO's Skia framebuffer is rebuilt every pass. Multisampled targets are passed to Skia with their sample count and are untested here.
- **State.** `End()` restores only your draw framebuffer binding; the rest of the GL state is undefined, scissor test
  included, like the texture target.

`Sample.Brutal.OGL --direct` shows it in a window, and `--smoke-test` checks it against the texture render.

`samples/Sample.Brutal.OGL` is a complete example on a hidden GLFW window; `--smoke-test` reads one pixel back with
`glReadPixels` and exits 0 if it is right. `Tests.Brutal.OGL` runs the shared scene against a golden image in CI.

## BRUTAL license

BRUTAL's license is MIT plus attribution terms for the end application: credits notice, its logo before
gameplay, and shipping `NOTICE.txt`. That applies to apps built on BRUTAL, not to this package.

## Measured

OpenGL, `benchmarks/Benchmarks.Brutal.OGL` (hidden GLFW window, never presented, 1920x1080, random solid-color rects on black). "Texture" is
`SkiaRenderTarget2D` Begin/draw/End plus an app fullscreen-triangle pass sampling it onto the back buffer; "Direct" is
`SkiaDirectCanvas` into the back buffer. CPU is a stopwatch around the frame; throughput is the whole loop plus `glFinish`, divided by frames.
GPU: NVIDIA GeForce RTX 5060 Laptop GPU, GL 3.3.0 NVIDIA 610.47, Windows 11. Warmup 30, frames 200, one run per row.

| Load | Path | CPU ms/frame | Throughput ms/frame |
|---|---|---|---|
| 1 rect | Texture + fullscreen draw | 0.102 | 0.106 |
| 1 rect | Direct | 0.035 | 0.036 |
| 1000 rects | Texture + fullscreen draw | 1.672 | 1.677 |
| 1000 rects | Direct | 1.115 | 1.121 |

On this machine only: direct saves about 0.07 ms of fixed cost per frame (the 1-rect rows) and about 0.55 ms at 1000 rects.
The two halves of each 1000-rect run differ by up to 25%, so read that gap as roughly 0.4 to 0.7 ms.

Vulkan, `benchmarks/Benchmarks.Brutal` (headless, no window) times `Begin()`, the draw calls and `End()` per frame.
"CPU" is a stopwatch around that span; "Throughput" is the whole loop plus a device wait, divided by frames. "Layout"
is who records the transitions: `library` (default, two small submits per frame) or `app` (`appManagesLayout: true`,
no consumer pass in this benchmark, so no barriers at all). Rects are random solid fills, paths a small cubic
shape, text 200 lines of 18 px default-typeface text.

GPU: NVIDIA GeForce RTX 5060 Laptop GPU (driverVersion 0x988BC000, API 1.4). OS: Microsoft Windows 10.0.26200. Warmup 30, frames 200.

| Size | Load | Layout | End | CPU ms/frame | Throughput ms/frame |
|---|---|---|---|---|---|
| 1920x1080 | clear only | library | async | 0.065 | 0.066 |
| 1920x1080 | clear only | library | sync | 0.206 | 0.206 |
| 1920x1080 | clear only | app | async | 0.027 | 0.027 |
| 1920x1080 | clear only | app | sync | 0.094 | 0.094 |
| 1920x1080 | 1000 rects | library | async | 0.875 | 0.877 |
| 1920x1080 | 1000 rects | library | sync | 0.957 | 0.958 |
| 1920x1080 | 1000 rects | app | async | 0.731 | 0.732 |
| 1920x1080 | 1000 rects | app | sync | 0.782 | 0.783 |
| 1920x1080 | 1000 paths | library | async | 1.467 | 1.471 |
| 1920x1080 | 1000 paths | library | sync | 1.933 | 1.934 |
| 1920x1080 | 1000 paths | app | async | 1.256 | 1.259 |
| 1920x1080 | 1000 paths | app | sync | 1.726 | 1.727 |
| 1920x1080 | text, 200 lines | library | async | 1.697 | 1.698 |
| 1920x1080 | text, 200 lines | library | sync | 2.185 | 2.186 |
| 1920x1080 | text, 200 lines | app | async | 1.362 | 1.364 |
| 1920x1080 | text, 200 lines | app | sync | 1.498 | 1.499 |
| 3840x2160 | clear only | library | async | 0.056 | 0.057 |
| 3840x2160 | clear only | library | sync | 0.201 | 0.202 |
| 3840x2160 | clear only | app | async | 0.023 | 0.024 |
| 3840x2160 | clear only | app | sync | 0.112 | 0.113 |
| 3840x2160 | 1000 rects | library | async | 0.833 | 0.835 |
| 3840x2160 | 1000 rects | library | sync | 0.933 | 0.934 |
| 3840x2160 | 1000 rects | app | async | 0.725 | 0.726 |
| 3840x2160 | 1000 rects | app | sync | 0.802 | 0.803 |
| 3840x2160 | 1000 paths | library | async | 1.327 | 1.330 |
| 3840x2160 | 1000 paths | library | sync | 2.017 | 2.019 |
| 3840x2160 | 1000 paths | app | async | 1.350 | 1.353 |
| 3840x2160 | 1000 paths | app | sync | 1.796 | 1.797 |
| 3840x2160 | text, 200 lines | library | async | 1.650 | 1.651 |
| 3840x2160 | text, 200 lines | library | sync | 1.622 | 1.623 |
| 3840x2160 | text, 200 lines | app | async | 1.484 | 1.485 |
| 3840x2160 | text, 200 lines | app | sync | 1.634 | 1.635 |

What this shows on this machine only: at these loads the 1080p and 4K numbers are close, so the cost here is
CPU-side Skia recording and submit rather than fill rate. `sync` adds roughly 0.1 to 0.5 ms per frame. The
library's own transitions cost about 0.04 to 0.4 ms per frame depending on load (compare `library` and `app` rows),
the larger gaps coming from `sync`. Numbers vary run to run by a few percent; one run per row.

- Windows 11, NVIDIA GeForce RTX 5060 Laptop GPU (driver 610.47), real driver: smoke test and `BrutalLayoutContractTests` pass.
- Linux, Mesa lavapipe with the Khronos validation layer and synchronization validation: the `brutal-linux` job
  in `.github/workflows/master.yml` runs the same smoke test and tests, failing on any VUID or SYNC-HAZARD message.
- Not measured: performance, multi-threaded queue use, AMD or Intel GPUs.

### Direct canvas vs render target (Vulkan)

`Benchmarks.Brutal --direct` (headless, 1920x1080, random solid rects on black). Both paths end each frame with the same
app-owned image in `SHADER_READ_ONLY_OPTIMAL`. "Texture" is `SkiaRenderTarget2D` Begin/draw/End (library-managed layout, async End) plus
the app's own pass that samples its image onto the app image (a fullscreen triangle, recorded and submitted every frame, two frames in
flight). "Direct" is `SkiaDirectCanvas` into the app image, with no such pass. CPU is a stopwatch around the whole frame including the
fence wait. GPU is a timestamp query pair around one frame whose submits are held behind a host-set event, so the GPU runs them back to
back with no CPU gaps; median of 200 such frames.
GPU: NVIDIA GeForce RTX 5060 Laptop GPU (driverVersion 0x988BC000, API 1.4), Windows 11. Warmup 30, frames 200, two runs, both shown.

| Load | Path | CPU ms/frame (run 1 / run 2) | GPU ms/frame (run 1 / run 2) |
|---|---|---|---|
| 1 rect | Texture + sampling pass | 0.121 / 0.165 | 0.046 / 0.049 |
| 1 rect | Direct | 0.165 / 0.232 | 0.027 / 0.058 |
| 100 circles | Texture + sampling pass | 0.203 / 0.287 | 0.051 / 0.052 |
| 100 circles | Direct | 0.438 / 0.353 | 0.061 / 0.074 |
| 1000 rects | Texture + sampling pass | 1.232 / 1.214 | 0.097 / 0.097 |
| 1000 rects | Direct | 1.040 / 1.208 | 0.107 / 0.129 |

On this machine only, direct is no faster on CPU or GPU. Direct over texture is 1.4x / 1.4x CPU at 1 rect and 0.84x / 1.0x at 1000 rects;
rows move by up to 40% between runs, so read these as equal within noise at 1000 rects and direct slower at small loads.

- **What direct saves.** The sampling pass takes 0.020 ms of GPU time and 0.03 to 0.10 ms of CPU (fence wait, recording, submit) per
  frame, and the texture path's image is 8.44 MiB of device memory at 1920x1080 (`vkGetImageMemoryRequirements`). Direct saves all three.
- **What direct pays.** Its Begin costs 0.01 to 0.06 ms more CPU than the texture path's, and its End 0.03 to 0.10 ms more. Timing
  `SkiaDirectCanvas` piece by piece (not committed) put the Begin extra in wrapping the image as a new Skia surface (0.011 to 0.022 ms) and the
  End extra in Skia's flush, which costs more on a freshly wrapped surface than on the texture path's long-lived one. Why that flush costs
  more is a guess: Skia creating its framebuffer and image view for the new wrap. That per-pass wrap is what cancels the saving.
- **Submits are not the difference.** Direct makes three queue submits per pass (two layout moves and Skia's), the texture path four
  (the same three plus the app's pass). Each layout-move submit costs about 0.025 to 0.035 ms of CPU here. None can be dropped: Skia
  submits its own command buffer, and SkiaSharp 3.119.4 cannot ask Skia for a final layout, so the moves stay separate submits.
- **Run halves.** Either half of a direct row can be the slower one, the texture rows split the same way, and over 1000 frames the
  direct End time stayed flat (0.121 to 0.131 ms at 1 rect), so the split is machine noise, not something that builds up.
- Direct's GPU time at small loads is at or above the texture path's despite skipping the sampling pass. Not explained; unmeasured guess:
  the layout moves' `ALL_COMMANDS` scopes cost more on the GPU than the texture path's narrower ones.
