# KNI Android quick start

`SkiaGameRendering.Kni.Android` renders with Skia's GL backend into KNI `Texture2D` targets on
Android (OpenGL ES), with no CPU readback. The code inside `Game` is the same as every other
KNI/MonoGame backend (`SkiaRenderer` plus `SkiaRenderTarget2D`, see [SkiaRenderTarget2D](../documentation/SkiaRenderTarget2D.md)).

## Prerequisites

- .NET 10 SDK and the Android workload: `dotnet workload install android`. KNI's Android platform
  package only ships `net10.0-android36.0`, so the game does too.
- A JDK (17+) and the Android SDK (platform 36). `dotnet build -t:InstallAndroidDependencies
  -p:AndroidSdkDirectory=<dir> -p:JavaSdkDirectory=<dir> -p:AcceptAndroidSDKLicenses=True` downloads both.
- A device with OpenGL ES 3.0 or newer. The shared FBO code uses `DEPTH24_STENCIL8`, which is ES 3.0 core; ES 2.0 is not supported.

## Add the package

```powershell
dotnet add package SkiaGameRendering.Kni.Android
```

Your project also references KNI's Android platform (`nkast.Kni.Platform.Android.GL`), as in KNI's
own Android template. `samples/Sample.Kni.Android` is a complete project.

## Lifecycle

- **Release Skia when the activity is destroyed.** The process outlives the activity, and the next
  launch builds a new `GraphicsDevice`; `SkiaRenderer` is still bound to the old one and throws on
  the first `SkiaRenderTarget2D`. KNI does not unload content when an activity is destroyed, so in
  `OnDestroy` dispose your `SkiaRenderTarget2D` instances, then call `SkiaRenderer.Dispose()`.
- **`Game.Exit()` throws on KNI Android** ("Android platform does not allow programmatically
  closing"). Call `Activity.Finish()` instead.

## How it works

KNI creates its GL ES context through Java's EGL10 and makes it current on the thread's native EGL
state. `EglSharedContext` (in `Core.OGL`) reads that with the native `eglGetCurrent*` calls,
creates a second context with the host's `EGLConfig` and GLES version sharing the host's objects,
and Skia draws into an FBO around the `RenderTarget2D`'s GL texture (`GetSharedHandle()`, public
API, so no reflection). The Skia context runs surfaceless where the driver allows it and on the
host's draw surface otherwise; `SkiaKniAndroidBackend.Description` says which.

`SkiaDirectCanvas` works here too. A surfaceless context has no default framebuffer, so for direct
drawing the Skia context is made current on KNI's current window surface, read again on every
`Begin()` because KNI recreates it when the app resumes.

## Verified

Run with `Sample.Kni.Android`'s smoke test
(`adb shell am start -n <package>/<activity> --ez smoke true`, then read `Smoke test passed` from
`adb logcat -s DOTNET SkiaSample`), plus a relaunch and a HOME-and-resume in the same process:

| Device | Android | GPU | GLES | Result |
|---|---|---|---|---|
| Motorola Edge 2025 | 16 (API 36) | Mali-G615 | 3.2 | Pass, surfaceless |
| Treble GSI on a low-end phone | 12L (API 32) | PowerVR Rogue GE8300 | 3.2 | Pass, surfaceless |
| Emulator, x86_64 image, `-gpu host` | 15 (API 35) | Host GPU through the emulator's GLES translator | 3.1 | Pass, surfaceless |
| CI emulator, Pixel 6 x86_64 image, `-gpu swiftshader_indirect` | 15 (API 35) | SwiftShader (software) | 3 | Pass, surfaceless |

The smoke test also checks `SkiaDirectCanvas` on the back buffer, a plain render target and a 4x MSAA
one (`tests/Shared/DirectDrawLayering.cs`), logging each result under `SkiaSample`. Those checks
came after the two phones above were tested, so only the emulators have run them. CI's `android` job
runs the whole smoke test, 4x MSAA included, on the SwiftShader emulator on every PR.

To run on an emulator, build with `-p:EmulatorRuntime=true`, which adds `android-x64`. Passing
`RuntimeIdentifiers` itself with `-p:` also reaches the `net8.0` library references and fails the
build. `eng/run-android-smoke-test.sh <apk>` installs and runs the smoke test on the connected device
and exits non-zero on a failed check, a crash, or a timeout. The smoke test reads the back buffer at
(400, 400), so the screen must be wider than that; the emulator's default AVD is not.

Not verified: GLES 3.0-only drivers, devices that reject a surfaceless context (the fallback path
is untested), 32-bit ARM, rotation, `SkiaDirectCanvas` after a HOME-and-resume, and the sample's
performance.
