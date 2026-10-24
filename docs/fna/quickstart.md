# FNA Quick Start

Two packages, one per FNA3D driver:

```powershell
dotnet add package SkiaGameRendering.Fna.WindowsDX   # D3D11, Windows
dotnet add package SkiaGameRendering.Fna.OGL         # OpenGL, Windows/Linux/macOS
```

In the browser, see the [WebGL quick start](webgl-quickstart.md) (`SkiaGameRendering.Fna.WebGL`).

Tested against FNA 26.09. Reference FNA the FNA way, as a submodule plus a `ProjectReference`; the
packages carry no FNA dependency and bind to whatever FNA your game builds against. Ship `fnalibs`
next to your exe as usual.

## Pick the driver

FNA3D has three drivers: SDL_GPU (the default on SDL3 builds), D3D11 (Windows) and OpenGL. Only
D3D11 and OpenGL hand out their native device (`FNA3D_GetSysRendererEXT`), so select one before the
`Game` exists:

```csharp
// Program.cs
System.Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "D3D11"); // or "OpenGL" with SkiaGameRendering.Fna.OGL
using var game = new Game1();
game.Run();
```

`SkiaRenderer.Initialize` throws with that instruction if the package's driver isn't the active one.
SDL_GPU stays unsupported until FNA3D implements `FNA3D_GetSysRendererEXT` for it.

## Draw

Everything inside `Game` is the same code as MonoGame and KNI; see the
[desktop quick start](../desktop/quickstart.md#initialize). D3D11 goes through ANGLE like the
WindowsDX backends, and OpenGL creates a second SDL GL context sharing FNA's, like MonoGame DesktopGL.

## Samples

`samples/Sample.Fna.WindowsDX/` and `samples/Sample.Fna.OGL/` build against the `external/FNA`
submodule and the vendored `external/fnalibs`, which are Windows x64 only; on Linux/macOS drop in
your own.
