# WebGL troubleshooting

## Host is not ready

Render `SkiaGameWebGlHost` first and await `Ready` before constructing the backend. Initialization fails deliberately instead of racing the first Blazor render.

## WebGL2 unavailable

Confirm hardware acceleration is enabled and inspect the renderer string shown by the sample. The supported baseline does not silently fall back to CPU rasterization or WebGL1.

## Black or inverted frame

The default path preserves the browser canvas Y orientation (`FlipY = false`), uses premultiplied alpha, and disables browser color-space conversion. Do not compensate again in SpriteBatch. Run the asymmetric benchmark pattern before changing these flags.

## Incorrect transparent edges

Use `BlendState.AlphaBlend` for the premultiplied Skia texture. `BlendState.NonPremultiplied` produces dark or bright antialiased fringes.

## Context lost

The host releases Skia GPU wrappers on `webglcontextlost` and recreates them on restore. Pause calls to your `SkiaRenderTarget2D`'s `Begin()`/`End()` while `host.IsContextLost` is true. KNI recreates its own destination resources through its graphics lifecycle.

## Trimming or AOT failure

Initialize the backend explicitly. Do not rely on assembly scanning in a trimmed browser build. Ensure `WasmBuildNative` is true and `wasm-tools-net8` is installed so `libSkiaSharp.a` is linked.

## Firefox or Safari is slow

Run `Benchmarks.WebGL` for all paths and export JSON. Do not infer an internal readback from throughput alone. A mandatory browser that misses the budget triggers the separately designed shared-context Option A work; there is no silent managed-buffer fallback.
