---
name: compare-runtime-renders
description: Comparing the shared Scene across runtimes by pixel diff, not by eye. Triggers: --screenshot, eng/compare-renders.py, "do MonoGame/Stride/Brutal/raylib render the same", golden-free cross-engine checks.
---

# Compare Runtime Renders

Two screenshots that look the same can differ by a wrong alpha, a color-space shift or a missing clear. Capture each runtime's Scene (`samples/Shared/Scene.cs`, 800x800 on `Scene.Background`) and diff the PNGs with `eng/compare-renders.py`; never judge by eye or from a contact sheet.

## Captures

Each command writes a PNG. Run it in the foreground with a timeout and give it a unique output path. Build the individual sample `.csproj` in Release first.

| Runtime | Command | What the PNG is |
|---|---|---|
| MonoGame DesktopGL | `Sample.MonoGame.DesktopGL --screenshot <p>.png` | `<p>.png` is the Skia surface (Scene only, transparent background); `<p>-backbuffer.png` is the composited back buffer. Compare the back buffer. |
| raylib OGL | `Sample.Raylib.OGL --screenshot <p>.png` | `LoadImageFromScreen`: the back buffer. Window hidden. |
| Stride VK | `Sample.Stride.VK --screenshot <p>.png` | `<p>.png` is `Presenter.BackBuffer` after the composite; `<p>-skia.png` is the Skia texture alone. |
| Brutal | `Sample.Brutal --screenshot <p>.png` | The Skia target. BRUTAL has no engine frame, so there is nothing else to read. |

Brutal and Stride are `net10.0`, MonoGame and raylib `net8.0`: run `dotnet <sample>.dll --screenshot ...` from the project's `bin/Release/<tfm>/`.

## Diff

`python -I eng/compare-renders.py ref.png other.png [more.png] [--tol N] [--diff-dir d]` compares every file to the first. Per file it prints differing pixels, max channel delta, max delta among pixels opaque in both, and an md5 (equal md5 means a byte-identical file). Exit 1 when the max delta exceeds `--tol` (default 1). `--diff-dir` writes a white-on-black mask of changed pixels; look at where they sit (which cell of the Scene) before guessing a cause.

## Tolerance

- Runtimes on the same graphics API (Stride VK vs Brutal, both Vulkan): byte-identical, `--tol 0`.
- Different graphics APIs on the same GPU (MonoGame DesktopGL, OpenGL, vs Vulkan): at most 1 per channel. The measured gap is about 18k of 640k pixels, nearly all in the gradient and SVG cells, so it is Skia's gradient rasterization on another API (inferred; not isolated).
- More than 1 is a bug. A wrong alpha in raylib reads 64; Stride Linear without the sRGB-copy composite reads up to 68.

## Landmines

- Check which image a capture is. A Skia-target capture (Brutal, `-skia.png`, MonoGame's plain `<p>.png`) proves Skia drew right, not that the engine's composite did. Only the back buffer captures prove the composite; for Stride VK they must equal `-skia.png`.
- MonoGame's plain `<p>.png` has no background and goes through a premultiplied round trip, so it differs from every other capture by up to 255 on transparent pixels. Do not diff it.
- Stride defaults to the Linear color space, where the back buffer is sRGB-format; see the Linear and Gamma note in `docs/stride/vulkan-quickstart.md`. Capture on a build that has the sRGB-copy composite.
- A capture is only independent if it came from a fresh process: run twice and compare md5s, as a same-path or cached file would hide a stale result.
