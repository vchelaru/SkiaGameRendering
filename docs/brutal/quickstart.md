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

## BRUTAL license

BRUTAL's license is MIT plus attribution terms for the end application: credits notice, its logo before
gameplay, and shipping `NOTICE.txt`. That applies to apps built on BRUTAL, not to this package.

## Measured

`benchmarks/Benchmarks.Brutal` (headless, no window) times `Begin()`, the draw calls and `End()` per frame.
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
