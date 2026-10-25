# SkiaRenderTarget2D

The sections up to [Godot](#godot) describe the MonoGame, KNI and FNA type. Godot, Stride (D3D11 and Vulkan) and raylib have their own
`SkiaRenderTarget2D` with the same Begin/Canvas/End shape; each has a section below.

## Definition

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It works like
`SpriteBatch`'s own Begin/End shape: individual shapes carry their own position (via Skia's
drawing API, the same way `SpriteBatch.Draw` carries its own), and `End()` composites the whole
surface onto whatever render target is currently bound — no separate
`spriteBatch.Draw(canvas.Texture, ...)` step, the same way `SpriteBatch.End()` needs no separate
step to show its queued sprite draws. Construct it sized to match whatever you intend to draw it
onto — typically the back buffer, or a `RenderTarget2D` the same size as the viewport.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering` (and the platform packages that reference it —
`SkiaGameRendering.WindowsDX`, `SkiaGameRendering.Android`, `SkiaGameRendering.iOS`, `SkiaGameRendering.Kni.DesktopGL`,
`SkiaGameRendering.Kni.WindowsDX`, `SkiaGameRendering.Kni.Android`, `SkiaGameRendering.Kni.iOS`, `SkiaGameRendering.Kni.WebGL`)

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888, int multiSampleCount = 0)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size surface. `multiSampleCount` works like `RenderTarget2D`'s `preferredMultiSampleCount`; see [Multisampling](#multisampling). |

## Members

| Member | Description |
| --- | --- |
| `Texture` | The underlying `Texture2D`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Begins a render pass; throws if a previous `Begin` hasn't been closed yet. |
| `End()` | Ends the render pass and composites the whole surface, at native size and the origin, onto whatever's currently bound. |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the GPU resources. Throws if called between `Begin` and `End`. |

## Example

The common case — one canvas, drawn once, matching its target's size:

```csharp
var canvas = new SkiaRenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

canvas.Begin();
canvas.Canvas.DrawCircle(100, 100, 100, paint);
canvas.End();
```

Many shapes, one shared canvas — the same pattern `SpriteBatch` uses for many sprites in one pass:

```csharp
canvas.Begin();
foreach (var entity in entities)
    canvas.Canvas.DrawCircle(entity.X, entity.Y, entity.Radius, entity.Paint);
canvas.End();
```

Reusing the result more than once (a nested `RenderTarget2D` pass, a second draw at a different
size, and shader sampling) needs the raw texture, so skip the composite:

```csharp
canvas.Begin();
DrawContent(canvas.Canvas);
canvas.EndWithoutDrawing();

spriteBatch.Draw(canvas.Texture, new Rectangle(40, 100, 394, 246), Color.White);

GraphicsDevice.SetRenderTarget(uiTarget);
spriteBatch.Begin();
spriteBatch.Draw(canvas.Texture, Vector2.Zero, Color.White);
spriteBatch.End();
GraphicsDevice.SetRenderTarget(null);

