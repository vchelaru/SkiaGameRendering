# Unity Quick Start

Supports Unity 6 on Windows x64 (Direct3D 11, Direct3D 12, Vulkan or OpenGL, Mono or IL2CPP) and macOS (Metal).

## Install

In the Package Manager, choose **Add package from git URL** and enter:

```
https://github.com/vchelaru/SkiaGameRendering.git#upm
```

To pin a version, use `#upm/v<version>` instead, for example `#upm/v0.19.0`.

On Windows, set Player Settings > Other Settings > Graphics APIs for Windows to Direct3D11, Direct3D12, Vulkan
or OpenGLCore. OpenGL ES (Android) and Linux are not supported yet. macOS uses Metal, its default.

## Limit code to supported platforms

The package only compiles for the Editor and Windows x64 and macOS players, so code that uses it
needs the same limits, or the project's other platform builds fail to compile. Put that code in an
asmdef limited to Editor, Windows 64-bit and macOS, or inside:

```csharp
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || (UNITY_STANDALONE_WIN && UNITY_64) || UNITY_STANDALONE_OSX
```

## Draw

Call `Begin`/`End` from the main thread. Unity renders on its own thread, so the canvas records
your drawing and `End` plays it back on Unity's render thread.

```csharp
var target = new SkiaRenderTarget2D(512, 512);

void Update()
{
    target.Begin();
    target.Canvas.DrawCircle(256, 256, 200, paint);
    target.End();
}

void OnGUI()
{
    if (Event.current.type == EventType.Repaint)
        Graphics.DrawTexture(new Rect(0, 0, 512, 512), target.Texture, SkiaRenderTarget2D.PremultipliedGuiMaterial);
}
```

## Premultiplied alpha

Skia writes premultiplied alpha, and Unity's default blending darkens its edges. Draw `Texture`
with the matching material:

- `SkiaRenderTarget2D.PremultipliedMaterial` for a `RawImage`'s material, or a copy on a mesh.
- `SkiaRenderTarget2D.PremultipliedGuiMaterial` for `Graphics.DrawTexture` in `OnGUI`.

Both work in Gamma and Linear color space projects, but only with the right one for where you
draw, since IMGUI stays in gamma in a Linear project.

## Sample

`samples/Sample.Unity/` is a Unity 6 project. Run `eng/build-unity-package.ps1` first to fill the
package's `Plugins/`. A player built from it with `--smoke-test` checks the rendered pixels and exits.
