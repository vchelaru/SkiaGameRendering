# Samples

- `Sample.MonoGame.DesktopGL/` — DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `Sample.MonoGame.WindowsDX/` — WindowsDX sample (Windows only)
- `Sample.Kni.DesktopGL/` — KNI DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `Sample.Kni.WindowsDX/` — KNI WindowsDX sample (Windows only)
- `Sample.MonoGame.Android/`: MonoGame Android sample (.NET 10; install the APK, or run with `--ez smoke true` to check the pixels)
- `Sample.MonoGame.iOS/`: MonoGame iOS sample (.NET 10; build for the Simulator or a device on a Mac, or launch with `--smoke-test` to check the pixels, see `eng/run-ios-smoke-test.sh`)
- `Sample.Kni.Android/`: KNI Android sample (.NET 10; install the APK, or run with `--ez smoke true` to check the pixels)
- `Sample.Kni.WebGL/` — KNI Blazor WebAssembly sample
- `Sample.Gum.Kni.WebGL/` — KNI Blazor WebAssembly sample of Gum UI interleaved with SpriteBatch, render targets, shaders, and browser input
- `Sample.Raylib.OGL/` — raylib sample (Windows, Linux, macOS)
- `Sample.Fna.WindowsDX/`: FNA sample (Windows, D3D11 only; builds against the `external/FNA` submodule and the vendored `external/fnalibs`)
- `Sample.Fna.WebGL/`: FNA in the browser (.NET 10 WebAssembly); build the natives first with `eng/build-fna-wasm-natives.sh`, see `docs/fna/webgl-quickstart.md`
- `Sample.Fna.OGL/`: FNA sample on FNA3D's OpenGL driver (same setup; the vendored fnalibs are Windows x64 only, so on Linux/macOS drop in your own)
- `Sample.Stride.D3D11/` — Stride sample (Windows, D3D11 only)
- `Sample.Stride.VK/` — Stride sample (Vulkan; builds on Windows via `StrideGraphicsApi=Vulkan`, runs on Windows/Linux/macOS)
- `Sample.Stride.D3D12/`: Stride sample (Windows, D3D12 via `StrideGraphicsApi=Direct3D12`)
- `Sample.Godot/` — Godot 4.7 project (Vulkan, D3D12 or Compatibility via `--rendering-driver`; `dotnet build` it, then open or run it with a Godot .NET editor binary - not shipped here)
- `Sample.Unity/`: Unity 6 project (Windows D3D11 and D3D12, macOS Metal). Run `eng/build-unity-package.ps1` first to fill the package's `Plugins/`; `--smoke-test` checks the rendered pixels and exits
- `Test/` — More comprehensive test with dynamic add/remove, FPS counter, input handling

DesktopGL, WindowsDX, KNI WindowsDX, and both FNA samples share the same `Game1.cs` via a linked file include; KNI DesktopGL has its own copy.