shader.Texture = canvas.Texture;
// ... draw with the shader
```

## Multisampling

`multiSampleCount` is 0 by default, which draws exactly as before. Pass 2, 4 or 8 to back the target with a
multisampled surface: the GPU smooths edges itself, so Skia can use faster paths for shapes and strokes and
spends less CPU recording them. Skia resolves the samples into `Texture` on the GPU at `End()`, with no readback.

```csharp
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 1600, 900, multiSampleCount: 4);
```

Any other value, a count the GPU does not offer, or a backend without support throws `NotSupportedException`.
It never falls back to 0.

**Supported on** the OpenGL backends: MonoGame DesktopGL, Android and iOS, KNI DesktopGL, Android and iOS, and
FNA OpenGL and FNA WebGL. **Throws `NotSupportedException`** on MonoGame WindowsDX (ANGLE/D3D11), WindowsDX12, DesktopVK, KNI
WindowsDX, KNI WebGL and FNA WindowsDX. Godot ([section](#godot)), raylib ([section](#raylib)), Unity and Stride ([D3D11](#stride-d3d11), [Vulkan](#stride-vulkan)) all take the same
`multiSampleCount` parameter so user code reads the same everywhere, but only 0 works there: 2, 4 and 8 throw
`NotSupportedException` (on Unity: `SkiaRenderTarget2D(width, height, multiSampleCount)` and the package's `SkiaRenderer.SupportsMultiSampledTargets`, on every graphics API). A `SkiaDirectCanvas` still draws into an MSAA back buffer on D3D11 and OpenGL, only D3D12 and Vulkan refuse that.

**Portable code** asks `SkiaRenderer.SupportsMultiSampledTargets` instead of hard-coding a backend:

```csharp
int samples = SkiaRenderer.SupportsMultiSampledTargets ? 4 : 0;
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 1600, 900, multiSampleCount: samples);
```

It is true on the OpenGL backends listed above (and FNA WebGL) and false everywhere else. Like `SkiaRenderer.SupportsDirectCanvas`, it is
false until the renderer is initialized (a first `SkiaRenderTarget2D` or an explicit `Initialize` does that), so
create or initialize before reading it. On Godot, raylib, Unity and Stride it is always false and needs no initialization.
It says the backend can multisample, not that this GPU offers 4 samples for your `SKColorType`; that case still throws.

**Measured**, on a Ryzen 7 260 with an RTX 5060 Laptop GPU (driver 32.0.16.1047), MonoGame DesktopGL, 1600x900,
4 samples against none. CPU time of the render-target draw, median of 10 interleaved runs (a ratio above 1x is faster):

| scene | 4 samples vs none |
|---|---|
| Circles, 500 / 2k / 10k | 1.40x / 1.77x / 1.92x |
| Mixed shapes, 2k / 10k | 2.36x / 2.23x |
| SVG, 100 / 500 / 2k | 1.25x / 1.60x / 1.41x |
| Shadows, 100 / 500 / 2k | 0.91x / 0.91x / 0.96x (slower) |
| Pre-rasterized SVG, 100 / 500 / 2k | 0.93x / 0.89x / 0.86x (slower) |

It costs about 14 MiB of extra GPU memory at 1600x900 (whole-GPU reading, median of 3 runs), and the resolve
adds work to every `End()`. Edges differ slightly from the unsampled result: on the mixed shapes 2k scene,
118,889 of 686,054 non-black pixels differ, by at most 121 levels in a channel. Interior pixels are identical.

**Leave it off** when the target mostly draws shadows or already-rasterized images (a pre-rasterized SVG
drawn as a picture): those got slower. Leave it off too where memory is tight, or where pixel-exact output
matters, such as comparing against a golden image. Measure your own scene before turning it on.

`SkiaDirectCanvas` has no such option. Its equivalent is enabling MSAA on the engine's back buffer (for
example `PreferMultiSampling` on MonoGame's `GraphicsDeviceManager`); Skia follows the sample count automatically.

## Remarks

- `End()`'s composite always uses `SpriteSortMode.Deferred` with default blend/sampler/effect
  state, drawn at the surface's native size and the origin (`Vector2.Zero`) of whatever render
  target is currently bound. Set a render target before calling `Begin()` if you want it to land
  somewhere other than the back buffer.
- Size is fixed for the object's lifetime, like `RenderTarget2D` — construct a new
  `SkiaRenderTarget2D` (and `Dispose()` the old one) for a different size.
- Tear the shared backend down (e.g. on exit, or before switching backends) with
  `SkiaRenderer.Dispose()`. Dispose your own `SkiaRenderTarget2D` instances first.
- See the sample this pattern is drawn from: `samples/Sample.Gum.Kni.WebGL/Gum/SkiaGumRenderable.cs`
  and `samples/Sample.Gum.Kni.WebGL/Game1.cs`.

## Godot

### Godot definition

`SkiaRenderTarget2D` is a GPU texture that SkiaSharp renders into and Godot displays like any
other `Texture2D`. It mirrors the other engines' Begin/Canvas/End shape, over `SkiaGameRendering.Core.VK`
when Godot runs on Vulkan, `SkiaGameRendering.Core.D3D12` on D3D12, `SkiaGameRendering.Core.Metal` on
Metal, and `SkiaGameRendering.Core.OGL`
on the Compatibility renderer (the backend is chosen at run time from
`RenderingServer.GetCurrentRenderingDriverName()`). Like the raylib and
Stride adapters it is a standalone class: it does **not** go through `SkiaBackend`, and its `SkiaRenderer` is Godot's own static class,
while `SkiaBackend` is typed to MonoGame's `Texture2D`/`GraphicsDevice`. In Godot the scene tree draws the
texture, so unlike the other adapters there is no composite step - `End()` only submits.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.Godot`

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

