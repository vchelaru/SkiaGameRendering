# Option A per-frame cost benchmark (repo issue #12)

Measures the real per-frame CPU cost of the Option A architecture (Skia's Emscripten WASM GL
runtime registered directly into KNI's own `WebGL2RenderingContext`, no cross-context blit) as a
candidate replacement for today's shipped Option D (`SkiaWebGlBackend` + `WebGlCanvasUpload`'s
`texSubImage2D`/`texImage2D` blit - see `benchmarks/Benchmarks.WebGL` for that measurement).

This is a **spike, not production code** - see the `SPIKE ONLY` comments throughout. It exists to
answer one question: is Option A's per-frame handoff cost (state-cache invalidation + a Skia draw +
KNI's next draw) cheap enough to justify the architecture change, particularly as Firefox's rescue
path (Firefox misses the Option D upload budget by 60-300x per
`docs/webgl/performance-results.md` - an internal CPU readback during the cross-context blit).

## What it measures, per "frame"

1. `GraphicsDevice.InvalidateStateCache()` - a patched-KNI-only API (see **Prerequisites** below).
2. A Skia draw into KNI's real, shared WebGL2 context: a magenta clear + a filled, anti-aliased
   circle (`SKPaint`/`DrawCircle`) - deliberately not a bare `Clear()`, which does not exercise the
   shader/texture/buffer-binding corruption path `InvalidateStateCache()` exists to fix.
3. KNI's own next real draw call: a full-viewport pure-green `SpriteBatch` quad, reusing the exact
   same `SpriteBatch`/`Texture2D`/`BlendState.Opaque` objects on every call (see `BenchGame.cs`'s
   comment for why reuse matters - it's what defeats KNI's reference-equality dirty-flag cache).

Timed at 1920x1080, 2560x1440, and 3840x2160 - 60 warm-up frames then 300 measured frames per
resolution, same shape as `benchmarks/Benchmarks.WebGL/benchmark.js`. Each resolution is its own
page load (`?w=&h=` query parameters, see **Running it yourself** below) rather than one page
resizing 3 times: KNI's BlazorGL platform's `ChangeClientSize` is an empty stub in `kniEngine/kni`
v4.3.9001, so `GraphicsDeviceManager.ApplyChanges()` does not actually resize the real canvas on
this platform - an earlier version of this benchmark tried exactly that and silently kept
benchmarking the original 1920x1080 backing store while believing it had moved on to
2560x1440/3840x2160 (caught by the `correctness` readback below, not by inspection - it looked
identical to a passing run until the readback coordinates fell outside the real, unresized
framebuffer). See `BenchGame.cs`'s comment for the full account if you're extending this benchmark
and are tempted to add a live-resize path back in.

Each report includes:

- `timingsMilliseconds.frameCpu` - the whole JS-to-WASM call boundary around steps 1-3, timed from
  JS (`performance.now()`) - the number most comparable to Option D's "upload CPU" column.
- `timingsMilliseconds.innerCpu` - steps 1-3 only, timed inside C# with `Stopwatch`, excluding the
  call boundary.
