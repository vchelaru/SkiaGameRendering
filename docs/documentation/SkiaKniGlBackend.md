# SkiaKniGlBackend

## Definition

`SkiaKniGlBackend` is the `SkiaBackend` for KNI's DesktopGL (OpenGL) platform. Same shared-SDL-GL-context
trick as `SkiaGlBackend`, adapted to KNI's own SDL bridge and shared-texture API.

Namespace: `SkiaGameRendering.Kni.DesktopGL`

Assembly/package: `SkiaGameRendering.Kni.DesktopGL`

```csharp
public class SkiaKniGlBackend : SkiaBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaKniGlBackend()` | Parameterless. All setup happens in `Initialize`. |

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Ready` | Inherited from `SkiaBackend`; already complete. |
| `Initialize(GraphicsDevice)` | Creates a second SDL GL context sharing KNI's, and creates the Skia `GRContext` on it. |
| `Dispose()` | Disposes the Skia `GRContext`. |

## Example

```csharp
using SkiaGameRendering.Kni.DesktopGL;

SkiaRenderer.Initialize(new SkiaKniGlBackend(), GraphicsDevice);
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
```

## Remarks

- Cross-platform: Windows, Linux, macOS (wherever KNI DesktopGL runs).
- The window handle and native GL texture handle (via a `shared: true` `RenderTarget2D`) are plain
  public KNI API. Only the SDL GL-context-sharing hop still needs reflection, into public members
  of KNI's internal `Sdl`/`Sdl.GL` types — narrower reflection surface than the MonoGame adapter,
  which reflects into MonoGame's private fields directly.
- See also the [desktop quick start](../desktop/quickstart.md).