### Godot constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(int width, int height, SKColorType colorType = SKColorType.Rgba8888, int multiSampleCount = 0)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size texture. Must be called on the render thread (the main thread under the default "Safe" thread model). On the RenderingDevice drivers it stalls the GPU once, briefly, to hand the texture to Godot in a known state - create targets up front. The Compatibility renderer accepts `Rgba8888` only. `multiSampleCount` must be 0 (the default): 2, 4 and 8 throw `NotSupportedException`, never a silent fallback; see [Multisampling](#multisampling). |

### Godot members

| Member | Description |
| --- | --- |
| `Texture` | A `Texture2D` (a `Texture2DRD` viewing the RD texture; an `ImageTexture` on the Compatibility renderer) to assign to a `Sprite2D`, `TextureRect`, material, etc. Updates in place; nothing needs re-assigning after `End()`. |
| `TextureRid` | The `RenderingDevice` texture RID, for **fragment-shader sampling** at the RD level (e.g. a `SamplerWithTexture` uniform in your own shader). Owned by this object; do not free it, copy to/from it, clear it, or bind it as a storage image - Godot would move it out of the sampled state this object keeps it in. Invalid on the Compatibility renderer. |
| `Width`, `Height` | The fixed size given to the constructor. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`. |
| `Begin(bool clear = true)` | Starts a render pass; with `clear` false the previous contents are kept. Throws off the render thread. |
| `End()` | Submits Skia's GPU work and hands the texture back in the state Godot expects (Vulkan: a layout barrier; D3D12: a `CopyResource` into Godot's texture with barriers around it; OpenGL: just a flush on the shared context), without waiting on the GPU. No composite. |
| `Dispose()` | Frees Skia's resources. On the RenderingDevice drivers it also detaches the `Texture2DRD` view and frees the RD texture, so nodes still showing `Texture` go blank; on the Compatibility renderer the `ImageTexture` keeps its last contents. Must not be called between `Begin`/`End`. `SkiaRenderer.Dispose()` disposes every target still alive. Under the "Separate" thread model the GPU half is handed to the render thread and completes a frame or so later. |
| `static CreatePremultipliedAlphaMaterial()` | A `CanvasItemMaterial` with `BlendMode = PremultAlpha`, matching Skia's premultiplied output. Assign to the displaying node when the canvas has transparent areas. |

### Godot related types

| Type | Role |
| --- | --- |
| `SkiaRenderer` | Static holder for the shared backend, mirroring the raylib and Stride `SkiaRenderer`s. `Initialize(RenderingDevice? = null)` is optional - call it to fail fast on an unsupported driver. `Driver` reports `"vulkan"`, `"d3d12"` or `"opengl3"`, `IsZeroCopy` whether Skia draws straight into Godot's texture, `D3D12UsesEnhancedBarriers` which state-tracking mode Godot's D3D12 device runs in. `Dispose()` releases the backend and every target still alive on it; a target used afterward throws `ObjectDisposedException`. `Initialize` accepts only Godot's global `RenderingDevice`. |
| `SkiaGameRendering.Core.VK.VkImageLayoutTransitioner` | Added for this adapter: queues `vkCmdPipelineBarrier` layout transitions on the host's queue through a ring of command buffers, resolving its entry points through `vkGetDeviceProcAddr`. Any Vulkan host whose engine tracks image layouts needs it. |
| `SkiaGameRendering.Core.D3D12.D3D12ResourceTransitioner` | Its D3D12 twin: queues resource-state transitions and a `CopyResource` bracketed by transitions through a ring of command lists, over raw COM vtables. |

### Godot example

```csharp
using SkiaGameRendering;
using SkiaSharp;

