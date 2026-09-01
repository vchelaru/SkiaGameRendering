# SkiaAngleBackend

## Definition

`SkiaAngleBackend` is the `SkiaBackend` for MonoGame's WindowsDX (D3D11) platform. Skia issues GL
calls; ANGLE (Google's GL ES → D3D11 translator, the same library Chrome uses for WebGL on
Windows) translates them onto the same D3D11 device MonoGame already owns, so Skia renders
directly into a MonoGame `Texture2D` with zero-copy GPU sharing.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.WindowsDX`

```csharp
public class SkiaAngleBackend : SkiaBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaAngleBackend()` | Parameterless. All setup happens in `Initialize`. |

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Ready` | Inherited from `SkiaBackend`; already complete — the D3D11 device exists as soon as MonoGame creates its `GraphicsDevice`. |
| `Initialize(GraphicsDevice)` | Extracts MonoGame's internal D3D11 device/context via reflection and wraps them as an ANGLE EGL device (`AngleSkiaSurfaceFactory`, shared with `SkiaKniAngleBackend`). |
| `Dispose()` | Releases the ANGLE/EGL resources. |

## Example

```csharp
using SkiaGameRendering;

SkiaRenderer.Initialize(new SkiaAngleBackend(), GraphicsDevice);
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
```

## Remarks

- Windows only.
- Reaches MonoGame's internal `_d3dDevice`/`_d3dContext`/`_texture` (SharpDX-wrapped) fields via
  reflection — these may change between MonoGame versions, and further if MonoGame moves away from
  SharpDX.
- ANGLE requires textures to have `D3D11_BIND_RENDER_TARGET`; a plain `Texture2D` only sets
  `BIND_SHADER_RESOURCE`, so `CreateTexture` allocates a `RenderTarget2D` instead (it sets both).
  Because MonoGame WindowsDX allocates its D3D11 resource lazily, `CaptureTextureHandle` forces
  allocation with a one-time `SetData(new byte[w*h*4])` call — wasteful but currently the only known
  trigger; a cheaper one is an open item (see the repo's `TODO.md`).
- ANGLE modifies D3D11 pipeline state (shaders, blend, render targets, viewport) when it renders;
  MonoGame caches its own copy and only re-applies on detected changes, so its state cache goes
  stale after ANGLE runs. `AngleSkiaSurfaceFactory` handles resetting this — you don't need to.
- See also the [desktop quick start](../desktop/quickstart.md).
