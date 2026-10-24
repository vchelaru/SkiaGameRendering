# MonoGame iOS quick start

`SkiaGameRendering.iOS` renders with Skia's GL backend into MonoGame `Texture2D` targets on iOS
(OpenGL ES), with no CPU readback. The code inside `Game` is the same as every other MonoGame backend
(`SkiaRenderer` plus `SkiaRenderTarget2D`, see [SkiaRenderTarget2D](../documentation/SkiaRenderTarget2D.md)).

## Prerequisites

- .NET 10 SDK and the iOS workload: `dotnet workload install ios`. The package targets
  `net10.0-ios26.0`, the first iOS target SkiaSharp 4 ships native assets for. Building an app needs
  a Mac with the Xcode that the iOS SDK pack asks for (Xcode 26.0 for `net10.0-ios26.0`).
- MonoGame.Framework.iOS 3.8.4.1 or newer (the sample uses 3.8.5.1).
- iOS 15 or newer. MonoGame iOS renders with OpenGL ES, which Apple deprecated in iOS 12 but still
  ships, on devices and in the Simulator.

## Add the package

```powershell
dotnet add package SkiaGameRendering.iOS
```

`samples/Sample.MonoGame.iOS` is a complete project.

## Lifecycle

- **`Game.Exit()` is a compile error on MonoGame iOS**, and iOS apps are not meant to quit themselves.
  The sample only exits in its smoke test, with `Environment.Exit`.

## How it works

MonoGame creates an `EAGLContext` (OpenGL ES 3, or 2 as a fallback) and keeps it current on the game
thread. `SkiaIosBackend` creates a second `EAGLContext` in the same sharegroup, the way MonoGame makes
its own background-loading context, so textures and renderbuffers are visible from both while each
context keeps its own GL state. Skia draws into an FBO around the `RenderTarget2D`'s GL texture. The
texture name is read from `GL_TEXTURE_BINDING_2D` right after MonoGame creates it, so there is no
reflection.

MonoGame iOS has no default framebuffer: its back buffer is an FBO around a renderbuffer backed by
the view's `CAEAGLLayer`. `SkiaDirectCanvas` on the back buffer attaches that renderbuffer to an FBO
of Skia's. MonoGame deletes and recreates the renderbuffer on every view layout (rotation, for
example), and GL can hand the deleted name to the new one. Skia's FBO holds the renderbuffer only for the length of a pass: one that kept the old renderbuffer alive would stop the new one from taking over the layer, and MonoGame's rebuild would fail.

`SkiaDirectCanvas` works on the back buffer and on a plain render target. **A multisampled render
target does not work on MonoGame iOS at all:** unbinding one throws a `NullReferenceException` inside
MonoGame's own resolve (`FramebufferHelper.BlitFramebuffer`), as on MonoGame Android, so the smoke test
skips that case.

## Verified

`Sample.MonoGame.iOS`'s smoke test (`eng/run-ios-smoke-test.sh <.app> com.vchelaru.skiagamerendering.monogame.sample`)
checks the scene's pixels on the back buffer, then `SkiaDirectCanvas` layered between `SpriteBatch`
draws on the back buffer, a render target, and the back buffer again after MonoGame rebuilds it:

| Device | iOS | GLES | Result |
|---|---|---|---|
| CI iOS Simulator, iPhone, `macos-26` runner (Apple silicon), Xcode 26.0 | 26 | 3 | Run on every PR |

Build for the Simulator with `dotnet build samples/Sample.MonoGame.iOS/Sample.MonoGame.iOS.csproj -c Release -r iossimulator-arm64`.

Not verified: physical devices, iPad, rotation by the user, backgrounding and resuming, GLES 2-only
devices, and performance.
