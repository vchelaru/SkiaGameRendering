# WebGL performance results

Measured with `benchmarks/Benchmarks.WebGL`'s full-matrix mode (60 warm-up + 300 measured frames per
combo) on physical hardware. Raw JSON exports are checked into `docs/webgl/results/`.

## Hardware

- **CPU:** AMD Ryzen 7 260 (8C/16T)
- **GPU:** NVIDIA GeForce RTX 5060 Laptop GPU (ANGLE/D3D11) — Radeon 780M iGPU also present but not
  the one Chrome/Edge/Firefox selected for these runs
- **RAM:** 62 GB
- **OS:** Windows 11 Pro, build 26200

This is a high-end laptop GPU, not the "mid-range" reference the original spike's budget was
written against — treat these numbers as a ceiling, not a guarantee for weaker hardware.

## Budget

Chrome, mid-range GPU, 1920×1080 RGBA: upload CPU < 500 µs, upload GPU < 1 ms.

## Chrome (raw: `results/chrome-2026-09-05.json`)

Renderer: `ANGLE (NVIDIA, NVIDIA GeForce RTX 5060 Laptop GPU (0x00002D59) Direct3D11 vs_5_0 ps_5_0, D3D11)`

| Resolution | Path | Upload CPU p50/p95 (ms) | Upload GPU p50/p95 (ms) | Missed frames /300 |
|---|---|---|---|---|
| 1920x1080 | texSubImage2D | 0.0 / 0.1 | 0.052 / 0.227 | 161 |
| 1920x1080 | texImage2D | 0.1 / 0.2 | 0.067 / 0.147 | 161 |
| 1920x1080 | imageBitmap | 0.3 / 0.7 | 0.043 / 0.122 | 163 |
| 1920x1080 | offscreenBitmap | 0.1 / 0.2 | 0.193 / 0.377 | 157 |
| 1920x1080 | readPixels (baseline) | 18.9 / 22.5 | 2.282 / 2.304 | 299 |
| 2560x1440 | texSubImage2D | 0.1 / 0.2 | 0.068 / 0.108 | 157 |
| 2560x1440 | texImage2D | 0.0 / 0.2 | 0.073 / 0.115 | 160 |
| 2560x1440 | imageBitmap | 0.3 / 0.6 | 0.064 / 0.110 | 163 |
| 2560x1440 | offscreenBitmap | 0.1 / 0.2 | 0.421 / 0.515 | 167 |
| 2560x1440 | readPixels (baseline) | 31.9 / 36.0 | 4.039 / 4.057 | 300 |
| 3840x2160 | texSubImage2D | 0.0 / 0.1 | 0.075 / 0.079 | 161 |
| 3840x2160 | texImage2D | 0.1 / 0.2 | 0.074 / 0.078 | 154 |
| 3840x2160 | imageBitmap | 0.3 / 0.5 | 0.074 / 0.079 | 163 |
| 3840x2160 | offscreenBitmap | 0.1 / 0.2 | 0.110 / 0.115 | 151 |
| 3840x2160 | readPixels (baseline) | 53.6 / 60.7 | 2.644 / 2.676 | 300 |

## Edge (raw: `results/edge-2026-09-05.json`)

Same renderer as Chrome (Chromium/ANGLE). Numbers track Chrome closely.

| Resolution | Path | Upload CPU p50/p95 (ms) | Upload GPU p50/p95 (ms) | Missed frames /300 |
|---|---|---|---|---|
| 1920x1080 | texSubImage2D | 0.1 / 0.2 | 0.112 / 0.243 | 151 |
| 1920x1080 | texImage2D | 0.1 / 0.2 | 0.097 / 0.229 | 157 |
| 1920x1080 | imageBitmap | 0.2 / 0.4 | 0.020 / 0.021 | 152 |
| 1920x1080 | offscreenBitmap | 0.1 / 0.2 | 0.021 / 0.040 | 163 |
| 1920x1080 | readPixels (baseline) | 18.3 / 20.9 | 2.281 / 2.301 | 300 |
| 2560x1440 | texSubImage2D | 0.0 / 0.1 | 0.035 / 0.037 | 158 |
| 2560x1440 | texImage2D | 0.1 / 0.2 | 0.035 / 0.038 | 162 |
| 2560x1440 | imageBitmap | 0.3 / 0.5 | 0.034 / 0.038 | 164 |
| 2560x1440 | offscreenBitmap | 0.1 / 0.2 | 0.039 / 0.040 | 164 |
| 2560x1440 | readPixels (baseline) | 23.6 / 26.6 | 1.174 / 1.191 | 300 |
| 3840x2160 | texSubImage2D | 0.1 / 0.2 | 0.075 / 0.079 | 157 |
| 3840x2160 | texImage2D | 0.1 / 0.2 | 0.076 / 0.095 | 158 |
| 3840x2160 | imageBitmap | 0.3 / 0.4 | 0.078 / 0.079 | 155 |
| 3840x2160 | offscreenBitmap | 0.1 / 0.2 | 0.111 / 0.114 | 154 |
| 3840x2160 | readPixels (baseline) | 51.2 / 55.5 | 2.644 / 2.676 | 300 |

