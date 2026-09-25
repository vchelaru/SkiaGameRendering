using Godot;
using SkiaGameRendering.Godot;
using SkiaSharp;
using SharedScene = Sample.Shared.Scene;

/// <summary>
/// The scene root (see Main.tscn). Godot has no user-owned Draw() loop the way MonoGame/raylib do:
/// a node draws Skia content into a <see cref="SkiaGodotRenderTarget2D"/> in <c>_Process</c>, and
/// the scene tree displays its <c>Texture</c> through an ordinary <see cref="Sprite2D"/> - the same
/// way a Godot project would show any other <see cref="Texture2D"/>.
///
/// Run from this folder with the Godot 4.7 .NET editor binary, after `dotnet build`:
///   godot --path . --rendering-driver vulkan     (or d3d12, or opengl3 with --rendering-method gl_compatibility)
/// Pass `-- --screenshot out.png` to save the fifth rendered frame to a PNG and quit, which is what
/// tests/Tests.Godot drives, once per driver, for an objective, no-human-in-the-loop pixel check.
/// </summary>
public partial class Main : Node2D
{
    SkiaGodotRenderTarget2D? _canvas;
    string? _screenshotPath;
    int _framesRendered;

    public override void _Ready()
    {
        // Optional: SkiaGodotRenderTarget2D auto-initializes on first use. Initializing explicitly
        // fails fast (with a clear stack) if the project is not on a supported rendering driver.
        SkiaGodotRenderer.Initialize();
        GD.Print($"SkiaGameRendering.Godot on {SkiaGodotRenderer.Driver} (zero-copy: {SkiaGodotRenderer.IsZeroCopy}, D3D12 enhanced barriers: {SkiaGodotRenderer.D3D12UsesEnhancedBarriers?.ToString() ?? "n/a"})");

        var size = GetViewportRect().Size;
        _canvas = new SkiaGodotRenderTarget2D((int)size.X, (int)size.Y);

        AddChild(new Sprite2D
        {
            Texture = _canvas.Texture,
            Centered = false,
            // Skia's output is premultiplied; Godot's default Mix blend expects straight alpha.
            Material = SkiaGodotRenderTarget2D.CreatePremultipliedAlphaMaterial(),
        });

        var userArgs = OS.GetCmdlineUserArgs();
        for (int i = 0; i < userArgs.Length - 1; i++)
        {
            if (userArgs[i] == "--screenshot")
                _screenshotPath = userArgs[i + 1];
        }
    }

    public override void _Process(double delta)
    {
        if (_canvas == null)
            return;

        _canvas.Begin();
        _canvas.Canvas.Clear(SKColors.CornflowerBlue);
        SharedScene.Draw(_canvas.Canvas, _canvas.Width, _canvas.Height);
        _canvas.End();

        if (_screenshotPath != null && ++_framesRendered == 5)
        {
            // The viewport texture holds the previous frame's presented image - by frame five the
            // Sprite2D has been drawn several times, so this captures steady-state output.
            var image = GetViewport().GetTexture().GetImage();
            var error = image.SavePng(_screenshotPath);
            GD.Print(error == Error.Ok ? $"Saved screenshot to {_screenshotPath}" : $"SavePng failed: {error}");
            GetTree().Quit(error == Error.Ok ? 0 : 1);
        }
    }

    public override void _ExitTree()
    {
        // Dispose render targets before tearing down the shared interop they depend on.
        _canvas?.Dispose();
        _canvas = null;
        SkiaGodotRenderer.Dispose();
    }
}
