# SkiaKniAngleBackend

## Definition

`SkiaKniAngleBackend` is the `SkiaBackend` for KNI's WinForms DX11 (D3D11) platform. Reuses the
same ANGLE/D3D11 interop as `SkiaAngleBackend` (`AngleSkiaSurfaceFactory`, shared via
`Core.ANGLE`); this class's job is reaching KNI's own D3D11 device/context/texture.

Namespace: `SkiaGameRendering.Kni.WindowsDX`

Assembly/package: `SkiaGameRendering.Kni.WindowsDX`

```csharp
public class SkiaKniAngleBackend : SkiaBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaKniAngleBackend()` | Parameterless. All setup happens in `Initialize`. |

## Members

| Member | Description |
| --- | --- |
| `GRContext` | The Skia GPU context, valid after `Initialize` returns. |
| `Ready` | Inherited from `SkiaBackend`; already complete. |
| `Initialize(GraphicsDevice)` | Reaches KNI's D3D11 device/context through KNI's public Strategy bridge and wraps them as an ANGLE EGL device. |
| `Dispose()` | Releases the ANGLE/EGL resources. |

## Example

```csharp
using SkiaGameRendering.Kni.WindowsDX;

SkiaRenderer.Initialize(new SkiaKniAngleBackend(), GraphicsDevice);
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
```

## Remarks

- Windows only.
- KNI exposes a public Strategy-pattern bridge down to its platform internals
  (`IPlatformGraphicsDevice`, `IPlatformGraphicsContext`, `IPlatformTexture`), so only the last hop
  into each concrete strategy's SharpDX field (`D3DDevice`, `D3dContext`, `GetTexture`) needs
  reflection — narrower than the MonoGame adapter, which reflects into MonoGame's private fields
  directly.
- Unlike MonoGame WindowsDX, KNI's `RenderTarget2D` allocates its D3D11 resource eagerly, so no
  `SetData` allocation workaround is needed here.
- See also the [desktop quick start](../desktop/quickstart.md).
