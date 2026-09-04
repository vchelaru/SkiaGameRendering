# SkiaGameRendering Repository Guidelines

## What Is This?

A library that lets MonoGame, KNI, and raylib applications render with SkiaSharp straight into
engine textures, with no CPU readback. `README.md` covers the public API (`SkiaRenderer`,
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
- WindowsDX and KNI WindowsDX need Windows; the WebGL sample needs `dotnet workload install wasm-tools-net8`.

**Zero new warnings** after every change. **Never launch a sample `.exe`, `dotnet run`, or a GUI
app** to verify — build and test only. Anything that needs a window on screen is the user's manual
test; give them numbered steps.

## Gotchas

- **A backend is only verifiable on its own platform and GPU stack.** A change to shared core source
  compiles for every backend but is exercised by none of them until each platform actually runs.
  Say which backends you verified and which you did not, rather than implying a clean build covers all.
- **KNI internals are reached by reflection, not a fork** — see
  `src/SkiaGameRendering.Kni.WebGL/WebGlCanvasUpload.cs`. A KNI version bump can break these silently
  at runtime with no compile error.
