# Skia + MonoGame Interop on WebGL (KNI / Blazor WebAssembly)

Comprehensive notes on how to extend SkiaGameRendering's zero-copy Skia-to-MG-texture interop pattern to Blazor WebAssembly via KNI (the MonoGame fork that runs in the browser).

This is a sibling document to `SkiaGameRendering-Notes.md`, focused entirely on the WebGL/KNI target. The general background on the SkiaGameRendering library (mechanism, current OpenGL implementation, WindowsDX/ANGLE port) lives in that document; read sections 1–7 there before reading this.

## Implementation status (2026-07-10)

Option D is implemented in `SkiaGameRendering.Kni.WebGL` with a synchronous Skia host and a pinned public KNI canvas-upload patch. The integrated sample covers interleaving, render targets, shader sampling, Gum rendering, pointer/touch/wheel/text input, fractional DPR, upload-path diagnostics, context loss, and backend recreation. See `docs/webgl/validated-baseline.md` for the exact pins and the remaining browser/hardware acceptance gates.

---

## 1. Problem statement

KNI is a MonoGame fork that runs in Blazor WebAssembly with a WebGL backend. We want Skia content (Gum UI) to land in an MG `Texture2D` so it can interleave with `SpriteBatch` and feed into `RenderTarget2D`.

The non-negotiables:
- **Interleaving with `SpriteBatch` passes** — Gum content is often drawn *between* other MG draw calls in a frame, not as a final overlay.
- **Rendering into `RenderTarget2D`** — Gum is often rendered to a render target that's later used as a texture elsewhere in the frame.
- **Full-frame, every frame.** Not dirty-driven. The integration must run at 60 FPS at typical screen resolutions.

Browser-side overlay composition (two canvases stacked in the DOM) is therefore **not sufficient** — it only solves the "Skia on top" case, not interleaving and not RenderTarget consumption.

---

## 2. Why the desktop trick doesn't transfer

The desktop OpenGL implementation (`SkiaGameRendering` original) depends on `SDL_GL_SHARE_WITH_CURRENT_CONTEXT` — two GL contexts that **share object IDs but isolate state**. Each context has its own bound textures, programs, blend modes, viewport, etc.; switching via `MakeCurrent` save/restores state atomically as a driver primitive. State collision between MG and Skia is impossible because state lives in two different bags.

**WebGL has no equivalent.** WebGL 1 and WebGL 2 both forbid cross-context object sharing — spec-mandated, two reasons:
1. **Security.** Cross-origin resource sharing across GL contexts is a fingerprinting/information-leak surface the spec deliberately closed.
2. **Portability.** Not all WebGL implementations have a sane way to implement it. ANGLE on D3D11 would have to fake GL context sharing on top of D3D11. The spec said no.

So in WebGL, the only way two libraries can interop on the GPU is by **driving the same single context** — which collapses both libraries' state into one bag and means they collide.

The WindowsDX/ANGLE port hit the same collapse: ANGLE and MG both drove one D3D11 device context. The escape hatch there was `SwapDeviceContextState` — D3D11.1's API for atomically save/restoring all state across that one context. **WebGL has no equivalent of `SwapDeviceContextState`.**

### 2.1 The state-cache problem (the real obstacle, not state-setting)

When two libraries drive one context, the naive fear is "MG won't reset the state it needs and will draw with Skia's leftover state." That's not the failure mode. MG's `SpriteBatch.Begin` (and the "set the state I need" pattern generally) *does* re-set state. The problem is one level deeper.

MG (and KNI) cache the last state they applied:

```csharp
public BlendState BlendState {
    set {
        if (_cachedBlendState == value) return;  // ← elide redundant GPU call
        _cachedBlendState = value;
        ApplyToGpu(value);
    }
}
```

This is a standard perf optimization — state changes are expensive, redundant calls dominate normal workloads. The failure mode when a third party (Skia) mutates GPU state behind MG's back:

