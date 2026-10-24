# SkiaIosBackend

## Definition

`SkiaIosBackend` is the `SkiaBackend` for the iOS (OpenGL ES) platforms of MonoGame and KNI. Same shared-context
design as `SkiaAndroidBackend`, with an EAGL sharegroup in place of a shared EGL context.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.iOS` (targets `net10.0-ios26.0`)

```csharp
public class SkiaIosBackend : SkiaSharedGlBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaIosBackend()` | Parameterless. All setup happens in `Initialize`. |

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Ready` | Inherited from `SkiaBackend`; already complete. |
| `Description` | The Skia context's EAGL API, e.g. `OpenGLES3`. For diagnostics. |
| `Initialize(GraphicsDevice)` | Creates a second `EAGLContext` in the sharegroup of MonoGame's current one, and creates the Skia `GRContext` on it. Call it on the game thread. |
| `Dispose()` | Disposes the Skia `GRContext` and the EAGL context. |

## Example

```csharp
SkiaRenderer.Initialize(new SkiaIosBackend(), GraphicsDevice);
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
```

`SkiaRenderer.Initialize(GraphicsDevice)` and constructing a `SkiaRenderTarget2D` pick this backend
by themselves, so the explicit form is only for tests.

## Remarks

- No reflection. MonoGame's context is `EAGLContext.CurrentContext` on the game thread, and the
  texture's GL name comes from `GL_TEXTURE_BINDING_2D` right after MonoGame creates it.
- MonoGame's back buffer is an FBO around a `CAEAGLLayer` renderbuffer that MonoGame recreates on
  every view layout. `SkiaDirectCanvas` attaches it only for the length of a pass, so the rebuild can take over the layer.
- `SkiaDirectCanvas` does not work on a multisampled render target, because MonoGame's own GLES
  resolve throws when one is unbound.
- See the [MonoGame iOS quick start](../ios/quickstart.md).

## KNI iOS

The `SkiaGameRendering.Kni.iOS` package (targets `net10.0-ios26.0`) ships a `SkiaIosBackend` with the same name and namespace.

Assembly/package: `SkiaGameRendering.Kni.iOS`

- KNI iOS keeps its `EAGLContext` current on the game thread and builds the same back buffer as MonoGame (an FBO around a `CAEAGLLayer` renderbuffer, recreated on every view layout), so everything above applies.
- KNI does not leave a new texture bound, so the backend creates a `shared: true` `RenderTarget2D` and reads its GL name from the public `GetSharedHandle()`, as `SkiaAndroidBackend` does on KNI Android.
- See the [KNI iOS quick start](../ios/kni-quickstart.md).
