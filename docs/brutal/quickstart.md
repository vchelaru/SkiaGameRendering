# BRUTAL quickstart

[BRUTAL](https://github.com/ahwootech/BRUTAL) ships Vulkan bindings and no renderer or texture type, so
`SkiaGameRendering.Brutal` gives you a Vulkan image that Skia draws into on the GPU. You sample it in your
own renderer. Windows and Linux, Vulkan 1.3, no CPU readback.

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
- **No MSAA.** Multisampled targets are not offered. Other `SKColorType`s throw `NotSupportedException`.
- **Color space.** The image is plain UNORM and holds the sRGB bytes you draw. If your pass writes to an sRGB
  target without decoding the sampled value first, colors are encoded twice. Stride has the same issue; see
  its Vulkan quickstart.

## OpenGL

BRUTAL also ships OpenGL bindings (`Ahwoo.BRUTAL.OpenGl`) next to its GLFW and SDL windowing packages. For an app on
an OpenGL 3.3+ context use `SkiaGameRendering.Brutal.OGL`, which draws into a `GL_TEXTURE_2D`. Windows and Linux.

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
  texture binding). Program, vertex array, buffers, viewport, scissor, blend, depth/stencil and pixel-store state are left
  as Skia had them, so set yours again after `End()`.
- **Texture.** `GL_RGBA8`, linear filtering, clamped, premultiplied alpha; canvas row 0 is texel row 0. No MSAA, and only
  `Rgba8888`.
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
- **State.** `End()` restores only your draw framebuffer binding, like the texture target.

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
