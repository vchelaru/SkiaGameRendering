# MonoGame Android quick start

`SkiaGameRendering.Android` renders with Skia's GL backend into MonoGame `Texture2D` targets on
Android (OpenGL ES), with no CPU readback. The code inside `Game` is the same as every other
MonoGame backend (`SkiaRenderer` plus `SkiaRenderTarget2D`, see the [README](../../README.md)).
For KNI, see the [KNI Android quick start](quickstart.md).

## Prerequisites

- .NET 10 SDK and the Android workload: `dotnet workload install android`. The package targets
  `net10.0-android36.0`; .NET 8 and 9 for Android are out of support.
- A JDK (17+) and the Android SDK (platform 36). `dotnet build -t:InstallAndroidDependencies
  -p:AndroidSdkDirectory=<dir> -p:JavaSdkDirectory=<dir> -p:AcceptAndroidSDKLicenses=True` downloads both.
- MonoGame.Framework.Android 3.8.4.1 or newer (the sample uses 3.8.5.1).
- A device with OpenGL ES 3.0 or newer. The shared FBO code uses `DEPTH24_STENCIL8`, which is ES 3.0 core.

## Add the package

```powershell
dotnet add package SkiaGameRendering.Android
```

`samples/Sample.MonoGame.Android` is a complete project.

## Lifecycle

- **Release Skia when the activity is destroyed.** The process outlives the activity, and the next
  launch builds a new `GraphicsDevice`; `SkiaRenderer` is still bound to the old one and throws on
  the first `SkiaRenderTarget2D`. In `OnDestroy`, dispose your `SkiaRenderTarget2D` instances, then
  call `SkiaRenderer.Dispose()`.
- **`Game.Exit()` only moves the task to the back on MonoGame Android.** Call `Activity.Finish()` to
  end the activity.

## How it works

Same as KNI Android: MonoGame creates its GL ES context through Java's EGL10 and makes it current on
the thread's native EGL state, and `EglSharedContext` (in `Core.OGL`) creates a second context
sharing it. Skia draws into an FBO around the `RenderTarget2D`'s GL texture. The texture name is read
from `GL_TEXTURE_BINDING_2D` right after MonoGame creates it, as on DesktopGL, so there is no
reflection. `SkiaAndroidBackend.Description` says whether the Skia context runs surfaceless.

`SkiaDirectCanvas` works on the back buffer and on a plain render target. **A multisampled render
target does not work on MonoGame Android at all:** unbinding one throws a `NullReferenceException`
inside MonoGame's own resolve (`FramebufferHelper.BlitFramebuffer`), so the smoke test skips that case.

## Verified

`Sample.MonoGame.Android`'s smoke test (`eng/run-android-smoke-test.sh <apk>
com.vchelaru.skiagamerendering.monogame.sample`) checks the pixels and `SkiaDirectCanvas` on the back
buffer and a render target:

| Device | Android | GPU | GLES | Result |
|---|---|---|---|---|
| Emulator on Windows, x86_64 image, `-gpu swiftshader_indirect` | 15 (API 35) | SwiftShader (software) | 3 | Pass, surfaceless, plus a relaunch in the same process |
| CI emulator, Pixel 6 x86_64 image, `-gpu swiftshader_indirect` | 15 (API 35) | SwiftShader (software) | 3 | Run on every PR |

To run on an emulator, build with `-p:EmulatorRuntime=true`, which adds `android-x64`.

Not verified: physical devices, GLES 3.0-only drivers, devices that reject a surfaceless context,
32-bit ARM, rotation, a HOME-and-resume, and performance.
