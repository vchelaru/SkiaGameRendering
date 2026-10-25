# Unity Quick Start

Supports Unity 6 on Windows x64 (Direct3D 11, Direct3D 12, Vulkan or OpenGL, Mono or IL2CPP) macOS (Metal) and Android (OpenGL ES 3, IL2CPP, arm64).

## Install

In the Package Manager, choose **Add package from git URL** and enter:

```
https://github.com/vchelaru/SkiaGameRendering.git#upm
```

To pin a version, use `#upm/v<version>` instead, for example `#upm/v0.19.0`.

On Windows, set Player Settings > Other Settings > Graphics APIs for Windows to Direct3D11, Direct3D12, Vulkan
or OpenGLCore. macOS uses Metal, its default. For Android, set Graphics APIs to OpenGLES3 only (not Vulkan, not OpenGLES2), the scripting backend to IL2CPP and the target architecture to ARM64; Unity 6000.6 no longer builds x86_64 Android players. Linux and WebGL are not supported yet.

## Limit code to supported platforms

The package only compiles for the Editor and Windows x64, macOS and Android players, so code that uses it
needs the same limits, or the project's other platform builds fail to compile. Put that code in an
asmdef limited to Editor, Windows 64-bit, macOS and Android, or inside:

```csharp
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || (UNITY_STANDALONE_WIN && UNITY_64) || UNITY_STANDALONE_OSX || UNITY_ANDROID
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

## Render pipelines

The examples above use `Camera.onPostRender`, which only the built-in render pipeline raises. Under URP, call `Begin`/`End` from `RenderPipelineManager.endCameraRendering` instead (and check `GraphicsSettings.currentRenderPipeline != null` to pick the hook). No library change is needed. `samples/Sample.Unity` uses the second hook under URP and HDRP: `eng/build-unity-srp.ps1 -Pipeline urp -Linear` builds a URP player (`Build/Mono-URP-Linear`) without changing the committed project; add `-Extra '-d3d12'`, `'-vulkan'` or `'-gl'` for the other APIs. Verified with URP 17.6 (Unity 6000.6) in a Linear project: `--smoke-test` and `--smoke-test --msaa` pass on Direct3D 11, Direct3D 12, Vulkan and OpenGL, and the screen camera and a camera with a `targetTexture` both receive the Skia draw. HDRP 17.6 `--smoke-test` and `--smoke-test --msaa` pass in a Linear project on Direct3D 11, Direct3D 12 and Vulkan (on 12 and Vulkan the multisampled `targetTexture` is refused, the back buffer is drawn) once the sample fixes HDRP's exposure and tone mapping with a global volume (HDRP otherwise scales and tone maps every color it draws). Under URP the sample has no second camera drawing over Skia (a base camera clears color and an overlay camera draws before the base camera's `endCameraRendering`), so that one check expects Skia on top. An SRP ignores `QualitySettings.antiAliasing` (URP reads `msaaSampleCount` from its pipeline asset) and resolves a multisampled camera into a plain back buffer, so under URP `--msaa` makes only the `targetTexture` multisampled: Skia refuses that one on Direct3D 12 and Vulkan and still draws on the back buffer. URP in Gamma passes `--smoke-test` and `--smoke-test --msaa` on Direct3D 11. HDRP does not render this scene in Gamma (HDRP requires Linear). The built-in pipeline still passes both runs on Direct3D 12 and Vulkan. HDRP does not run on OpenGL: Unity refuses the player build (`Platform StandaloneWindows64 with graphics API OpenGLCore is not supported with HDRP, use the Vulkan graphics API instead`, from `HDRPPreprocessBuild`). Under HDRP `--msaa` makes the screen camera really multisampled (the sample sets the asset to Forward lit shader mode with 4x MSAA and the camera's frame settings to MSAA 4x; `HDCamera.msaaSamples` reads `MSAA4x` in the smoke test), and `--smoke-test --msaa` passes on Direct3D 11, Direct3D 12 and Vulkan. HDRP resolves the camera into a plain back buffer before `endCameraRendering`, so Skia still draws on the back buffer on 12 and Vulkan and only the `targetTexture` is refused.

## Multisampling

`SkiaRenderTarget2D(width, height, multiSampleCount)` takes the same parameter as the other engines, but only 0 (the default) works on Unity: 2, 4 and 8 throw `NotSupportedException`, and `SkiaRenderer.SupportsMultiSampledTargets` is false. See [SkiaRenderTarget2D](../documentation/SkiaRenderTarget2D.md#multisampling).

## Sample

`samples/Sample.Unity/` is a Unity 6 project. Run `eng/build-unity-package.ps1` first to fill the
package's `Plugins/`. A player built from it with `--smoke-test` checks the rendered pixels and exits.
