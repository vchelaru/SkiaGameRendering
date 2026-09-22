using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;
using Sample.Shared;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;

namespace Sample.MonoGame.WindowsDX12;

/// <summary>
/// PROOF-OF-CONCEPT for MonoGame/MonoGame#9536 ("Exposing Native GPU Handles") and
/// MonoGame/MonoGame#9535 ("Wrapping External Texture in RenderTarget2D") together - the two PRs
/// SkiaGameRendering's WindowsDX12 glue is blocked on (issue #67). Renders with SkiaSharp directly
/// into a texture MonoGame's native WindowsDX12 platform then displays via SpriteBatch, every frame,
/// with no CPU readback.
///
/// Deliberately bypasses this repo's public <c>SkiaRenderer</c>/<c>SkiaBackend</c> API: no
/// <c>SkiaBackend</c> implementation exists for WindowsDX12 yet (that is the blocked work this
/// sample is meant to de-risk), so it drives <c>SkiaGameRendering.Core.D3D12</c> directly, the way
/// the eventual backend would.
///
/// KNOWN GAP: neither PR exposes a fence/semaphore for cross-call synchronization. This sample
/// relies on <see cref="D3D12SkiaSurfaceFactory.EndDraw"/>'s synchronous flush (blocks the CPU until
/// the GPU finishes our draw) plus single-threaded call order - our draw always completes, on the
/// CPU timeline, before MonoGame's own Draw() submits anything to the same queue. That is sufficient
/// for this proof of concept but is a real GPU stall; a production backend would want something
/// better (see this project's README).
/// </summary>
public class Game1 : Game
{
    const int TargetWidth = 512;
    const int TargetHeight = 512;

    readonly GraphicsDeviceManager _graphics;
    SpriteBatch _spriteBatch = null!;

    D3D12SkiaSurfaceFactory _skiaFactory = null!;
    D3D12TextureState _textureState = null!;
    IntPtr _resource;
    RenderTarget2D _skiaTarget = null!;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // --- MonoGame/MonoGame#9536: MonoGame's own ID3D12Device/ID3D12CommandQueue ---
        var handles = GraphicsDevice.GetNativeHandles();
        if (handles.Backend != GraphicsBackend.DirectX12)
            throw new InvalidOperationException(
                $"This sample requires the WindowsDX12 platform; GraphicsDevice reported {handles.Backend}.");

        // Resource lives on MonoGame's own device, so Skia and MonoGame share one device/queue -
        // no separate device, no cross-device shared-handle/fence dance.
        _resource = D3D12Native.CreateRenderTargetResource(handles.LogicalDevice, TargetWidth, TargetHeight);

        _skiaFactory = new D3D12SkiaSurfaceFactory();
        _skiaFactory.InitializeFromNative(handles.PhysicalDevice, handles.LogicalDevice, handles.Queue);

        _textureState = _skiaFactory.CreateTextureState(
            _resource, D3D12Native.DXGI_FORMAT_R8G8B8A8_UNORM, D3D12Native.D3D12_RESOURCE_STATE_RENDER_TARGET);

        // --- MonoGame/MonoGame#9535: hand the SAME resource back to MonoGame as a RenderTarget2D ---
        _skiaTarget = RenderTarget2D.FromNativeHandle(GraphicsDevice, _resource, TargetWidth, TargetHeight);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Redraw the shared Scene every frame - this is the steady-state case that actually
        // exercises queue sharing with MonoGame's own per-frame Clear/Draw/Present, not just a
        // one-shot wrap.
        _skiaFactory.BeginDraw();
        var (surface, _) = _skiaFactory.CreateSurface(_textureState, TargetWidth, TargetHeight, SKColorType.Rgba8888);
        Scene.Draw(surface.Canvas, TargetWidth, TargetHeight);
        surface.Canvas.Flush();
        surface.Dispose();
        _skiaFactory.EndDraw();

        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin();
        _spriteBatch.Draw(_skiaTarget, Vector2.Zero, Color.White);
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _skiaTarget?.Dispose();
        _skiaFactory?.Dispose();
        if (_resource != IntPtr.Zero)
            D3D12Native.Release(_resource);

        base.UnloadContent();
    }
}