var skia = new SkiaRenderTarget2D(512, 512);
AddChild(new Sprite2D { Texture = skia.Texture, Centered = false });

// every frame:
skia.Begin();
skia.Canvas.DrawCircle(256, 256, 200, paint);
skia.End();
```

### Godot remarks

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
  construct, `Begin`/`End` and `SkiaRenderer.Initialize` inside
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
- **No `SkiaDirectCanvas` on Godot, on any renderer.** Godot gives no point where Skia can draw into its current target in order: Compatibility has no hook that runs while its target is bound, and the RenderingDevice renderers defer every command into a graph submitted at frame end. Use this type.
- See also the [Godot quick start](../godot/quickstart.md).

## Stride (D3D11)

### Stride (D3D11) definition

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It has the same name and
Begin/Canvas/End shape as the [MonoGame `SkiaRenderTarget2D`](#definition), via ANGLE/D3D11 (the same
interop the WindowsDX `SkiaAngleBackend` uses), but is a standalone class: Stride support does
**not** go through `SkiaBackend`. `SkiaBackend` and the MonoGame `SkiaRenderer` are typed directly to MonoGame's
`Texture2D`/`GraphicsDevice`, and Stride has neither type, so `src/SkiaGameRendering.Stride.D3D11/` is a
hand-rolled sibling typed to Stride's own `Texture`/`GraphicsDevice` throughout - the same choice
the raylib package made. Its `SkiaRenderer` is Stride's own static class in the same namespace.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.Stride.D3D11`

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

### Stride (D3D11) constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888, int multiSampleCount = 0)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size target. `multiSampleCount` must be 0 (the default): 2, 4 and 8 throw `NotSupportedException`, never a silent fallback; see [Multisampling](#multisampling). |

### Stride (D3D11) members

| Member | Description |
| --- | --- |
| `Texture` | The Stride `Texture`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Switches to the ANGLE GL context and binds this target's FBO. |
| `End(GraphicsContext graphicsContext)` | Flushes Skia's queued GPU work, switches back to Stride's D3D11 state, and composites the whole surface at native size and the origin via a `SpriteBatch`. Needs a `GraphicsContext` (unlike the other backends' parameterless `End()`) because Stride's `SpriteBatch` requires one - normally `RenderDrawContext.GraphicsContext` from inside a `SceneRendererBase.DrawCore` (see `SkiaSceneRenderer` below). |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the texture and Skia surface. Must not be called between `Begin`/`End`. |

### Stride (D3D11) related types

| Type | Role |
| --- | --- |
| `SkiaRenderer` | Static holder for the shared `SkiaStrideContext`, mirroring the MonoGame and raylib `SkiaRenderer`s. `Initialize(GraphicsDevice)` is optional - call it explicitly only to fail fast on ANGLE/EGL setup instead of lazily on first render target. `Dispose()` releases it. |
| `SkiaSceneRenderer` | A `SceneRendererBase` you add to Stride's `GraphicsCompositor` (e.g. via the Stride Community Toolkit's `Game.AddSceneRenderer`). Stride drives rendering through the compositor rather than a user-owned `Draw()` call, so this is the hook that runs `Begin`/`End` every frame - set its `Canvas` (or `DirectCanvas`) and subscribe to its `SkiaDraw` event. |
| `SkiaDirectCanvas` | Draws straight into the target bound when `Begin(GraphicsContext)` is called, with no texture and no composite; `End()` hands it back. See [SkiaDirectCanvas](SkiaDirectCanvas.md). |

