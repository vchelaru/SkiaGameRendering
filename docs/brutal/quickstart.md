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

`samples/Sample.Brutal` is a complete headless example. `--smoke-test` draws, reads one pixel back through
Vulkan and exits 0 if it is right.

## Contracts

- **Layout.** The image is in `SHADER_READ_ONLY_OPTIMAL` whenever you own it: after construction and after
  `End()`. `Begin()` moves it to `COLOR_ATTACHMENT_OPTIMAL` and `End()` moves it back, each as a small submit
  on the context's queue. No barrier is needed before sampling on that queue. For another queue, call
  `End(synchronous: true)` and handle queue ownership yourself.
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

- Windows 11, NVIDIA GeForce RTX 5060 Laptop GPU, real driver: smoke test and `BrutalLayoutContractTests` pass.
- Linux, Mesa lavapipe with the Khronos validation layer and synchronization validation: the `brutal-linux` job
  in `.github/workflows/master.yml` runs the same smoke test and tests, failing on any VUID or SYNC-HAZARD message.
- Not measured: performance, multi-threaded queue use, AMD or Intel GPUs.
