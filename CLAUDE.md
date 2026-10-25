# SkiaGameRendering Repository Guidelines

## What Is This?

A library that lets MonoGame, KNI, FNA, raylib, and Stride applications render with SkiaSharp straight
into engine textures, with no CPU readback. `README.md` is a short landing page with the package
table. Per-engine setup is in `docs/<engine>/quickstart.md`, the API in `docs/documentation/`, and the
backend-per-graphics-API architecture in `docs/architecture.md`. Keep the README short and put new
detail in those pages. Performance results differ by platform, so docs report what was measured
where and never turn it into one blanket rule ("draw sprites with `SpriteBatch`"). `SkiaGameRendering-Notes.md` has the deeper interop detail (ANGLE, D3D11 state management).

**Never propose or build a CPU rasterization or CPU readback fallback, for any backend or any
failure.** GPU rendering with no CPU readback is the whole purpose of the library. When a backend
cannot run on some hardware, the options are to fix it, narrow the supported range, or hold back
the release.

**New public API takes the shape of the existing API it sits beside.** Drawing types use
`SkiaRenderTarget2D`'s `Begin()`/`Canvas`/`End()`, like `SpriteBatch`; don't add callback-style
variants.

This repo began as a fork of [mfigueirido/SkiaMonoGameRendering](https://github.com/mfigueirido/SkiaMonoGameRendering),
and has since diverged. It exists to close the vector-rendering gap between SkiaGum and
MonoGame-Gum, so changes that affect Gum's renderers matter more than they look.

**The main branch is `master`, not `main`.** Base every branch and PR off it.

## Skills and Guidance Files

Load any skill under `.claude/skills/` whose trigger matches the area you are working in, before
reading code or designing a change. "I'm only investigating" is not a reason to skip — the skill is
there to inform the investigation.

**Load `skills-writer` before creating or editing any guidance file** — a skill, an agent file, or
this `CLAUDE.md`. It owns the rules for what belongs in one (and what does not).

When a task surfaces an improvement to a guidance file, include that change in the **same PR** as
the work that motivated it. Don't ask, and don't split it out.

## Building and Testing

Build the individual `.csproj` for the platform you touched rather than the whole solution — the
projects share core source via linked includes, so a change in `src/SkiaGameRendering/` or either
`Core.*` project reaches several packages at once. `.github/workflows/master.yml` is the
authoritative list of what CI builds, runs and in what order.

**CI is the regression gate. Locally, run only what you changed, plus what CI cannot run.**

- Run the test classes you added or changed plus a short named list of directly related classes,
  in one test project: build it once, then
  `dotnet test tests/<Project>/<Project>.csproj --no-build --filter "FullyQualifiedName~ClassA|FullyQualifiedName~ClassB"`.
  Never run a whole project, a namespace, `tests/Tests.proj` (CI's full run) or the solution, and
  never run the suite on `master` and the branch to compare results.
- The same rule covers every other runner: sample `--smoke-test` exes, the Playwright specs
  (`npx playwright`), `Tests.Godot`, and the Android emulator. CI runs all of them on every push
  (`master.yml`), so don't run them locally, and don't install their toolchains (Playwright
  browsers, Godot, the wasm workload) to verify a change. Read CI's result with `gh pr checks`
  and `gh run view --log-failed`; a golden mismatch uploads a `golden-image-diffs-*` artifact.
- Run locally only what no CI job runs: Unity (below), and anything else the change touches that
  no `master.yml` job exercises. Say in the PR which of those you ran.
- Every command finishes in about 4 minutes; set the tool timeout to match. A command that was
  killed or timed out is narrowed, never rerun unchanged. An agent stops at about 25 minutes and
  reports what it did and what it did not verify.
- A brief for another agent names test classes, never namespaces, projects or "every backend",
  and asks it to report how long each build and test step took. A brief that says "regenerate on
  every backend this machine can run" overrides these rules and costs a quarter hour, so don't
  write one.
- A PreToolUse hook (`.claude/hooks/block-full-test-runs.sh`, on Bash and PowerShell) refuses a
  `dotnet test` that does not name one test `.csproj`, has no `--filter`, or filters on a
  namespace or a bare word. Prefix `SGR_ALLOW_FULL_TESTS=1` (PowerShell:
  `$env:SGR_ALLOW_FULL_TESTS=1;`) only when the user asks for a full run. The hook sees only
  `dotnet test`; the other runners above are enforced by nothing but these rules.
  `bash .claude/hooks/block-full-test-runs.test.sh` tests the hook.
- CI never runs Unity, by choice: the editor is a multi-GB download per run. Verify Unity changes
  locally with the commands below, and say in the PR which platforms were run.
- The Unity sample needs no editor window. First run `eng/build-unity-package.ps1`, then
  `Unity.exe -batchmode -quit -projectPath samples/Sample.Unity -executeMethod SampleBuild.Build`,
  then `samples/Sample.Unity/Build/Mono/Sample.Unity.exe --smoke-test`, which exits 0 only if the
  pixels are right. Unity locks a project that is open in the editor, so the batch build fails then.
  Add `-d3d12` to build a Direct3D 12 player (`Build/Mono-D3D12`), `-vulkan` for Vulkan (`Build/Mono-Vulkan`) and `-linear` to build a Linear color space player (`Build/Mono-Linear`); the committed project
  is Gamma, so revert the `ProjectSettings/` changes that build saves. Pass `--msaa` to the player (`Sample.Unity.exe --smoke-test --msaa`) for 4x MSAA on Direct3D 11 and OpenGL; Direct3D 12 and Vulkan refuse multisampled targets, which the same run checks (it expects the refusal and no Skia pixels). Builds rewrite the graphics API list in `ProjectSettings/`, which the Editor tests then follow, so revert it and pass `-force-d3d11`, `-force-glcore`, `-force-d3d12` or `-force-vulkan` to pick the API they run on. On macOS the same commands
  run with `Unity.app/Contents/MacOS/Unity` and build a Metal player, `Build/Mono/Sample.Unity.app`.
- The Metal, D3D12 and Vulkan plugins (`unity/native/SkiaUnityMetal`, `SkiaUnityD3D12`, `SkiaUnityVulkan`) are committed prebuilt. After editing one, run
  `eng/build-unity-metal-plugin.sh`, `eng/build-unity-d3d12-plugin.ps1` or `eng/build-unity-vulkan-plugin.ps1` and then `eng/build-unity-package.ps1`, or Unity keeps loading
  the old copy in the package's `Plugins/`.
- The Unity Editor tests (the package's `Tests/Editor`, which reload the domain and enter play mode)
  run with `Unity.exe -batchmode -projectPath samples/Sample.Unity -runTests -testPlatform EditMode
  -testResults <path>.xml`, without `-quit` or `-nographics`; exit code 0 means they passed.
- CI's `android` job runs the KNI Android smoke test on an x86_64 emulator with SwiftShader; see
  `docs/android/quickstart.md` for building and running it against your own device or emulator.
- WindowsDX and KNI WindowsDX need Windows; the WebGL sample needs `dotnet workload install wasm-tools-net8`.

`Directory.Build.props` sets `TreatWarningsAsErrors`, so a new C# warning fails the build. MSBuild
task warnings from third-party targets stay warnings; don't silence them there.

**When no CI job checks it, you may launch a sample `.exe`/`dotnet run` and screenshot it to verify objective output**: a
solid fullscreen color, a known static frame, a specific pixel value — by launching it, waiting for
it to render, and comparing a screenshot's pixels against the expected value; kill the process when
done. Prefer a small standalone verification app over the full interactive sample when one exists,
since a sample's camera/controls/gameplay still need a human's subjective judgment. For anything
needing that subjective read, keep to build-and-test and give the user numbered manual steps instead.
Launched verification and benchmark apps run hidden or in a small window, never fullscreen and never
taking focus. A MonoGame game driven by `RunOneFrame` in a loop never shows its window, yet its back
buffer still renders at full size (`benchmarks/Benchmarks.DirectDraw.DesktopGL`); a hidden window is
never active, so set `InactiveSleepTime` to zero or every frame sleeps 20 ms.

## Releasing

Every package, the NuGet packages and the Unity package alike, ships together at one version from a
single `publish.yml` run. Never add an option to publish one platform or package on its own.
`SkiaGameRendering.DesktopVK` is the one exception for now: it is not packed, and stays at 0.17.4-preview on
SkiaSharp 3.119.4, until MonoGame creates its Vulkan instance at 1.1 or higher
(https://github.com/MonoGame/MonoGame/issues/9588). Re-add its pack step in `publish.yml` and its
`monogame-native-windows` matrix entry in `master.yml` then.

`LibraryVersion` in `eng/Versions.props` is only a default; the `version` input of `publish.yml`
sets the real one. To find the last published version of a package, read
`https://api.nuget.org/v3-flatcontainer/<lowercase-id>/index.json`.

## Gotchas

- **`gh` defaults to the wrong repo.** This repo is a fork of `mfigueirido/SkiaMonoGameRendering`,
  so `gh pr create`/`gh issue create` without an explicit target silently resolve to that upstream
  parent instead of `vchelaru/SkiaGameRendering`. Always pass `--repo vchelaru/SkiaGameRendering`
  (and `--base`/`--head` for PRs) rather than relying on the default.
- **A backend is only verifiable on its own platform and GPU stack.** A change to shared core source
  compiles for every backend but is exercised by none of them until each platform actually runs.
  Say which backends you verified and which you did not, rather than implying a clean build covers all.
- **"Not verified" means neither CI nor this host ran it.** A platform a `master.yml` job runs is
  verified by that job, so a PR does not list it as unverified and does not rerun it locally. What
  only a local run covers is Unity: when the change can reach Unity (shared sample code, core
  source, the Unity package), run the Unity players this host can build, including `-d3d12` and
  `-linear` on Windows. Only Metal needs macOS.
- **NuGet audit advisories on Stride's own dependencies are not ours to fix.** Stride.Assets pulls in
  `SixLabors.ImageSharp` 3.1.12, which carries advisories. `eng/StrideAudit.props` suppresses them for
  every Stride project, so import it from any new one. Don't pin or upgrade ImageSharp; revisit only
  when a Stride release stops depending on it.
- **FNA is a submodule, not a package.** `external/FNA` (plus the five C#-binding submodules under
  its `lib/`) must be initialized before anything `Fna.*` builds; the checkout step in
  `.github/workflows/master.yml` is the exact command list. Its native DLLs are vendored under
  `external/fnalibs/` (see the README.txt there), and the D3D11 adapter depends on the layout of a
  struct inside that `FNA3D.dll` (see the MAINTENANCE NOTES on `SkiaAngleBackend` and `SkiaGlBackend`), so bump the two together.
- **Engine internals are reached by reflection, not a fork.** See
  `src/SkiaGameRendering.Kni.WebGL/WebGlCanvasUpload.cs` and `KniStateInvalidation.cs`. A MonoGame or KNI version bump can break
  these silently at runtime with no compile error. Every backend has reflection pin tests
  (`tests/Shared/EngineReflectionPin.cs` plus a `*ReflectionTests.cs` in each backend's test
  project) that fail the build on a rename.
- **MonoGame WindowsDX12's direct canvas reads MonoGame's C++ objects by offset.** See
  `src/SkiaGameRendering.WindowsDX12/MonoGameD3D12Native.cs`. It only turns on for an `mgruntime.dll`
  whose SHA-256 is in its `KnownRuntimes`; on a MonoGame bump re-diff the structs against
  `native/monogame/directx12` in MonoGame/MonoGame, re-probe, then add the hash (`Tests.WindowsDX12`
  fails until you do).
- **FNA WebGL runs on FNA's SDL2 platform backend and needs .NET 10.** Mono's WebAssembly interpreter traps on pinvokes
  that take or return an enum with a 64-bit underlying type (SDL3's `SDL_WindowFlags`), so SDL3 cannot start, and the
  Emscripten version must match the .NET runtime's (net8 is 3.1.34, net10 is 3.1.56). `eng/build-fna-wasm-natives.sh`
  builds SDL2 and FNA3D; `src/SkiaGameRendering.Fna.WebGL` saves and restores all GL state around each Skia pass
  because FNA3D caches state and has no invalidate call.