1. SpriteBatch.Begin sets `BlendState = AlphaBlend`.
2. Cache says "already AlphaBlend, no-op." Skips the GPU call.
3. Actual GPU state is whatever Skia left.
4. SpriteBatch.Draw queues sprites against the wrong blend mode.
5. Output is wrong or blank. **SpriteBatch ran correctly. The cache was the trap.**

This is exactly what bit the WindowsDX/ANGLE port (`SkiaGameRendering-Notes.md` §7). The "dirty flags" approach they tried first failed because invalidating MG's cache from outside required reflecting into a half-dozen private fields with no clean public API. `SwapDeviceContextState` won because it doesn't touch MG — it fixes the GPU side, leaving MG's cache accurate.

---

## 3. The four options

### A. Shared WebGL context (spiritual port of the desktop trick)

Force both KNI and SkiaSharp's WASM Skia build to drive *the same* `WebGL2RenderingContext`. Requires bridging Emscripten's GL context bookkeeping (SkiaSharp is Emscripten-compiled and uses `emscripten_webgl_make_context_current`) to KNI's JS-interop WebGL context, plus solving the state-cache problem (see §6).

Prerequisites: KNI must be on WebGL2, and the Emscripten context-injection trick must work. Originally estimated as weeks of debugging; with the KNI-fork state-cache fix (§6) the estimate drops materially but the context-bridging unknown remains.

### B. CPU readback

`gl.readPixels` from Skia's surface → `Texture2D.SetData(byte[])` on the MG side. Simple and correct but pays a CPU round-trip every frame: ~5–15 ms for 1080p RGBA, plus a GPU pipeline stall. **Rejected** — full-frame, every-frame paint requirement means dirty-driven mitigation doesn't apply.

### C. Two-canvas overlay composition (the Blazor SkiaGum host pattern)

Skia renders on its own canvas, browser composites it over the MG canvas. No interop at all. **Rejected** — doesn't support Gum-into-RenderTarget or Gum-between-SpriteBatch-passes.

### D. Cross-context GPU blit via `texImage2D(canvas)` — recommended path

WebGL allows `gl.texImage2D(TEXTURE_2D, 0, RGBA, RGBA, UNSIGNED_BYTE, sourceCanvas)` where the source is *another* WebGL-backed canvas. Browsers implement this as a GPU-side blit — no CPU readback, no context sharing.

Skia renders to its own canvas/context; each frame MG uploads that canvas into an MG-owned `Texture2D` via this call, then uses the texture normally (SpriteBatch source, render-target sampler, shader input). Interleaving works because MG controls when in the frame the upload happens.

This is the standard pattern PixiJS / Three.js / Babylon use for the same problem.

---

## 4. Comparison

| Concern | A (shared ctx) | B (readback) | C (overlay) | D (GPU blit) |
|---|---|---|---|---|
| Interleaving with SpriteBatch | ✓ | ✓ | ✗ | ✓ |
| Gum → RenderTarget2D | ✓ | ✓ | ✗ | ✓ |
| Per-frame cost @ 1080p | ~0 | 5–15 ms + stall | ~0 | 0.1–1 ms (Chrome/Edge confirmed) |
| Implementation cost | High (revised down — see §6) | Days | Days | Days |
| Depends on KNI internals | Heavily (incl. source patch) | Lightly | Not at all | Lightly (JS shim) |
| Depends on WebGL2 | Yes | No | No | No |
| Firefox support | TBD | Yes (slow everywhere) | Yes | Unknown — likely needs OffscreenCanvas path |

D is a copy, not zero-copy — but the copy is GPU-resident (compositor blit), not a CPU round-trip. The desktop pattern's true zero-copy was never available on WebGL regardless of effort, so D is the actual ceiling.

---

## 5. Option D mechanism

