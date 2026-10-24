# SkiaAndroidBackend

## Definition

`SkiaAndroidBackend` is the `SkiaBackend` for MonoGame's Android (OpenGL ES) platform. Same
shared-EGL-context design as `SkiaAndroidBackend`.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.Android` (targets `net10.0-android36.0`)

```csharp
public class SkiaAndroidBackend : SkiaSharedGlBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaAndroidBackend()` | Parameterless. All setup happens in `Initialize`. |

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Ready` | Inherited from `SkiaBackend`; already complete. |
| `Description` | The Skia context's GLES major version and whether it runs surfaceless, e.g. `GLES 3, surfaceless`. For diagnostics. |
| `Initialize(GraphicsDevice)` | Creates a second EGL context (`EglSharedContext`) sharing MonoGame's, and creates the Skia `GRContext` on it. Call it on the game thread. |
| `Dispose()` | Disposes the Skia `GRContext` and destroys the EGL context. |

## Example

```csharp
SkiaRenderer.Initialize(new SkiaAndroidBackend(), GraphicsDevice);
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
```

`SkiaRenderer.Initialize(GraphicsDevice)` and constructing a `SkiaRenderTarget2D` pick this backend
by themselves, so the explicit form is only for tests.

## Remarks

- No reflection. `EglSharedContext` reads MonoGame's display, context and `EGLConfig` with the native
  `eglGetCurrent*` calls, and the texture's GL name comes from `GL_TEXTURE_BINDING_2D` right after
  MonoGame creates it.
- Surfaceless where the driver allows it (`EGL_KHR_surfaceless_context`), on the host's draw surface
  otherwise. Only the surfaceless path has been run.
- `SkiaDirectCanvas` does not work on a multisampled render target, because MonoGame's own GLES
  resolve throws when one is unbound.
- Release Skia when the activity is destroyed, or the next launch in the same process throws. See the
  [MonoGame Android quick start](../android/monogame-quickstart.md).

## KNI Android

The `SkiaGameRendering.Kni.Android` package (targets `net10.0-android36.0`, as KNI's own Android platform package does) ships a `SkiaAndroidBackend` with the same name and namespace.

Assembly/package: `SkiaGameRendering.Kni.Android`


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
