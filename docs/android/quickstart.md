# KNI Android quick start

`SkiaGameRendering.Kni.Android` renders with Skia's GL backend into KNI `Texture2D` targets on
Android (OpenGL ES), with no CPU readback. The code inside `Game` is the same as every other
KNI/MonoGame backend (`SkiaRenderer` plus `SkiaRenderTarget2D`, see the [README](../../README.md)).

## Prerequisites

- .NET 10 SDK and the Android workload: `dotnet workload install android`. KNI's Android platform
  package only ships `net10.0-android36.0`, so the game does too.
- A JDK (17+) and the Android SDK (platform 36). `dotnet build -t:InstallAndroidDependencies
  -p:AndroidSdkDirectory=<dir> -p:JavaSdkDirectory=<dir> -p:AcceptAndroidSDKLicenses=True` downloads both.
- A device or emulator with OpenGL ES 3.0 or newer. Skia's GL backend needs ES 3.0.

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

## Verified

Run with `Sample.Kni.Android`'s smoke test
(`adb shell am start -n <package>/<activity> --ez smoke true`, then read `Smoke test passed` from
`adb logcat -s DOTNET SkiaSample`), plus a relaunch and a HOME-and-resume in the same process:

| Device | Android | GPU | GLES | Result |
|---|---|---|---|---|
| Motorola Edge 2025 | 16 (API 36) | Mali-G615 | 3.2 | Pass, surfaceless |
| Treble GSI on a low-end phone | 12L (API 32) | PowerVR Rogue GE8300 | 3.2 | Pass, surfaceless |

Not verified: GLES 3.0-only drivers, devices that reject a surfaceless context (the fallback path
is untested), 32-bit ARM, emulators, rotation, and the sample's performance.