- `timingsMilliseconds.uploadGpu` - `EXT_disjoint_timer_query_webgl2` wall time, Chromium only
  (`null`, not zero samples, on browsers that don't expose the extension - e.g. Firefox).
- `correctness.pureGreenAfterSequence` - a readback of the center pixel taken once per resolution,
  outside the timed loop, before any timing number is trusted. `false` means the state-cache fix
  did not do its job on this build/browser and every timing number in that resolution's report
  should be disregarded, not just distrusted.

## Prerequisites

This benchmark calls `GraphicsDevice.InvalidateStateCache()`, an API that only exists on a locally
patched KNI build - it is not in the real `nkast.Kni.Platform.Blazor.GL` NuGet package.
`Benchmarks.WebGLOptionA.csproj` points at `C:\kni-src` (a clone of `kniEngine/kni` tag `v4.3.9001`)
via `ProjectReference` instead of the usual `PackageReference`. If that path is missing or its
patch has been reverted:

1. Clone `https://github.com/kniEngine/kni`, checkout tag `v4.3.9001`, to `C:\kni-src`.
2. Apply the `InvalidateStateCache` patch described in `WebGL-KNI-Integration.md` section 6 (four
   files: `Platforms/Graphics/.BlazorGL/ConcreteGraphicsContext.cs`,
   `src/Xna.Framework.Graphics/Graphics/{GraphicsContext,GraphicsContextStrategy,GraphicsDevice}.cs`).

## Running it yourself (real hardware, non-headless)

Headless/CI runs of this benchmark (Playwright + headless Chromium/Firefox) are **correctness
signals only** - same policy as `docs/webgl/performance-results.md` states for Option D. For real
numbers, run this on the hardware you care about, in an actual browser window:

```
dotnet workload install wasm-tools-net8   # once, if not already installed
dotnet build benchmarks\Benchmarks.WebGLOptionA -c Release
dotnet run --project benchmarks\Benchmarks.WebGLOptionA -c Release --no-build --urls http://127.0.0.1:5098
```

Then, in Chrome, Edge, and/or Firefox, open each of these 3 URLs in turn (one page load per
resolution - see the note above on why there's no single "run all 3" button):

- `http://127.0.0.1:5098/?w=1920&h=1080`
- `http://127.0.0.1:5098/?w=2560&h=1440`
- `http://127.0.0.1:5098/?w=3840&h=2160`

For each one:

1. Wait for the status line to read "Ready." (this is the context-bridge handoff completing - if it
   instead reads "FAILED to initialize...", open devtools console for the `[optionA]`-prefixed
   diagnostic lines and see **Troubleshooting** below).
2. Click **Run this resolution** (~6-10 seconds for 60 warm-up + 300 measured frames). When it
   finishes, the status line reads "Done. correctness.pureGreenAfterSequence=..." and the same
   summary is logged to the devtools console (`[optionA] done: ...`).
3. **Before trusting the number**, confirm that status line says `pureGreenAfterSequence=true`. If
   it says `false`, the state-cache fix did not hold on this run and the timing numbers are not
   measuring what this benchmark claims to measure - re-check the `C:\kni-src` patch
   (**Prerequisites**) before re-running.
4. Below the status line, a **comparison table** appears automatically (no extra click) - Option
   D's already-published numbers (embedded from `docs/webgl/performance-results.md`, see
   **Comparing against Option D** below) right next to the row you just measured, for the browser
   this page detects itself running in, plus a plain-English verdict per resolution ("Option A is
   Nx faster/slower than Option D", and whether each clears the < 500 µs CPU / < 1 ms GPU budget).
   The table remembers every resolution you've run **in this browser** so far (via `localStorage`),
   so it fills in one more row each time you repeat this flow at a different `?w=&h=` - the row for
   whichever resolution the current page is showing is marked "(this page)".
5. Click **Export** to download this resolution's JSON report.

Repeat all 3 URLs per browser you want data for, then combine the 3 downloaded JSON files (they're
each a 1-element array in the same shape) into one list per browser. Firefox's reports will not
have a `timingsMilliseconds.uploadGpu` column (`null`) - it does not expose
`EXT_disjoint_timer_query_webgl2` by default. The on-page comparison table's `localStorage` history
is per-browser-profile (not shared across Chrome/Edge/Firefox, and not written to any file) - the
exported JSON files are still the durable record.

`package.json`/`playwright.config.js`/`tests/headless-run.spec.js` in this directory are a separate,
headless Playwright driver used only to get a quick, non-authoritative correctness+timing preview
(`npm install && npx playwright install chromium firefox && npm test`) - not part of the manual flow
above, and not a substitute for it.

## Troubleshooting

- **"FAILED to initialize the Option A context bridge"** - check the devtools console for which
  `[optionA]` line came last. `registerContext available=false` means this browser's Skia/Emscripten
  build doesn't expose the context registry at all (try a newer Chromium). `success=false` with a
  `registerContext returned a falsy handle` error means KNI's context was already lost or invalid
  when registration was attempted.
- **A blank page or a script 404 in devtools** - the `nkast.Wasm.*` script versions hardcoded in
  `wwwroot/index.html` must match `eng/Versions.props`'s `NkastWasmCanvasVersion` exactly (repo
  issue #14); check that first if you bumped `KniVersion` or rebuilt `C:\kni-src` against a newer
  KNI tag.
- **Build fails referencing `C:\kni-src\Platforms\Kni.Platform.Blazor.GL.csproj`** - see
  **Prerequisites** above; the patched clone is expected to already exist and build cleanly on its
  own (`dotnet build` inside `C:\kni-src`) before this project can reference it.

## Comparing against Option D

The on-page comparison table (see step 4 above) does this automatically - it needs no manual
cross-referencing of `docs/webgl/performance-results.md`. `wwwroot/js/option-d-baseline.js`
embeds that doc's Chrome/Edge/Firefox rows (the `texSubImage2D` path specifically - KNI's actual
production upload path, see that file's comment) plus the stated budget (upload CPU < 500 µs,
upload GPU < 1 ms) as fixed reference data; it is **not** re-measured by this benchmark. If
`docs/webgl/performance-results.md` is ever re-measured on different hardware, update
`option-d-baseline.js` by hand to match - there is no automated link between the two files.

Only Chrome, Edge, and Firefox have a baseline (matching that doc's own coverage); the table shows
a "no baseline published" message for any other browser's user agent, and Safari (Tier 2, never
measured for Option D either) will hit that path.

Once you've collected numbers, still add a new section to `docs/webgl/performance-results.md`
itself if you want the comparison to live somewhere durable/shared - the on-page table is a live,
per-browser-profile convenience, not a replacement for that doc.