### Stride (D3D11) example

```csharp
using SkiaGameRendering;
using SkiaSharp;

var canvas = new SkiaRenderTarget2D(game.GraphicsDevice, 200, 200);
var renderer = new SkiaSceneRenderer { Canvas = canvas };
renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
game.AddSceneRenderer(renderer);
```

### Stride (D3D11) remarks

- Windows and D3D11 only. For Vulkan (Windows/Linux/macOS), see `SkiaGameRendering.Stride.VK` /
  [the Stride Vulkan section](#stride-vulkan) instead.
- Requires Stride 4.4.1 or newer. Stride's D3D11 backend became Silk.NET (from SharpDX) in
  this release, which is what makes `GraphicsDevice.NativeDevice` a public
  `ComPtr<ID3D11Device>` - Stride 4.3 still wraps D3D11 in SharpDX and
  keeps `NativeDevice` non-public.
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

## Stride (Vulkan)

### Stride (Vulkan) definition

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It has the same name and
Begin/Canvas/End shape as the [MonoGame `SkiaRenderTarget2D`](#definition), via Vulkan (the interop `SkiaGameRendering.Core.VK`
provides), but is a standalone class: like the D3D11 Stride adapter, it does **not** go through
`SkiaBackend` - Stride has neither `Texture2D` nor `GraphicsDevice` typed the way
MonoGame's are, so `src/SkiaGameRendering.Stride.VK/` is a hand-rolled sibling typed to Stride's own
`Texture`/`GraphicsDevice` throughout, duplicated from (not shared with) the D3D11 adapter -
see [the Stride D3D11 section](#stride-d3d11) for why this repo duplicates rather than generalizes across
Stride's two graphics APIs.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.Stride.VK`

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

### Stride (Vulkan) constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888, int multiSampleCount = 0)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size target. `multiSampleCount` must be 0 (the default): 2, 4 and 8 throw `NotSupportedException`, never a silent fallback; see [Multisampling](#multisampling). |

### Stride (Vulkan) members

| Member | Description |
| --- | --- |
| `Texture` | The Stride `Texture`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Acquires Stride's Vulkan queue lock so Skia's and Stride's `vkQueueSubmit` calls stay externally synchronized. |
| `End(GraphicsContext graphicsContext)` | Flushes Skia's queued GPU work and submits it to the shared `VkQueue` (releasing the queue lock), then composites the whole surface at native size and the origin via a `SpriteBatch`. Needs a `GraphicsContext` (unlike the other backends' parameterless `End()`) because Stride's `SpriteBatch` requires one - normally `RenderDrawContext.GraphicsContext` from inside a `SceneRendererBase.DrawCore` (see `SkiaSceneRenderer` below). |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the texture and Skia surface. Must not be called between `Begin`/`End`. |

### Stride (Vulkan) related types

| Type | Role |
| --- | --- |
| `SkiaRenderer` | Static holder for the shared `SkiaStrideVulkanContext`, mirroring `SkiaRenderer`. `Initialize(GraphicsDevice)` is optional - call it explicitly only to fail fast on Vulkan handle setup instead of lazily on first render target. `Dispose()` releases it. |
| `SkiaSceneRenderer` | A `SceneRendererBase` you add to Stride's `GraphicsCompositor` (e.g. via the Stride Community Toolkit's `Game.AddSceneRenderer`). Stride drives rendering through the compositor rather than a user-owned `Draw()` call, so this is the hook that runs `Begin`/`End` every frame - set its `Canvas` and subscribe to its `SkiaDraw` event. |

