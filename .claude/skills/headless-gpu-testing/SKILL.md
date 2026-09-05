---
name: headless-gpu-testing
description: Getting real D3D11/OpenGL contexts in CI without a GPU or window. Triggers: windows-latest has no GPU, WARP, llvmpipe, mesa-dist-win, Tests.Core.ANGLE, Tests.Core.OGL, headless rendering test.
---

# Headless GPU Testing

`Core.ANGLE` and `Core.OGL` take their D3D11/GL context from the caller, so a test can create its
own instead of needing a real GPU, a window, or a running sample - see
`tests/Tests.Core.ANGLE/WarpDevice.cs` and `AngleSkiaPixelReadbackTests.cs` for the established
D3D11 pattern.

## D3D11 - WARP

`D3D11CreateDevice` with `D3D_DRIVER_TYPE_WARP` gets Microsoft's software D3D11 rasterizer, bundled
with every Windows install (including GitHub's `windows-latest` runners) - no vendoring needed.
`WarpDevice.cs` is the reusable helper; `D3D11StateSwapTests.cs` and `AngleSkiaPixelReadbackTests.cs`
are the usage examples (a state-swap round-trip, and a full draw-then-read-pixels-back test).

## OpenGL - WGL context + llvmpipe

Windows has nothing like WARP for OpenGL. `tests/Tests.Core.OGL/WglContext.cs` gets a real context
the standard way: a hidden window (created without `WS_VISIBLE`, never shown) backing a real
`wglCreateContext`. `WglFunctionLoader.cs` resolves entry points via `wglGetProcAddress`, falling
back to `GetProcAddress` on `opengl32.dll` for the pre-1.1 core functions `wglGetProcAddress` can't
resolve - watch for its failure sentinels, which are 0, 1, 2, 3 and -1, not just `NULL`.
`WglSkiaPixelReadbackTests.cs` drives the real `GlSkiaSurfaceFactory` path through it and reads
pixels back with `glReadPixels` (bound-framebuffer-relative, unlike D3D11's `CopyResource` - so the
readback has to happen before unbinding the FBO, not after).

GitHub's `windows-latest` runner has no GPU, and its default driver only exposes OpenGL 1.1 (no
FBOs) - a dev box with a real GPU exercises the WGL/loader plumbing but not that gap.
[pal1000/mesa-dist-win](https://github.com/pal1000/mesa-dist-win)'s `release-msvc` archive ships a
software rasterizer (Mesa llvmpipe, GL 4.6, full FBO support) built for exactly this. `x64/opengl32.dll`
in that archive is a thin loader - it needs `x64/libgallium_wgl.dll` (~59MB) alongside it to actually
render, which is why `master.yml`'s "Download Mesa llvmpipe for headless Core.OGL tests" step fetches
both into `tests/Tests.Core.OGL/mesa-vendor/` at CI time instead of vendoring them into the repo (too
large to check in). `Tests.Core.OGL.csproj` copies them into the test output directory - conditionally,
so their absence on a dev box is a silent no-op, not a build error.

### Landmines

- **A vendored `opengl32.dll` next to the test binary is not enough by itself.** GDI's
  `ChoosePixelFormat`/`SetPixelFormat` resolve their own internal `opengl32.dll` reference
  independently of any `DllImport` in test code, and on modern Windows that resolution is hardened to
  always come from System32 - so `wglCreateContext` fails against a pixel format GDI picked from the
  real driver's tables while WGL calls go to Mesa's. The fix, `WglNative.PreloadVendoredOpenGl32IfPresent`,
  exploits the one loophole: Windows reuses an already-loaded module that matches by file name
  regardless of where it came from, so explicitly `LoadLibrary`-ing the vendored DLL's full path
  *before* the first GDI pixel-format call makes GDI's own resolution land on the same module.
- **Mesa's gallium WGL loader prefers a D3D12-backed driver over llvmpipe whenever a D3D12 adapter is
  enumerable (WARP included), and only falls back to llvmpipe when none exists.** That makes the
  software path non-deterministic across machines - set `GALLIUM_DRIVER=llvmpipe` to force it
  regardless of what the host exposes (`master.yml` sets this for the whole `dotnet test` step).
- **`dotnet test` runs the assembly inside a vstest `testhost` process.** `AppContext.BaseDirectory`
  there is still the test assembly's own output directory (not some shared vstest install location),
  so app-local DLL probing works the same as any other .NET Core host - this was checked directly,
  not assumed.
