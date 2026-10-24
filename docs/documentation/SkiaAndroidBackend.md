# SkiaAndroidBackend

## Definition

`SkiaAndroidBackend` is the `SkiaBackend` for MonoGame's Android (OpenGL ES) platform. Same
shared-EGL-context design as `SkiaKniAndroidBackend`.

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
