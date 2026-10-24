# SkiaDirectCanvas

On MonoGame DesktopGL, KNI DesktopGL, MonoGame and KNI Android, KNI WebGL, MonoGame WindowsDX and WindowsDX12, KNI WindowsDX and FNA
(on its D3D11 or OpenGL driver), `SkiaDirectCanvas` draws into whatever the
engine is rendering to when `Begin()` is called, the back buffer or the bound `RenderTarget2D`, with
no intermediate texture and no `SpriteBatch` composite. It has the same `Begin`/`Canvas`/`End` shape as
`SkiaRenderTarget2D`, without the size or the texture:

```cs
_skia = new SkiaDirectCanvas(GraphicsDevice); // once, e.g. in LoadContent

spriteBatch.Begin(); /* background */ spriteBatch.End();
_skia.Begin();
_skia.Canvas.DrawCircle(100, 100, 50, paint);
_skia.End();
spriteBatch.Begin(); /* foreground */ spriteBatch.End();
```

`Begin()` does not clear; Skia draws on top of what is there. Coordinates are target pixels from the
top left; the engine's `Viewport` and scissor rectangle are ignored. Multisampled back buffers and render
targets work. Only one bound `RenderTarget2D` with `SurfaceFormat.Color` is supported; on the D3D11
platforms a back buffer whose texture has an sRGB format is refused too. `SkiaRenderer.SupportsDirectCanvas`
is false on every other platform, where the constructor throws `NotSupportedException`; use
`SkiaRenderTarget2D` there.

On KNI WebGL, Skia and KNI drive the one WebGL context, since WebGL cannot share objects between
contexts, and each pass resets KNI's cached GL state afterward; see the [WebGL quick start](../webgl/quickstart.md).

raylib on Windows and Linux (package `SkiaGameRendering.Raylib.OGL`) has a `SkiaDirectCanvas` of the same name, constructed with no arguments after
`Raylib.InitWindow`. Call `Begin`/`End` between `BeginDrawing` and `EndDrawing`, where it draws into the back
buffer, or inside `BeginTextureMode`, where it draws into that `RenderTexture2D` the right way up for raylib's
own drawing. `Begin` submits raylib's batched draws first. A render texture's Skia framebuffer is built for each
pass, since raylib gives no notice when one is unloaded; the back buffer's is reused. Not on macOS
(`SkiaRenderer.SupportsDirectCanvas` is false there). Dispose it before `SkiaRenderer.Dispose`.

```cs
var skia = new SkiaDirectCanvas();

Raylib.BeginDrawing();
Raylib.ClearBackground(Color.Black);
skia.Begin();
skia.Canvas.DrawCircle(100, 100, 50, paint);
skia.End();
Raylib.DrawText("on top", 10, 10, 20, Color.White);
Raylib.EndDrawing();
```

Stride on Direct3D 11 has the same thing as `SkiaDirectCanvas`. `Begin` takes the
`GraphicsContext`, where Stride keeps its current target, and `End` takes nothing; or set
`SkiaSceneRenderer.DirectCanvas` (see the [Stride quick start](../stride/quickstart.md)). Inside the compositor that target
is the back buffer. In Stride's default Linear color space the back buffer is an sRGB view of an
8-bit texture: Skia writes into it without encoding, so an `SKColor` shows as that color, and Skia
blends in sRGB space while Stride blends in linear. One bound 8-bit RGBA or BGRA target is supported;
sRGB-format textures and HDR targets throw `NotSupportedException`. Stride's Vulkan backend has no
direct canvas yet.

Stride on Direct3D 12 has `SkiaDirectCanvas`, used the same way, with the same color
handling, and with single-sample targets only (a multisampled one throws `NotSupportedException`).
`Begin` submits whatever Stride has recorded on the command list so far and starts a new recording,
because Skia's work reaches the GPU when `End` runs and would otherwise land under Stride's earlier
draws. That makes each pass cost a submission, so draw it once per frame rather than once per widget.
MonoGame's native WindowsDX12 has `SkiaDirectCanvas` too, with the Stride D3D12 limits (single-sample targets, no
multisampled back buffer) and the same per-pass submission cost, and no drawing with MonoGame between `Begin`
and `End`. MonoGame gives no way to get its current target's `ID3D12Resource` or to submit what it has recorded,
so this reads MonoGame's native objects by offset and is only enabled for a `mgruntime.dll` whose layout has
been verified (3.8.6); on any other build `SkiaRenderer.SupportsDirectCanvas` is false and
`SkiaRenderTarget2D` still works.