1. Skia owns an `HTMLCanvasElement` with its own WebGL context (use `SkiaSharp.Views.Blazor.SKGLView`, or `GRContext.CreateGl()` against an offscreen canvas you manage).
2. Per frame in MG's order of operations:
   - Trigger Skia paint → Skia draws to its canvas on the GPU.
   - `surface.Flush()` on Skia (or `gl.flush()` on its context) to ensure GPU work is submitted before MG samples it.
   - On MG's WebGL context: `gl.texImage2D(TEXTURE_2D, 0, RGBA, RGBA, UNSIGNED_BYTE, skiaCanvas)` into a pooled MG `Texture2D`.
   - Use that `Texture2D` like any other — draw via `SpriteBatch`, sample in shaders, blit into a `RenderTarget2D`.

### 5.1 Fast-path variant: `OffscreenCanvas` + `ImageBitmap`

In modern Chrome/Firefox/Edge:

```js
const bitmap = offscreenSkiaCanvas.transferToImageBitmap(); // zero-copy GPU-side
gl.texImage2D(TEXTURE_2D, 0, RGBA, RGBA, UNSIGNED_BYTE, bitmap);
```

`transferToImageBitmap` hands GPU-backed pixel ownership to an `ImageBitmap` without copying; `texImage2D` from `ImageBitmap` is the fastest cross-context path the spec offers. Most likely candidate to rescue Firefox if `texImage2D(canvas)` is slow there.

### 5.2 Gotchas

- **Premultiplied alpha and Y-flip.** Set `UNPACK_PREMULTIPLY_ALPHA_WEBGL` and `UNPACK_FLIP_Y_WEBGL` to match Skia's output before the upload call. Mismatch pushes the browser off the direct-blit fast path and into a conversion-shader pass.
- **Texture pooling.** Don't allocate a `Texture2D` per frame — pool one per upload size.
- **Sync point cost.** Skia `Flush` + `texImage2D` enforces ordering; cheap but measurable. Profile it.
- **Browser variance.** Chrome historically fastest, Firefox apparently a CPU-readback fallback (see §7), Safari fine on recent versions.
- **KNI JS interop surface.** There's no .NET API for `texImage2D(canvas)` directly. You'll write a small JS shim exposing "upload this Skia canvas into this MG texture handle." Need to extract MG's underlying `WebGLTexture` from a `Texture2D` via reflection into KNI internals (or via a KNI-side API change — see §8).
- **WebGL1 vs WebGL2.** Confirm which KNI uses. Option D works in both, but several details (pixel-pack alignment, supported internal formats) differ slightly. 5-minute check, worth doing first.

---

## 6. Option A reconsidered: KNI-side state-cache invalidation

The original Option A estimate ("weeks of debugging") priced in manually save/restoring every piece of GL state around every Skia submission, because that's how an outside-the-library integration would have to do it (WindowsDX team rejected the dirty-flag approach precisely because it required reflecting into MG's private fields).

KNI is different: **it's already a fork.** Adding a public method like `GraphicsDevice.InvalidateStateCache()` is a one-file patch in KNI's source. After that, the integration pattern becomes:

```csharp
skiaInterop.DrawIntoTexture(myTexture);
graphicsDevice.InvalidateStateCache();   // ← new KNI API
spriteBatch.Begin();                      // sees stale cache, applies everything correctly
spriteBatch.Draw(myTexture, ...);
spriteBatch.End();
```

The invalidation is the bridge. Every MG drawing block that follows already uses the "set the state I need" pattern; with the cache primed to "nothing applied," that pattern just works.

Implementation cost in KNI: roughly 20–30 lines of straightforward code in the WebGL backend — null out cached current-blend, current-depth, current-rasterizer, current-effect, current-sampler-array, current-vertex-buffers, current-index-buffer, current-bound-textures-per-slot, current-viewport, current-scissor. WebGL's state space is smaller than D3D11's, so less to enumerate than the WindowsDX equivalent would have been.

