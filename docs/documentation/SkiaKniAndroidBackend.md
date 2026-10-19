# SkiaKniAndroidBackend

## Definition

`SkiaKniAndroidBackend` is the `SkiaBackend` for KNI's Android (OpenGL ES) platform. Same shared-context
trick as `SkiaKniGlBackend`, with an EGL context instead of an SDL one.

Namespace: `SkiaGameRendering.Kni.Android`

Assembly/package: `SkiaGameRendering.Kni.Android` (targets `net10.0-android36.0`, as KNI's own Android platform package does)

```csharp
public class SkiaKniAndroidBackend : SkiaBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaKniAndroidBackend()` | Parameterless. All setup happens in `Initialize`. |

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Ready` | Inherited from `SkiaBackend`; already complete. |
| `Description` | The Skia context's GLES major version and whether it runs surfaceless, e.g. `GLES 3, surfaceless`. For diagnostics. |
| `Initialize(GraphicsDevice)` | Creates a second EGL context (`EglSharedContext`) sharing KNI's, and creates the Skia `GRContext` on it. Call it on the game thread, in `Draw`. |
| `Dispose()` | Disposes the Skia `GRContext` and destroys the EGL context. |

## Example

```csharp
using SkiaGameRendering.Kni.Android;

SkiaRenderer.Initialize(new SkiaKniAndroidBackend(), GraphicsDevice);
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
```

`SkiaRenderer.Initialize(GraphicsDevice)` and constructing a `SkiaRenderTarget2D` pick this backend
by themselves, so the explicit form is only for tests.

## Remarks

- No reflection. KNI creates its context through Java's EGL10 but makes it current on the thread's
  native EGL state, so `EglSharedContext` reads the display, context and `EGLConfig` with the
  native `eglGetCurrent*` calls. The native texture handle comes from `GetSharedHandle()` on a
  `shared: true` `RenderTarget2D`, which is public KNI API.
- The Skia context uses the host's `EGLConfig` and GLES major version. OpenGL ES 3.0 or newer is expected (`DEPTH24_STENCIL8` is ES 3.0 core); only 3.2 drivers have been run.
- Surfaceless where the driver allows it (`EGL_KHR_surfaceless_context`), on the host's draw surface
  otherwise. Both devices it was run on took the surfaceless path, so the fallback is untested.
- Release Skia when the activity is destroyed (dispose your `SkiaRenderTarget2D` instances, then
  `SkiaRenderer.Dispose()`), or the next launch in the same process throws. `Game.Exit()` throws on
  KNI Android; finish the activity instead.
- See the [Android quick start](../android/quickstart.md).