### Stride (Vulkan) example

```csharp
using SkiaGameRendering;
using SkiaSharp;

var canvas = new SkiaRenderTarget2D(game.GraphicsDevice, 200, 200);
var renderer = new SkiaSceneRenderer { Canvas = canvas };
renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
game.AddSceneRenderer(renderer);
```

### Stride (Vulkan) remarks

- Requires Stride 4.4.1 or newer, built with `StrideGraphicsApi=Vulkan` (Stride's default on
  Linux/macOS; must be forced explicitly on Windows - see `docs/stride/vulkan-quickstart.md`).
- Reaches Stride Vulkan internals via reflection: `GraphicsDevice.NativeInstance`/
  `NativePhysicalDevice`/`NativeDevice` (properties) and `NativeCommandQueue`/`QueueLock` (fields),
  plus `Texture.NativeImage`/`NativeLayout`/`NativeFormat` (fields) - all `internal` in Stride's
  `GraphicsDevice.Vulkan.cs`/`Texture.Vulkan.cs`. Every one is pinned by
  `tests/Tests.Stride.VK/StrideVulkanReflectionTests.cs`. `SkiaDirectCanvas` (see
  [its page](SkiaDirectCanvas.md)) also reaches `Texture.ParentTexture`, `CommandList.activePipeline` and
  `CommandList.FlushInternal(bool)`, pinned by the same tests.
- Unlike the D3D11 adapter, there is no separate GL-style context to bind/unbind: Skia's Vulkan
  backend draws directly against Stride's shared `VkDevice`/`VkQueue`, so `Begin`/`End` only need to
  bracket the queue lock (`SkiaStrideVulkanContext.BeginDraw`/`EndDraw`), not a context switch.
- Skia's Vulkan backend requires `VK_IMAGE_USAGE_TRANSFER_SRC_BIT`/`_DST_BIT` on any wrapped image.
  Stride's own `Texture.Vulkan.cs` sets both unconditionally on every image it creates, so this
  adapter needs no extra texture-creation flags at the Stride API level to satisfy that requirement -
  see `SkiaStrideVulkanContext`'s doc comment for the full analysis (repo issue #54).
- **sRGB swap chain.** In Stride's default Linear color space the back buffer is an sRGB-format image, which Skia cannot wrap, so `SkiaDirectCanvas` wraps it as its UNORM twin and an `SKColor` lands as that color (Stride D3D11 and D3D12 refuse sRGB targets instead). Stride creates the swap chain without `VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT`, so this is outside the letter of the Vulkan spec; drivers accept it for same-size formats (checked on an NVIDIA GPU, not under the validation layer; tracked in #245).
- See also the [Stride Vulkan quick start](../stride/vulkan-quickstart.md).

## raylib

