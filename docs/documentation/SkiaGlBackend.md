# SkiaGlBackend

## Definition

`SkiaGlBackend` is the `SkiaBackend` for MonoGame's DesktopGL (OpenGL) platform. It creates a
second SDL GL context that shares MonoGame's GL object namespace, so Skia can render directly into
a MonoGame `Texture2D` with zero-copy GPU sharing.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering`

```csharp
public class SkiaGlBackend : SkiaBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaGlBackend()` | Parameterless. All setup happens in `Initialize`. |

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Ready` | Inherited from `SkiaBackend`; already complete — the GL context exists as soon as MonoGame has created its window, before this backend is even constructed. |
| `Initialize(GraphicsDevice)` | Reads MonoGame's current SDL window/GL context via reflection, creates a second SDL GL context with `SDL_GL_SHARE_WITH_CURRENT_CONTEXT`, and creates the Skia `GRContext` on it. |
| `Dispose()` | Disposes the Skia `GRContext`. |

## Example

```csharp
using SkiaGameRendering;

SkiaRenderer.Initialize(new SkiaGlBackend(), GraphicsDevice);
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
```

Most code never constructs this explicitly — `SkiaRenderTarget2D`'s constructor auto-detects and
initializes the right backend. Pass one explicitly only to force a specific backend (e.g. in
tests) instead of relying on auto-detection.

## Remarks

- Cross-platform: Windows, Linux, macOS (wherever MonoGame DesktopGL runs).
- Reaches MonoGame's SDL window/GL context via reflection into MonoGame internals — field names
  may change between MonoGame versions.
- See also the [desktop quick start](../desktop/quickstart.md).