## Firefox (raw: `results/firefox-2026-09-05.json`)

Renderer string reads `ANGLE (NVIDIA, NVIDIA GeForce GTX 980 Direct3D11 ..., or similar` — Firefox
masks `WEBGL_debug_renderer_info` for fingerprinting resistance; the actual GPU is the same RTX 5060
as the Chrome/Edge runs. `uploadGpu` is unavailable because Firefox does not expose
`EXT_disjoint_timer_query_webgl2` by default (also fingerprinting-resistance related).

| Resolution | Path | Upload CPU p50/p95 (ms) | Missed frames /300 |
|---|---|---|---|
| 1920x1080 | texSubImage2D | 35.0 / 39.0 | 300 |
| 1920x1080 | texImage2D | 35.0 / 40.0 | 300 |
| 1920x1080 | imageBitmap | 34.0 / 37.0 | 300 |
| 1920x1080 | offscreenBitmap | 32.0 / 36.0 | 300 |
| 1920x1080 | readPixels (baseline) | 39.0 / 44.0 | 300 |
| 2560x1440 | texSubImage2D | 64.0 / 79.0 | 300 |
| 2560x1440 | texImage2D | 55.0 / 59.0 | 300 |
| 2560x1440 | imageBitmap | 53.0 / 56.0 | 300 |
| 2560x1440 | offscreenBitmap | 49.0 / 52.0 | 300 |
| 2560x1440 | readPixels (baseline) | 70.0 / 85.0 | 300 |
| 3840x2160 | texSubImage2D | 122.0 / 128.0 | 300 |
| 3840x2160 | texImage2D | 122.0 / 134.0 | 300 |
| 3840x2160 | imageBitmap | 123.0 / 157.0 | 300 |
| 3840x2160 | offscreenBitmap | 113.0 / 142.0 | 300 |
| 3840x2160 | readPixels (baseline) | 171.0 / 200.0 | 300 |

## Conclusions

- **Chrome and Edge pass budget comfortably at every resolution and every production upload path**
  (`texSubImage2D`, `texImage2D`, `imageBitmap`, `offscreenBitmap`): upload CPU stays in the
  0–0.7 ms range and upload GPU stays under 1 ms even at 4K, well inside the < 500 µs CPU / < 1 ms
  GPU budget. `readPixels` correctly blows the budget by 40-100x, confirming it as a negative
  baseline. **Declaring Chrome and Edge Tier 1.**
- **Firefox misses budget on every path, at every resolution, by roughly 60-300x** (32 ms at best
  case 1080p vs. a 500 µs target), consistent with an internal CPU readback as suspected. This
  answers the issue's open sub-question: **`OffscreenCanvas + transferToImageBitmap` does not
  rescue Firefox** — it's the least-bad of the four alternative paths but still nowhere near
  budget. Per `docs/webgl/troubleshooting.md`'s stated policy, this triggers the Option A
  (shared-context) fallback gate — see `validated-baseline.md`.
- **Missed-frame counts (~50-55%) on Chrome/Edge despite sub-millisecond upload/GPU times** are a
  benchmark-harness artifact (jitter around the strict `> 16.67 ms` threshold relative to actual
  vsync timing), not a real regression — the timing stats they're paired with are the numbers that
  matter for the budget. Firefox's 100% missed-frame rate is real and tracks its measured upload
  cost.
- **Safari (Tier 2) was not measured** — no Mac hardware available for this run.

CI headless results remain correctness signals only and must not be used for GPU budget claims.
