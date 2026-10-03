# SkiaGameRendering Repository Guidelines

## What Is This?

A library that lets MonoGame, KNI, FNA, raylib, and Stride applications render with SkiaSharp straight
into engine textures, with no CPU readback. `README.md` covers the public API (`SkiaRenderer`,
`SkiaRenderTarget2D`), the per-platform package list, and the backend-per-graphics-API architecture.
`SkiaGameRendering-Notes.md` has the deeper interop detail (ANGLE, D3D11 state management).

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
authoritative list of what CI builds and in what order; mirror it when deciding what to verify.

- Unit tests: `dotnet test tests/Tests.proj`, which runs every test project under `tests/`.
- CI never runs Unity, by choice: the editor is a multi-GB download per run. Verify Unity changes
  locally with the commands below, and say in the PR which platforms were run.
- The Unity sample needs no editor window. First run `eng/build-unity-package.ps1`, then
  `Unity.exe -batchmode -quit -projectPath samples/Sample.Unity -executeMethod SampleBuild.Build`,
  then `samples/Sample.Unity/Build/Mono/Sample.Unity.exe --smoke-test`, which exits 0 only if the
  pixels are right. Unity locks a project that is open in the editor, so the batch build fails then.
  Add `-d3d12` to build a Direct3D 12 player (`Build/Mono-D3D12`) and `-linear` to build a Linear color space player (`Build/Mono-Linear`); the committed project
  is Gamma, so revert the `ProjectSettings/` changes that build saves. On macOS the same commands
  run with `Unity.app/Contents/MacOS/Unity` and build a Metal player, `Build/Mono/Sample.Unity.app`.
- The Metal and D3D12 plugins (`unity/native/SkiaUnityMetal`, `unity/native/SkiaUnityD3D12`) are committed prebuilt. After editing one, run
  `eng/build-unity-metal-plugin.sh` or `eng/build-unity-d3d12-plugin.ps1` and then `eng/build-unity-package.ps1`, or Unity keeps loading
  the old copy in the package's `Plugins/`.
- The Unity Editor tests (the package's `Tests/Editor`, which reload the domain and enter play mode)
  run with `Unity.exe -batchmode -projectPath samples/Sample.Unity -runTests -testPlatform EditMode
  -testResults <path>.xml`, without `-quit` or `-nographics`; exit code 0 means they passed.
- WindowsDX and KNI WindowsDX need Windows; the WebGL sample needs `dotnet workload install wasm-tools-net8`.

`Directory.Build.props` sets `TreatWarningsAsErrors`, so a new C# warning fails the build. MSBuild
task warnings from third-party targets stay warnings; don't silence them there.

**You may launch a sample `.exe`/`dotnet run` and screenshot it to verify objective output** — a
solid fullscreen color, a known static frame, a specific pixel value — by launching it, waiting for
it to render, and comparing a screenshot's pixels against the expected value; kill the process when
done. Prefer a small standalone verification app over the full interactive sample when one exists,
since a sample's camera/controls/gameplay still need a human's subjective judgment. For anything
needing that subjective read, keep to build-and-test and give the user numbered manual steps instead.

## Releasing

Every package, the NuGet packages and the Unity package alike, ships together at one version from a
single `publish.yml` run. Never add an option to publish one platform or package on its own.

## Gotchas

- **`gh` defaults to the wrong repo.** This repo is a fork of `mfigueirido/SkiaMonoGameRendering`,
  so `gh pr create`/`gh issue create` without an explicit target silently resolve to that upstream
  parent instead of `vchelaru/SkiaGameRendering`. Always pass `--repo vchelaru/SkiaGameRendering`
  (and `--base`/`--head` for PRs) rather than relying on the default.
- **A backend is only verifiable on its own platform and GPU stack.** A change to shared core source
  compiles for every backend but is exercised by none of them until each platform actually runs.
  Say which backends you verified and which you did not, rather than implying a clean build covers all.
- **FNA is a submodule, not a package.** `external/FNA` (plus the five C#-binding submodules under
  its `lib/`) must be initialized before anything `Fna.*` builds; the checkout step in
  `.github/workflows/master.yml` is the exact command list. Its native DLLs are vendored under
  `external/fnalibs/` (see the README.txt there), and the D3D11 adapter depends on the layout of a
  struct inside that `FNA3D.dll` (see the MAINTENANCE NOTES on `SkiaFnaAngleBackend` and `SkiaFnaGlBackend`), so bump the two together.
- **Engine internals are reached by reflection, not a fork.** See
  `src/SkiaGameRendering.Kni.WebGL/WebGlCanvasUpload.cs`. A MonoGame or KNI version bump can break
  these silently at runtime with no compile error. Every backend has reflection pin tests
  (`tests/Shared/EngineReflectionPin.cs` plus a `*ReflectionTests.cs` in each backend's test
  project) that fail the build on a rename.
