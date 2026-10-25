# KNI iOS quick start

`SkiaGameRendering.Kni.iOS` renders with Skia's GL backend into KNI `Texture2D` targets on iOS
(OpenGL ES), with no CPU readback. The code inside `Game` is the same as every other KNI backend
(`SkiaRenderer` plus `SkiaRenderTarget2D`, see [SkiaRenderTarget2D](../documentation/SkiaRenderTarget2D.md)).

## Prerequisites

- .NET 10 SDK and the iOS workload: `dotnet workload install ios`. The package targets
  `net10.0-ios26.5`; SkiaSharp 4 ships native assets from iOS 26.0 on. Building an app needs
  a Mac with the Xcode that the iOS SDK pack asks for (Xcode 26.6 for `net10.0-ios26.5`).
- KNI 4.3.9001 with `nkast.Kni.Platform.iOS.GL` and `<KniPlatform>iOS</KniPlatform>` in the app project.
- iOS 15 or newer. KNI iOS renders with OpenGL ES, which Apple deprecated in iOS 12 but still ships.

## Add the package

```powershell
dotnet add package SkiaGameRendering.Kni.iOS
```

`samples/Sample.Kni.iOS` is a complete project.

## Lifecycle

- **KNI 4.3.9001's iOS platform never sets `PresentationParameters.DeviceWindowHandle`**, and creating the
  device throws a `KeyNotFoundException` for any app. Set it in `PreparingDeviceSettings`, as the sample does:
  `graphicsDeviceManager.PreparingDeviceSettings += (_, e) => e.GraphicsDeviceInformation.PresentationParameters.DeviceWindowHandle = Window.Handle;`

- iOS apps are not meant to quit themselves. The sample only exits in its smoke test, with `Environment.Exit`.

## How it works

Same as [MonoGame iOS](quickstart.md#how-it-works): a second `EAGLContext` in KNI's sharegroup, and a
back buffer that is an FBO around a `CAEAGLLayer` renderbuffer which KNI recreates on every view
layout, so `SkiaDirectCanvas` attaches it only for the length of a pass. The one difference is the
texture name: KNI does not leave a new texture bound, so `SkiaRenderTarget2D` allocates a
`shared: true` `RenderTarget2D` and reads `GetSharedHandle()`. No reflection.

## Verified

`Sample.Kni.iOS`'s smoke test (`eng/run-ios-smoke-test.sh <.app> com.vchelaru.skiagamerendering.sample`)
checks the scene's pixels on the back buffer, then `SkiaDirectCanvas` layered between `SpriteBatch`
draws on the back buffer, a render target, a multisampled render target and the back buffer again after
KNI rebuilds it. It runs on the CI iOS Simulator (iPhone, `macos-26` runner, Xcode 26.6) on every PR.

Build for the Simulator with `dotnet build samples/Sample.Kni.iOS/Sample.Kni.iOS.csproj -c Release -r iossimulator-arm64`.

Not verified: physical devices, iPad, rotation by the user, backgrounding and resuming, GLES 2-only
devices, and performance.