This **does not solve the other hard part of Option A** — making Skia's Emscripten-built WASM Skia and KNI's JS-interop WebGL talk to the same `WebGL2RenderingContext`. That unknown remains. But it removes the largest piece of the "weeks of debugging" estimate, and shifts Option A from "rejected for now" to "potentially viable fallback if D doesn't work on the browsers we need."

---

## 7. Spike v0 — status and findings

Built `spikes/webgl-blit-v0/` — a standalone static HTML page with two WebGL2 canvases on separate contexts, a per-frame `texImage2D(canvas)` upload, and a HUD reporting CPU/GPU cost. No KNI, no Blazor, no MonoGame — just the load-bearing cross-context upload question in isolation.

### 7.1 Initial results (single user machine, default upload path)

| Browser | 1920×1080 upload cost | Verdict for Option D |
|---|---|---|
| Chrome | ~0.25 ms | ✓ Fast path confirmed |
| Edge | ~0.25 ms | ✓ Fast path confirmed (same Blink + ANGLE/D3D11 stack as Chrome) |
| Firefox | ~25 ms | ✗ ~100× slower — almost certainly an internal CPU readback |

The Firefox number (~330 MB/s effective throughput for 8 MB) is consistent with a PCIe-bus readback round-trip, not a GPU blit. Initial guess was that Firefox would be close behind Chrome. It isn't.

### 7.2 Why Chrome/Edge are fast and Firefox isn't

Chrome and Edge both use Blink + ANGLE/D3D11 on Windows. Cross-context `texImage2D(canvas)` between two ANGLE-backed WebGL contexts can use D3D11 shared-resource paths internally — a real GPU blit, no bus crossing.

Firefox uses its own stack (WebRender + its own GL implementation). The 25 ms figure is consistent with the browser internally doing `readPixels` → CPU buffer → `texImage2D` upload, treating the cross-context upload as if it were a foreign data source.

### 7.3 Spike extended with four alternative upload paths

1. `texSubImage2D(canvas)` — avoid re-allocating destination storage.
2. `createImageBitmap(canvas) → texImage2D(ImageBitmap)` — async path with an explicit "going to a texture" hint.
3. `OffscreenCanvas + transferToImageBitmap → texImage2D(ImageBitmap)` — the ownership-transfer fast path; spec wording supports zero-copy GPU handoff.
4. `readPixels → texImage2D(Uint8Array)` — Option B baseline. If Firefox matches the canvas path on this, that's confirmation it's already doing readback internally.

**Not yet measured:** Firefox numbers on the four alternative paths. Most likely candidate to rescue Firefox is path 3 (`OffscreenCanvas + transferToImageBitmap`).

### 7.4 Why GPU blit isn't *instant*

A pure intra-VRAM blit of 8 MB (1080p RGBA) is ~20–80 µs at modern VRAM bandwidth. The "0.1–1 ms" Chrome/Edge number is overhead around the copy, not the copy itself:

1. **GPU sync / fence** — destination can't sample source until source's pending draws finish.
2. **Cross-process IPC** — Chrome's renderer process serializes commands to the GPU process; ~50–200 µs per `texImage2D` regardless of payload.
3. **Driver-side validation** — format compatibility, dimension checks, completeness state.
4. **Possible format-conversion shader pass** — if premultiply alpha or Y-orientation differ, browser inserts a fullscreen shader pass.
5. **Possible intermediate copy** — some browser/driver combos stage through a transient texture; rare in practice.

`transferToImageBitmap` skips most of this because it's an explicit ownership transfer — no per-frame sync fence needed.

---

## 8. KNI-side changes worth making

Forking KNI inverts the calculus: anything that's painful to do from outside becomes a small targeted patch from inside.

### 8.1 For Option D (cross-context blit)

