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

## Vulkan - lavapipe

`tests/Tests.Core.VK/VulkanTestDevice.cs` creates a real, minimal `VkInstance`/`VkPhysicalDevice`/
`VkDevice`/`VkQueue` via raw P/Invoke against the system Vulkan loader (`VulkanTestNative.cs`) - no
validation layers, no extensions. `VkSkiaPixelReadbackTests.cs` allocates a host-owned `VkImage`,
wraps it with `SkiaGameRendering.Core.VK`'s `VkSkiaSurfaceFactory`, draws, then reads it back via a
staging-buffer copy. Same shape as the WARP/WGL tests above, one Vulkan analog down.

Unlike WARP, Windows has no first-party software Vulkan implementation, and unlike WGL, there is no
baseline driver to fall back to at all - a Vulkan-less machine enumerates zero physical devices and
the test just fails (no graceful skip). Mesa lavapipe (`vulkan_lvp.dll` + `lvp_icd.x86_64.json`) plays
the same role llvmpipe plays for GL, from the exact same
[pal1000/mesa-dist-win](https://github.com/pal1000/mesa-dist-win) `release-msvc` archive already used
above (`x64/lvp_icd.x86_64.json`, `x64/vulkan_lvp.dll`) - confirmed by downloading the archive and
running the real test against it that, unlike the GL path, it's self-contained: no
`libgallium_wgl.dll` dependency needed. `master.yml`'s "Download Mesa lavapipe for headless Core.VK
tests" step vendors both files into `tests/Tests.Core.VK/mesa-vendor/`.

**`VK_ICD_FILENAMES` does not work on `windows-latest` - use the registry instead.** The obvious
approach (point `VK_ICD_FILENAMES` at the vendored json's absolute path, the Vulkan equivalent of
`GALLIUM_DRIVER=llvmpipe`) fails silently: `windows-latest` runs its steps as an elevated
(administrator) process, and the Vulkan loader deliberately ignores `VK_ICD_FILENAMES` - along with
`VK_DRIVER_FILES`, `VK_ADD_DRIVER_FILES`, and the layer-path variables - for elevated processes. This
is documented Vulkan-Loader anti-privilege-escalation hardening (an elevated process trusting an
unprivileged env var to load an arbitrary DLL would be an escalation vector), not a bug, and it gives
no build-time warning - `vkCreateInstance` just returns `VK_ERROR_INCOMPATIBLE_DRIVER` (-9) as if no
driver existed at all. Confirmed directly against the runner (not assumed) via a temporary
`VK_LOADER_DEBUG=all` diagnostic step, which logged `Loader is running with elevated permissions.
Environment variable VK_ICD_FILENAMES will be ignored`, immediately followed by `Registry lookup
failed to get ICD manifest files. Possibly missing Vulkan driver?` and `Found no drivers!`. The
registry-based driver-registration path is exempt (writing `HKLM` already requires admin, so the
loader trusts it - the same mechanism a real GPU driver installer uses), so `master.yml`'s "Register
Mesa lavapipe as a Vulkan ICD" step writes the vendored json's absolute path as a `DWORD 0` value
under `HKLM:\SOFTWARE\Khronos\Vulkan\Drivers` instead of setting an environment variable. No
conditional MSBuild copy is needed the way `Tests.Core.OGL.csproj` needs one for its DLLs: the ICD
json's `library_path` (`.\vulkan_lvp.dll`) resolves relative to the json itself, not to the test
binary's output directory. Locally, with no registry entry added, the test just uses whatever real
Vulkan driver is already on the machine.

**Linux CI coverage does not exist for this or for Core.OGL.** The only job running
`dotnet test tests/Tests.proj` is `desktop-and-core` on `windows-latest`; the `ubuntu-latest` job
(`webgl-functional`) is a Playwright suite unrelated to either. `apt-get install mesa-vulkan-drivers`
installing `lvp_icd.x86_64.json` under `/usr/share/vulkan/icd.d/` would be the Linux equivalent if a
Linux test job is ever added, but this is unverified - no such job runs today.

### Landmines

- **Skia's Vulkan backend unconditionally requires BOTH `VK_IMAGE_USAGE_TRANSFER_SRC_BIT` and
  `VK_IMAGE_USAGE_TRANSFER_DST_BIT` on any wrapped image, texture or render target, regardless of
  whether the host ever uses them** (`check_image_info` in Skia's `GrVkGpu.cpp`) - omit either and
  `SKSurface.Create` just returns `null`, with nothing indicating which check failed. `GRContext`
  creation, `GetMaxSurfaceSampleCount`, and a fully Skia-managed (non-wrapped) Vulkan surface all
  still work fine in this state, which rules out most other causes before this one. No Vulkan
  validation layer was available on the dev box that found this, so tracing the actual Skia source
  (`GrVkGpu.cpp`'s `check_image_info`/`check_rt_image_info`) was the only way to find it -
  `VkSkiaSurfaceFactory.CreateTextureState` now checks for both bits itself and throws a specific
  `ArgumentException` instead of letting a caller hit the opaque `SKSurface.Create` failure.
- **SkiaSharp 3.119.4 has no way to query or steer the `VkImageLayout` Skia leaves a wrapped image in
  after a draw.** No `gr_backendrendertarget_get_vk_imageinfo` native entry point (unlike the GL path)
  and no `GrBackendSurfaceMutableState` binding either - verified by listing the actual `SkiaApi`
  P/Invoke surface, not assumed. See `VkSkiaSurfaceFactory.EndDraw`'s doc comment for the resulting
  design: the post-draw layout is a documented ASSUMPTION (`VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL`
  for a color-attachment image), not a value this library can report back.
- **A P/Invoke array-of-struct parameter (e.g. `VkQueueFamilyProperties[]`) needs an explicit
  `[In, Out]` attribute to round-trip through `vkGetPhysicalDeviceQueueFamilyProperties`.** Without
  it, the call appears to succeed (no exception, correct count) but every element comes back zeroed -
  the native writes never make it back into the managed array. An `IntPtr[]` (as used for
  `vkEnumeratePhysicalDevices`) round-trips fine without the attribute; a custom struct array does not.

## Golden images

`tests/Shared/GoldenScene.cs` (one scene, drawn by every backend) and `tests/Shared/GoldenImage.cs`
(tolerance comparison against a checked-in PNG in each test project's `goldens/`) are linked into the
three `Tests.Core.*` projects the way `EngineReflectionPin.cs` is. A failing run writes the render and
a magenta diff to `golden-failures/` next to the test binary, which `master.yml` uploads as an
artifact. `SKIAGAMERENDERING_UPDATE_GOLDENS=1` rewrites a golden instead of comparing, and fails the
test so the update cannot read as a pass.

### Landmines

- **A Core.OGL or Core.VK golden is only valid if Mesa rendered it.** Those backends use whatever
  driver the machine has, so one generated on a dev box's GPU will not match CI. Both compare only
  when `SKIAGAMERENDERING_PINNED_RASTERIZER=1` (set in `master.yml`) and otherwise check the render's
  orientation alone; regenerating locally means vendoring Mesa first and running under
  `GALLIUM_DRIVER=llvmpipe` or `VK_ICD_FILENAMES`. Core.ANGLE needs no gate, since WARP is the driver
  everywhere.
- **`VK_ICD_FILENAMES` works on a dev box**, the mirror of the elevated-CI case above: an unelevated
  shell is exactly what the Vulkan loader still honors it for.
