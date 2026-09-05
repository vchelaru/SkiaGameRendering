using System.Runtime.Versioning;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Benchmarks.WebGLOptionA;

// Minimal KNI game: just enough to get a real GraphicsDevice bound to the browser's #theCanvas
// WebGL2 context (KNI's BlazorGameWindow binds to that element id by convention - see
// ConcreteGame.cs / BlazorGameWindow.cs in the patched KNI clone). Deliberately does not run its
// own Update/Draw loop or call Present - OptionAFrameRunner drives the measured sequence directly
// against GraphicsDevice/SpriteBatch, once, per JS-driven "frame" (see WebGL-KNI-Integration.md
// section 6 for why a full Game.Tick() would draw more than the 3 steps this benchmark measures).
//
// One resolution per page load, fixed at construction time - NOT resizable mid-session.
// BlazorGameWindow.ChangeClientSize (kniEngine/kni v4.3.9001, Platforms/Game/.Blazor/
// BlazorGameWindow.cs) is an empty stub, so GraphicsDeviceManager.ApplyChanges() does not actually
// resize the real #theCanvas DOM element on this platform - an earlier version of this benchmark
// tried a mid-session resize via that path and silently kept drawing into the original 1920x1080
// backing store while believing it was testing 2560x1440/3840x2160 (a correctness check caught it:
// reading back a pixel outside the real, unresized framebuffer returned (0,0,0,0)). Index.razor's
// ?w=&h= query parameters plus this constructor are the actual per-resolution mechanism -
// Pages/Index.razor.cs.OnAfterRenderAsync constructs one BenchGame per page load, sized to match
// the canvas element's own width/height attribute (which Index.razor already set from the same
// query parameters), so KNI creates its WebGL2 context at the real target size from the start.
[SupportedOSPlatform("browser")]
internal sealed class BenchGame : Game
{
    private readonly GraphicsDeviceManager _graphics;

    public BenchGame(int width, int height)
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferredBackBufferWidth = width,
            PreferredBackBufferHeight = height,
        };
    }

    public SpriteBatch? Batch { get; private set; }
    public Texture2D? GreenPixel { get; private set; }

    protected override void LoadContent()
    {
        // Never recreated after this - OptionAFrameRunner deliberately reuses this exact
        // SpriteBatch/Texture2D/BlendState.Opaque combination on every measured frame so a
        // no-op-looking redraw is indistinguishable, from KNI's point of view, from "nothing
        // changed since last time" (the scenario InvalidateStateCache has to defeat).
        Batch = new SpriteBatch(GraphicsDevice);
        GreenPixel = new Texture2D(GraphicsDevice, 1, 1);
        // Pure lime (0,255,0,255), not the XNA/KNI Color.Green preset (0,128,0,255) - makes the
        // pass/fail correctness check ("is this exactly pure green") unambiguous at a glance.
        GreenPixel.SetData(new[] { new Color(0, 255, 0, 255) });
    }

    protected override void Update(GameTime gameTime)
    {
    }

    protected override void Draw(GameTime gameTime)
    {
    }
}