- **`Texture2D.UploadFromCanvas(IJSObjectReference canvasRef)`** — wraps the `texImage2D(canvas)` JS call and the pixel-store toggles (premultiply, flipY). Means consumer code never touches JS interop directly. Pair with `UploadFromImageBitmap` for the `OffscreenCanvas + transferToImageBitmap` path.
- **Internal texture pooling** for the upload destination so you don't allocate `Texture2D` every frame. KNI can manage the pool.
- **Expose the underlying `WebGLTexture` handle.** Some integrations want to do the upload themselves from JS — having a way to get the native handle from a `Texture2D` removes a layer of indirection.

### 8.2 For Option A (shared context)

- **`GraphicsDevice.InvalidateStateCache()`** (see §6). Single most valuable change. Without it, you're either save/restoring all GL state manually or reflecting into KNI's private fields.
- **Expose the `WebGL2RenderingContext` JS reference.** Needed so Skia can be initialized against KNI's context rather than its own. Probably an `IJSObjectReference` property on `GraphicsDevice`.
- **`Texture2D` factory that wraps an externally-owned `WebGLTexture`.** Needed so a Skia-allocated texture can be presented to MG as a `Texture2D` for sampling.
- **`BeginInterop()` / `EndInterop()` bracket.** Optional sugar on top of `InvalidateStateCache`. Bundles flushing pending MG batches, invalidating the cache, and re-establishing ambient state.

### 8.3 General quality-of-life

