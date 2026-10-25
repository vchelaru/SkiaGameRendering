# FNA WebGL quick start

```powershell
dotnet add package SkiaGameRendering.Fna.WebGL
```

FNA runs in the browser as .NET WebAssembly, with FNA3D's OpenGL driver on WebGL2. Skia and FNA3D draw on
the same WebGL context, so `SkiaRenderTarget2D` and `SkiaDirectCanvas` need no readback and no upload. Both
work the way they do on every other platform (see the [desktop quick start](../desktop/quickstart.md#initialize));
`SkiaRenderer.Initialize(GraphicsDevice)` picks `SkiaFnaWebGlBackend`.

## Requirements

- **.NET 10 SDK and the `wasm-tools` workload** (`dotnet workload install wasm-tools`). The Emscripten version
  has to match the .NET runtime's, and the natives below are built with 3.1.56, which is .NET 10's.
- **FNA's SDL2 platform backend.** Mono's WebAssembly interpreter traps on calls that take or return an enum
  with a 64-bit underlying type, which is how SDL3's `SDL_WindowFlags` is declared, so FNA's SDL3 platform
  cannot create a window. Start the runtime with `FNA_PLATFORM_BACKEND=SDL2`.
- **SDL2 and FNA3D built for Emscripten**, linked into the app as `NativeFileReference`s. Run
  `eng/build-fna-wasm-natives.sh` with `emcc` 3.1.56, `cmake` and `ninja` on the path. It writes
  `external/fnalibs-wasm/{SDL2,FNA3D,mojoshader}.a`.
- An FNA submodule and a `ProjectReference` to it, like the [desktop packages](quickstart.md).

## Project

Copy `samples/Sample.Fna.WebGL/`. The parts that matter in the `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <WasmBuildNative>true</WasmBuildNative>
    <EmccExtraLDFlags>-sMIN_WEBGL_VERSION=2 -sMAX_WEBGL_VERSION=2 -sFULL_ES3=1</EmccExtraLDFlags>
    <TrimmerSingleWarn>true</TrimmerSingleWarn>
  </PropertyGroup>
  <ItemGroup>
    <NativeFileReference Include="SDL2.a" />
    <NativeFileReference Include="FNA3D.a" />
    <NativeFileReference Include="mojoshader.a" />
  </ItemGroup>
</Project>
```

In the page's script, set the backend and give Emscripten the canvas before the first frame:

```js
const runtime = await dotnet.withEnvironmentVariable("FNA_PLATFORM_BACKEND", "SDL2").create();
runtime.Module.canvas = document.getElementById("canvas"); // SDL2's EGL path looks it up here
```

The page needs `<canvas id="canvas">`; pass another id to `new SkiaFnaWebGlBackend("my-canvas")` and
`SkiaRenderer.Initialize(new SkiaFnaWebGlBackend("my-canvas"), GraphicsDevice)`.

## Frame loop

FNA's own Emscripten main loop does not suit .NET WebAssembly. Create the `Game` in C#, export a method
with `[JSExport]` that calls `game.RunOneFrame()`, and call it from `requestAnimationFrame`.

## Notes

- `SkiaDirectCanvas` draws into FNA's bound target. FNA3D discards the back buffer's contents when a
  `RenderTarget2D` is bound over it, so draw render targets before the back buffer's frame.
- Each Skia pass saves the GL state FNA3D caches and restores it, since FNA3D has no way to invalidate that
  cache. Skia's own queued GL errors are cleared at the end of every pass (`SkiaFnaWebGlBackend.ClearedGlErrorCount`).
- Only `SurfaceFormat.Color` targets are supported, and `SkiaRenderTarget2D` takes `SKColorType.Rgba8888` only (anything else throws `NotSupportedException`).
- Context loss is not recovered. FNA3D's resources die with the context, so every pass throws `InvalidOperationException` from then on, even if the browser restores the context; dispose the game and start a new one.
- Multisampled back buffers work. `PreferMultiSampling` alone gives 0 samples, because `GraphicsDeviceManager` sets `MultiSampleCount` to 0 when it creates the first device; set `MultiSampleCount` in `PreparingDeviceSettings` and FNA3D grants it. FNA3D then draws to a multisampled FBO and blits it to the canvas each frame, and leaves that FBO bound afterward, so a `gl.readPixels` of the canvas must bind the read framebuffer to `null` first (otherwise it is `INVALID_OPERATION` and reads nothing).
- Tests: `tests/Tests.Fna.WebGL` (Playwright, Chromium) reads canvas pixels after each draw.
