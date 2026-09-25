# Godot quick start

Godot 4 is the first engine this library supports whose graphics device is reachable entirely
through public API: `RenderingDevice.GetDriverResource` hands out the raw Vulkan or D3D12 handles
and `RenderingServer.TextureGetNativeHandle` a GL texture, so `SkiaGameRendering.Godot` uses no
reflection at all. It is also the first engine here where one package covers three graphics APIs,
because Godot picks its renderer and driver at run time (project settings, a command-line switch,
or an automatic fallback), so the backend is chosen when the renderer initializes. Like the raylib and Stride adapters it does not go through
`SkiaBackend`/`SkiaRenderer` - see [SkiaGodotRenderTarget2D](../documentation/SkiaGodotRenderTarget2D.md)
for how it is structured.

## Prerequisites

- Godot 4.7 or newer, the .NET build (this package references `GodotSharp` 4.7.2 as a floor; a
  newer editor's own `Godot.NET.Sdk` unifies it upward)
- A C# Godot project targeting .NET 8 or newer
- Windows or Linux. macOS is not supported: there is no Metal interop here, and SkiaSharp's macOS
  native library is built without Vulkan, so MoltenVK is not an option either.
- One of: the **Forward+** or **Mobile** renderer on the **Vulkan** driver (Windows, Linux) or the
  **D3D12** driver (Windows); or the **Compatibility** renderer on the native
  `opengl3` driver (Windows; Linux X11). Metal, and the Compatibility renderer's ANGLE/Android/
  Wayland/macOS flavors, are not supported yet (see "Known limitations").

## Add the package

```powershell
dotnet add package SkiaGameRendering.Godot
```

Not on nuget.org yet. Until it is published, clone this repo and reference the project from your
game's `.csproj` instead; the package's dependencies (`SkiaSharp`, `GodotSharp`) restore normally:

```xml
<ItemGroup>
  <ProjectReference Include="path\to\SkiaGameRendering\src\SkiaGameRendering.Godot\SkiaGameRendering.Godot.csproj" />
</ItemGroup>
```

## Pick a supported driver

Godot 4.6+ configures **new** Windows projects for `d3d12`. D3D12, Vulkan and the Compatibility
renderer's `opengl3` all work with this package, so no setting needs changing on Windows or Linux.

`SkiaGodotRenderer.Initialize` throws with this instruction on an unsupported driver.
`--rendering-driver vulkan` (or `d3d12`; or `--rendering-method gl_compatibility` for OpenGL) on the
command line overrides the settings for one run.

## Initialize and render

Godot has no user-owned `Draw()` loop. A node draws Skia content into a `SkiaGodotRenderTarget2D`
in `_Process`, and the scene tree displays its `Texture` (a stock `Texture2DRD`, or an `ImageTexture` on the
Compatibility renderer) through any node
that takes a `Texture2D` - a `Sprite2D`, `TextureRect`, material, and so on. The same code runs on
every supported renderer:

```cs
using Godot;
using SkiaGameRendering.Godot;
using SkiaSharp;

public partial class SkiaOverlay : Node2D
{
    SkiaGodotRenderTarget2D? _canvas;
    readonly SKPaint _paint = new() { Color = SKColors.Crimson, IsAntialias = true };

    public override void _Ready()
    {
        // Optional - the render target auto-initializes on first use. Calling this explicitly
        // fails fast if the project is not on a supported rendering driver.
        SkiaGodotRenderer.Initialize();
        GD.Print($"Skia on {SkiaGodotRenderer.Driver}"); // "vulkan", "d3d12" or "opengl3"

        var size = GetViewportRect().Size;
        _canvas = new SkiaGodotRenderTarget2D((int)size.X, (int)size.Y);

        AddChild(new Sprite2D
        {
            Texture = _canvas.Texture,
            Centered = false,
            // Skia's output is premultiplied; Godot's default Mix blend expects straight alpha.
            // Only matters when the canvas has transparent areas.
            Material = SkiaGodotRenderTarget2D.CreatePremultipliedAlphaMaterial(),
        });
    }

    public override void _Process(double delta)
    {
        _canvas!.Begin();                       // clears to transparent by default
        _canvas.Canvas.DrawCircle(100, 100, 80, _paint);
        _canvas.End();                          // submits; the Sprite2D shows the result this frame
    }

    public override void _ExitTree()
    {
        _canvas?.Dispose();          // dispose render targets before...
        SkiaGodotRenderer.Dispose(); // ...tearing down the shared interop
        _paint.Dispose();
    }
}
```

`Begin`/`End`, the constructor, and `SkiaGodotRenderer.Initialize` must run on Godot's render
thread. Under the default `rendering/driver/threads/thread_model` ("Safe") that is the main thread,
so `_Ready`, `_Process` and `_Draw` all qualify. Under "Separate" (experimental in Godot), wrap them
in `RenderingServer.CallOnRenderThread`; they throw with that instruction otherwise. `Dispose` can
be called from either thread.

Full member list and other remarks are documented on
[SkiaGodotRenderTarget2D](../documentation/SkiaGodotRenderTarget2D.md).

## Build and run the sample in this repo

`samples/Sample.Godot` is a complete Godot project (`project.godot`, `Main.tscn`, `Main.cs`). Build
it with the .NET SDK like any other sample, then open or run it with a Godot 4.7 .NET editor
binary - this repo does not ship or download Godot:

```powershell
dotnet build samples\Sample.Godot\Sample.Godot.csproj -c Debug
<path-to>\Godot_v4.7.x-stable_mono_win64.exe --path samples\Sample.Godot --rendering-driver vulkan
<path-to>\Godot_v4.7.x-stable_mono_win64.exe --path samples\Sample.Godot --rendering-driver d3d12
<path-to>\Godot_v4.7.x-stable_mono_win64.exe --path samples\Sample.Godot --rendering-method gl_compatibility
```

Debug configuration matters: Godot loads the project assembly from `.godot/mono/temp/bin/Debug/`
when running a project outside an export. Passing `-- --screenshot out.png` makes the sample save
its fifth frame and quit, which is what `tests/Tests.Godot` uses, once per driver (set `GODOT_BIN`
to the Godot executable to enable that test; it skips otherwise).

## Known limitations

- **Vulkan and OpenGL are zero-copy; D3D12 is one GPU copy per frame.** Godot's D3D12 driver
  allocates every texture with a typeless format and Skia's D3D12 backend cannot render into
  those, so on D3D12 Skia draws into a typed texture this library owns and `End()` queues a
  `CopyResource` into Godot's texture. No CPU readback anywhere; `SkiaGodotRenderer.IsZeroCopy`
  tells you which path is active.
- **Compatibility renderer: native `opengl3` on Windows (WGL) and Linux X11 (GLX) only, RGBA8
  only.** Godot's `opengl3_angle` (ANGLE on Windows/macOS), `opengl3_es` (Android), Wayland and
  macOS native GL use EGL or NSOpenGL contexts this library has no platform code for; web exports
  cannot P/Invoke GL at all. `TextureRid` is invalid there (no RenderingDevice).
- **Metal is not supported** (no Metal interop in this library yet).
- **HDR 2D** (`rendering/viewport/hdr_2d`) is not compensated for: the texture is a plain UNORM
  format holding Skia's sRGB-encoded bytes, which is exactly right for Godot's default gamma-space
  2D pipeline.
- **On the RenderingDevice drivers, a one-time GPU stall per constructor, none per frame.** Godot
  tracks each texture's layout or state itself and SkiaSharp cannot be told which to leave a
  resource in, so the constructor runs a tiny fragment-shader pass and waits for it so Godot
  records the texture as sampled before Skia's first draw, and `End()` hands the texture back in
  that state with a small extra queue submission. Create targets up front, not per frame. See the
  documentation page for the full mechanism. (OpenGL has no layouts; none of this applies there.)
- **Vulkan without a dedicated transfer queue.** Godot submits texture uploads under a queue lock
  this library cannot take. When the GPU has no separate transfer queue (common on integrated,
  mobile and MoltenVK devices), those uploads share Skia's queue, and one made off the render thread
  (threaded resource loading) can race Skia's submit. `SkiaGodotRenderer.Initialize` prints a Godot
  warning when that applies; avoid loading textures off the render thread while Skia draws there.
- **`TextureRid` is for fragment-shader sampling only.** Binding it in your own CanvasItem or
  spatial fragment shader is fine; copying to or from it, clearing it, or using it as a storage
  image through `RenderingDevice` moves it out of the state this library keeps it in. On D3D12
  without enhanced barriers, sampling it from a vertex or compute shader does too.
- Verified on Windows with an NVIDIA GPU under Godot 4.7.2, on all three drivers, including clean
  runs under Godot's `--gpu-validation` (Khronos validation layer for Vulkan, the D3D12 debug layer
  for D3D12, GL debug output for OpenGL). That machine runs Godot's D3D12 driver with enhanced
  barriers; the legacy state-tracking path (older D3D12 runtimes) is implemented from Godot's source
  but has not been exercised. Linux is expected to work identically on Vulkan (same public API,
  same Skia Vulkan path Stride uses there) and on X11 OpenGL (the raylib adapter's GLX code), but
  has not been run. On macOS, Godot 4.7.2 on MoltenVK reaches Skia and fails there:
  `GRContext.CreateVulkan` returns null because SkiaSharp's macOS build has no Vulkan backend.