- **WebGL2 minimum.** If KNI currently supports both WebGL1 and WebGL2, mandating WebGL2 simplifies the Skia interop (Skia's WASM GL backend wants WebGL2 anyway).
- **Eager texture allocation.** WindowsDX hit this — MG defers actual GPU resource creation until first use; §7 of the rendering notes worked around it with a forced `SetData(new byte[...])`. If forking KNI, just make texture allocation eager so the WebGL handle exists from construction.

None of these are large patches individually. Most are 10–50 lines. The strategic point is that **most of them are useful whether you take path A or D**, so it's worth landing them in the KNI fork early and treating them as the public integration surface.

---

## 9. Upstreaming strategy (if pursuing PRs to KNI)

Mixed bag — some changes are legitimately upstreamable and arguably should be upstream regardless of your use case; others are the kind of thing maintainers usually push back on, with good reason.

### 9.1 Likely to be accepted upstream

**`BeginInterop()` / `EndInterop()` bracket (and the `InvalidateStateCache()` it wraps).** This isn't Skia-specific or WebGL-specific — it's a general "external renderer touched the GPU, MG is resuming" problem. ImGui-on-MG integrations have hit it. Native plugins have hit it. The WindowsDX ANGLE work in this repo hit it. **MonoGame proper has the same problem and no solution for it.** A clean public API for this benefits the broader ecosystem.

Pitch: frame it as "external renderer interop is a recurring sharp edge and here's a proper API." Reference the MG WindowsDX ANGLE pattern as prior art. D3D11's `ClearState()` is public for exactly this reason; this is the MG equivalent.

Acceptance odds: good, if framed generally.

### 9.2 Borderline — accepted as Blazor-specific extensions

**`Texture2D.UploadFromCanvas(IJSObjectReference)`.** Platform-specific, but KNI is itself Blazor-targeted, so platform-specific APIs aren't out of bounds.

Pitch: it's the canonical way for Blazor games to consume browser-side rendered content (Skia, OffscreenCanvas, video, future WebGPU output). Alternative is JS interop boilerplate every consumer reinvents.

Acceptance odds: moderate.

**`Texture2D` factory wrapping an existing `WebGLTexture`.** Same shape, narrower use cases.

Acceptance odds: lower than `UploadFromCanvas`.

### 9.3 Likely to be pushed back on

**Exposing the raw `WebGL2RenderingContext`.** Maintainers correctly resist "leak the backend" APIs because they couple every future internal change to external consumers. KNI today uses WebGL; tomorrow it might use WebGPU; an exposed-context API freezes that decision.

Better-framed alternative the maintainer might suggest (and that you should preemptively propose): an `IExternalRenderer` interface or a callback hook — `RegisterExternalRenderer(action)` — where KNI manages the context internally and gives the external renderer a controlled window to draw. Same use case served, abstraction intact.

Acceptance odds: low for raw exposure, moderate if reshaped as a callback/interface.

**Eager texture allocation as the default.** Probably not — the deferred behavior likely exists for a reason. Pitch as an opt-in (`new Texture2D(..., allocateEagerly: true)`) rather than a default change.

Acceptance odds: very low as default, moderate as opt-in.

**Mandating WebGL2.** Almost certainly not, unless KNI is already planning to drop WebGL1. Breaking changes to platform support are expensive.

### 9.4 Practical strategy

1. **Start with `InvalidateStateCache` (or the `BeginInterop`/`EndInterop` bracket).** Most general, most defensible, most likely to land. Use the PR conversation to gauge how receptive the maintainer is to interop changes generally.
2. **Based on that signal, decide whether to pitch the platform-specific ones.** If the maintainer engaged enthusiastically, follow up with `UploadFromCanvas`. If skeptical, save effort and keep those downstream.
3. **Pitch the raw-context exposure last, if at all, and reshaped as a callback hook.** Or keep it in your fork — smallest surface, cheapest to maintain downstream.
4. **For anything not accepted, keep a thin patch in your KNI fork.** Two or three rejected changes still means a patch of a few hundred lines, materially less than a hard fork.

### 9.5 Conversation posture

Maintainers usually have stronger opinions about *API shape* than about *whether the use case is legitimate*. Coming in with "here's what I need and one concrete proposal, but I'm open to other shapes that solve the same problem" gets further than "please merge this PR." Especially for the borderline ones — the maintainer probably has views about how interop should look that are worth surfacing before you've written 500 lines in a specific shape.

---

## 10. Where to pick up

1. **Firefox upload path** — the v0 spike (now removed; see §7 above for its findings) never finished measuring Firefox on the four alternative upload paths at 1080p / 1440p / 4K. Particularly want path 3's result (`OffscreenCanvas + transferToImageBitmap`). This question is now tracked as part of the hardware-acceptance benchmark issue rather than a standalone spike. Outcomes:
   - If path 3 lands in 0.5–2 ms on Firefox: Option D works everywhere by routing Skia through an `OffscreenCanvas` source.
   - If every path is multi-ms in Firefox: Option D is Chromium-only; Firefox either falls back to Option B or is unsupported.
2. **v1** (after v0 conclusive) — rerun upload measurement with the destination being a real KNI MG canvas. Requires:
   - A working KNI Blazor WASM project that renders something with `SpriteBatch` (any colored quad).
   - KNI version being targeted.
   - WebGL1 vs WebGL2 confirmation (`gl.getParameter(gl.VERSION)` in devtools).
3. **v2** (after v1) — interleaving with `SpriteBatch` and `RenderTarget2D`. Easiest if forking KNI with the changes in §8 — with `UploadFromCanvas` and the state-cache hook, v2 is straightforward. Without those, requires reflection into KNI to extract `WebGLTexture` from `Texture2D`.

---

## 11. Out of scope

- Reflecting into KNI to extract `WebGLTexture` from `Texture2D` cleanly (use a hack initially or fork KNI; productionize when the path is confirmed).
- Format negotiation beyond RGBA8.
- Multiple simultaneous Skia surfaces.
- Hooking this into SkiaGum's existing renderer abstraction. The spike is about proving the upload path, not integrating it.
- Pointer/keyboard input wiring for the Blazor side.
- WebGPU. Not yet exposed by SkiaSharp.

---

## 12. Related documents

- `SkiaGameRendering-Notes.md` — broader context on SkiaGameRendering's architecture, desktop OpenGL implementation, WindowsDX/ANGLE port, and future D3D12 / Vulkan plans.
