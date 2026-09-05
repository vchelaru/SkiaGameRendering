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

## OpenGL - no first-party equivalent, use llvmpipe

Windows has nothing like WARP for OpenGL. GitHub's `windows-latest` runner has no GPU, and its
default driver only exposes OpenGL 1.1 (no FBOs) - testing on a dev box with a real GPU will not
reproduce this gap.

[pal1000/mesa-dist-win](https://github.com/pal1000/mesa-dist-win) ships a prebuilt, MIT-licensed
`opengl32.dll` (Mesa's llvmpipe software rasterizer, OpenGL 3.3+, full FBO support) built for
exactly this case. Drop it next to the test binary - `opengl32.dll` is not on Windows' "KnownDLLs"
list, so normal DLL search order loads the local copy before System32's. Not yet wired into this
repo; the closest existing DLL-vendoring pattern to copy is `AngleEgl.cs`'s app-local resolver.

## Gotcha

A test that only exercises the mocked/`RecordingGlFunctionLoader`-style path (see
`tests/Tests.Core.OGL/RecordingGlFunctionLoader.cs`) never touches real GL semantics - it can't
catch a wrong enum or a driver rejecting an FBO config. That's the gap either WARP or llvmpipe
closes: a *real* context, still with no GPU or window required.