### raylib definition

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It has the MonoGame type's Begin/Canvas/End shape — `End()` composites the whole surface itself via
`Raylib.DrawTexture` — but is a standalone class: raylib support does **not** go through
`SkiaBackend`. That
type is typed directly to MonoGame's `Texture2D`, and raylib is the first non-MonoGame engine
this library supports, so `src/SkiaGameRendering.Raylib.OGL/` is a hand-rolled sibling typed to
raylib's own `Texture2D` throughout, reusing the same engine-agnostic GL/Skia FBO interop
(`Core.OGL`) that `SkiaGlBackend` uses.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.Raylib.OGL`

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

### raylib constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(int width, int height, SKColorType colorType = SKColorType.Rgba8888, int multiSampleCount = 0)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size target. `multiSampleCount` must be 0 (the default): 2, 4 and 8 throw `NotSupportedException`, never a silent fallback; see [Multisampling](#multisampling). |

### raylib members

| Member | Description |
| --- | --- |
| `Texture` | The raylib `Texture2D`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Switches to the shared Skia GL context and binds this target's FBO. |
| `End()` | Flushes Skia's queued GPU work, switches back to raylib's own GL context, and composites the whole surface at native size and the origin via `Raylib.DrawTexture` — call between `Raylib.BeginDrawing()`/`EndDrawing()`, same as any other raylib draw call. |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the texture and Skia surface. Must not be called between `Begin`/`End`. |

### raylib related types

| Type | Role |
| --- | --- |
| `SkiaRenderer` | Static holder for the shared `SkiaRaylibContext`, a static class in the same namespace as the MonoGame/KNI `SkiaRenderer`. `Initialize()` is optional — call it explicitly (after `Raylib.InitWindow`) only to fail fast on context creation instead of lazily on first render target. `Dispose()` releases it. |
| `SkiaRaylibContext` | The shared second GL context. rlgl (raylib's GL layer) and Skia both issue raw GL calls; sharing raylib's single context between them corrupts rlgl's own rendering, so this creates a second context sharing raylib's GL object namespace (the same trick `SkiaGlBackend` uses via SDL). raylib statically links GLFW and doesn't export context-creation entry points, so this goes through the OS's raw windowing API directly: `Core.OGL`'s `WglSharedContext` (raw Win32/WGL) on Windows and `GlxSharedContext` (raw X11/GLX) on Linux, picked via `OperatingSystem.IsWindows()`/`IsLinux()`. The Godot adapter's Compatibility backend uses the same two classes. |

### raylib example

```csharp
using Raylib_cs;
using SkiaGameRendering;
using SkiaSharp;

Raylib.InitWindow(800, 600, "raylib + Skia");
SkiaRenderer.Initialize();

var canvas = new SkiaRenderTarget2D(800, 600);
using var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();

    canvas.Begin();
    canvas.Canvas.Clear(SKColors.CornflowerBlue);
    canvas.Canvas.DrawRect(SKRect.Create(300, 200, 200, 200), paint);
    canvas.End(); // composites itself via Raylib.DrawTexture

    Raylib.EndDrawing();
}

canvas.Dispose();
SkiaRenderer.Dispose();
Raylib.CloseWindow();
```

### raylib remarks

- `Begin`/`End` must run between `Raylib.BeginDrawing()`/`EndDrawing()`, since `End()`'s composite
  is itself a `Raylib.DrawTexture` call. If you need the raw texture without an immediate
  composite (drawing it more than once, or at a different size), call `EndWithoutDrawing()` and
  draw `Texture` yourself.
- Platform support: Windows and Linux (Linux verified under WSLg — X11/GLX, Mesa llvmpipe). macOS
  is not implemented.
- raylib only supports a single window, so `SkiaRenderer` holds one global context rather
  than keying off a window handle the way the MonoGame `SkiaRenderer` keys off a `GraphicsDevice`.
  `SkiaRaylibContext.Initialize` must run after `Raylib.InitWindow` — it reads the window handle
  via `Raylib.GetWindowHandle()`, which is only valid once the window exists.
- The Skia surface is created with `GRSurfaceOrigin.BottomLeft` (the same
  `GlSkiaSurfaceFactory.CreateSurface` used by the MonoGame/KNI GL backends, with an optional
  origin parameter that defaults to `TopLeft` for them), so `Texture` already matches raylib's
  bottom-left texture-sampling convention — no manual flip needed.
- On Linux, `SkiaSharp.NativeAssets.Linux` must be referenced (the main `SkiaSharp` package only
  carries Windows/macOS native assets implicitly); without it `GRContext.CreateGl()` throws
  `DllNotFoundException` for `libSkiaSharp.so` at runtime even though the build succeeds.
- Dispose order matters: dispose your `SkiaRenderTarget2D` instances before calling
  `SkiaRenderer.Dispose()`, which tears down the GL context they depend on.
- See also the [raylib quick start](../raylib/quickstart.md).
