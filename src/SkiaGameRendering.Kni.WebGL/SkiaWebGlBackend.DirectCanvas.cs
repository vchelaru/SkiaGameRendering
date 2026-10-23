using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace SkiaGameRendering.Kni.WebGL;

// Drawing straight into the engine's current target (SkiaDirectCanvas).
//
// The blit path above renders in the host's hidden canvas and uploads it into a Texture2D each frame. Here
// there is no second canvas: KNI's own WebGL2 context is registered with SkiaSharp's Emscripten GL runtime
// (see beginDirectDraw in skia-game-webgl.js), a GRContext is created on it, and Skia's FBO is whatever KNI
// has bound, the canvas's own back buffer or a RenderTarget2D's framebuffer. Both libraries drive one GL
// context, so a pass saves and restores the bindings KNI relies on and resets KNI's state cache afterward
// (KniStateInvalidation).
public sealed partial class SkiaWebGlBackend
{
    private const int Status = 0, Handle = 1, Epoch = 2, FramebufferId = 3, Samples = 4, StencilBits = 5,
        DrawingBufferWidth = 6, DrawingBufferHeight = 7, PreviousHandle = 8;

    private int _kniContextUid = -1;
    private GRGlInterface? _directInterface;
    private GRContext? _directContext;
    private int _directEpoch;
    private DirectTarget? _backBufferTarget;
    private readonly Dictionary<RenderTarget2D, DirectTarget> _renderTargetTargets = new();
    private readonly List<RenderTarget2D> _disposedRenderTargets = new();

    internal override bool SupportsDirectCanvas => true;

    private sealed class DirectTarget
    {
        internal SKSurface Surface = null!;
        internal GRBackendRenderTarget RenderTarget = null!;
        internal int FramebufferId;
        internal int Width;
        internal int Height;
        internal int Samples;
        internal int StencilBits;
    }

    private sealed class DirectPass
    {
        internal DirectTarget Target = null!;
        internal int SaveCount;
        internal int PreviousHandle;
    }

    internal override SKCanvas BeginDirectDraw(out object pass)
    {
        EnsureThread();
        var renderTarget = GetBoundRenderTarget();

        if (_kniContextUid < 0)
            _kniContextUid = KniWebGlInternals.GetGlContextUid(GraphicsDevice);

        int[] info;
        try
        {
            info = DirectDrawInterop.Begin(_kniContextUid);
        }
        catch (JSException exception)
        {
            throw new InvalidOperationException(
                $"SkiaDirectCanvas could not adopt KNI's WebGL context: {exception.Message}", exception);
        }

        if (info[Status] != 0)
        {
            Diagnostics.DroppedFrameCount++;
            AbandonDirectContext();
            throw new InvalidOperationException(
                "KNI's WebGL context is lost; SkiaDirectCanvas cannot draw until it is restored.");
        }

        // A loss and restore since the last pass left Skia's context and every surface on it dead.
        if (info[Epoch] != _directEpoch)
        {
            AbandonDirectContext();
            _directEpoch = info[Epoch];
            Diagnostics.DirectContextLossCount++;
        }

        try
        {
            // Still KNI's view of the world: the bound framebuffer must be the one its current target implies.
            if (renderTarget != null ? info[FramebufferId] == 0 : info[FramebufferId] != 0)
                throw new InvalidOperationException("The engine's bound GL framebuffer does not match its current render target.");

            EnsureDirectContext();
            PruneDisposedRenderTargets();
            var target = renderTarget == null ? GetBackBufferTarget(info) : GetRenderTargetTarget(renderTarget, info);

            _directContext!.ResetContext();
            var canvas = target.Surface.Canvas;
            pass = new DirectPass { Target = target, SaveCount = canvas.Save(), PreviousHandle = info[PreviousHandle] };
            return canvas;
        }
        catch
        {
            EndGlPass(info[PreviousHandle]);
            throw;
        }
    }

    internal override void EndDirectDraw(object pass)
    {
        var directPass = (DirectPass)pass;
        try
        {
            directPass.Target.Surface.Canvas.RestoreToCount(directPass.SaveCount);
            directPass.Target.Surface.Flush();
            _directContext!.Flush();
            Diagnostics.DirectDrawCount++;
        }
        finally
        {
            EndGlPass(directPass.PreviousHandle);
        }
    }

    /// <summary>Puts back what KNI relies on, then forgets everything KNI thinks it has applied.</summary>
    private void EndGlPass(int previousHandle)
    {
        try
        {
            DirectDrawInterop.End(_kniContextUid, previousHandle);
        }
        finally
        {
            KniStateInvalidation.Invalidate(GraphicsDevice);
        }
    }

    private void EnsureDirectContext()
    {
        if (_directContext != null)
            return;

        _directInterface = GRGlInterface.Create()
            ?? throw new InvalidOperationException("GRGlInterface.Create failed for KNI's WebGL context.");
        _directContext = GRContext.CreateGl(_directInterface)
            ?? throw new InvalidOperationException("GRContext.CreateGl failed for KNI's WebGL context.");
        _directContext.SetResourceCacheLimit(_options.ResourceCacheBytes);
    }

    /// <summary>The one RenderTarget2D the engine has bound, or null for the canvas's own back buffer.</summary>
    private RenderTarget2D? GetBoundRenderTarget()
    {
        var bindings = GraphicsDevice.GetRenderTargets();
        if (bindings.Length == 0)
            return null;
        if (bindings.Length > 1)
            throw new NotSupportedException("SkiaDirectCanvas draws into one render target at a time, but the engine has several bound.");
        if (bindings[0].RenderTarget is not RenderTarget2D renderTarget)
            throw new NotSupportedException("SkiaDirectCanvas supports RenderTarget2D, not cube map targets.");
        if (renderTarget.Format != SurfaceFormat.Color)
            throw new NotSupportedException($"SkiaDirectCanvas supports SurfaceFormat.Color render targets, not {renderTarget.Format}.");
        return renderTarget;
    }

    private DirectTarget GetBackBufferTarget(int[] info)
    {
        var width = info[DrawingBufferWidth];
        var height = info[DrawingBufferHeight];
        if (_backBufferTarget is { } cached && Matches(cached, info, width, height))
            return cached;

        if (_backBufferTarget != null)
            DisposeDirectTarget(_backBufferTarget);
        _backBufferTarget = null;
        // The canvas's own framebuffer has its origin at the bottom left.
        _backBufferTarget = CreateDirectTarget(info, width, height, GRSurfaceOrigin.BottomLeft);
        return _backBufferTarget;
    }

    private DirectTarget GetRenderTargetTarget(RenderTarget2D renderTarget, int[] info)
    {
        if (_renderTargetTargets.TryGetValue(renderTarget, out var cached))
        {
            if (Matches(cached, info, renderTarget.Width, renderTarget.Height))
                return cached;

            DisposeDirectTarget(cached);
            _renderTargetTargets.Remove(renderTarget);
        }

        // KNI renders into a texture upside down so that its first row is the image's top, so Skia's
        // origin here is the top left, unlike the canvas.
        var target = CreateDirectTarget(info, renderTarget.Width, renderTarget.Height, GRSurfaceOrigin.TopLeft);
        _renderTargetTargets.Add(renderTarget, target);
        return target;
    }

    private bool Matches(DirectTarget target, int[] info, int width, int height) =>
        target.FramebufferId == info[FramebufferId] && target.Width == width && target.Height == height
        && target.Samples == info[Samples] && target.StencilBits == info[StencilBits];

    private DirectTarget CreateDirectTarget(int[] info, int width, int height, GRSurfaceOrigin origin)
    {
        var framebuffer = new GRGlFramebufferInfo((uint)info[FramebufferId], SKColorType.Rgba8888.ToGlSizedFormat());
        var renderTarget = new GRBackendRenderTarget(width, height, info[Samples], info[StencilBits], framebuffer);
        var surface = SKSurface.Create(_directContext!, renderTarget, origin, SKColorType.Rgba8888);
        if (surface == null)
        {
            renderTarget.Dispose();
            throw new InvalidOperationException(
                $"SKSurface.Create failed for KNI's {width}x{height} framebuffer ({info[Samples]} samples, {info[StencilBits]} stencil bits).");
        }

        Diagnostics.SurfaceRecreationCount++;
        return new DirectTarget
        {
            Surface = surface,
            RenderTarget = renderTarget,
            FramebufferId = info[FramebufferId],
            Width = width,
            Height = height,
            Samples = info[Samples],
            StencilBits = info[StencilBits],
        };
    }

    // A disposed RenderTarget2D's framebuffer is gone, and the engine may hand its name to the next one.
    private void PruneDisposedRenderTargets()
    {
        foreach (var renderTarget in _renderTargetTargets.Keys)
        {
            if (renderTarget.IsDisposed)
                _disposedRenderTargets.Add(renderTarget);
        }

        foreach (var renderTarget in _disposedRenderTargets)
        {
            DisposeDirectTarget(_renderTargetTargets[renderTarget]);
            _renderTargetTargets.Remove(renderTarget);
        }
        _disposedRenderTargets.Clear();
    }

    private static void DisposeDirectTarget(DirectTarget target)
    {
        target.Surface.Dispose();
        target.RenderTarget.Dispose();
        if (target.FramebufferId != 0)
            DirectDrawInterop.ReleaseFramebuffer(target.FramebufferId);
    }

    /// <summary>
    /// Drops Skia's context on KNI's WebGL context without touching GL, for a context that was lost: every
    /// GL object it owned went with the context.
    /// </summary>
    private void AbandonDirectContext()
    {
        _directContext?.AbandonContext();
        FreeDirectResources();
    }

    private void FreeDirectResources()
    {
        if (_backBufferTarget != null)
            DisposeDirectTarget(_backBufferTarget);
        _backBufferTarget = null;
        foreach (var target in _renderTargetTargets.Values)
            DisposeDirectTarget(target);
        _renderTargetTargets.Clear();

        _directContext?.Dispose();
        _directContext = null;
        _directInterface?.Dispose();
        _directInterface = null;
    }

    /// <summary>Frees the direct canvas's Skia context with KNI's context current, then unregisters KNI's context from Skia.</summary>
    private void DisposeDirect()
    {
        if (_kniContextUid < 0)
            return;

        var previous = -1;
        try
        {
            previous = DirectDrawInterop.MakeContextCurrent(_kniContextUid);
            if (_directContext != null && IsKniContextUsable())
                FreeDirectResources();
            else
                AbandonDirectContext();
        }
        catch (JSException)
        {
            // KNI's context is already gone; nothing on it can be freed.
            AbandonDirectContext();
        }
        finally
        {
            try
            {
                DirectDrawInterop.RestoreContext(previous);
                DirectDrawInterop.DisposeContext(_kniContextUid);
            }
            catch (JSException)
            {
            }
            _kniContextUid = -1;
        }
    }

    private bool IsKniContextUsable() => GraphicsDevice is { IsDisposed: false } && !DirectDrawInterop.IsContextLost(_kniContextUid);
}

[SupportedOSPlatform("browser")]
internal static partial class DirectDrawInterop
{
    [JSImport("globalThis.skiaGameWebGl.beginDirectDraw")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial int[] Begin(int contextUid);

    [JSImport("globalThis.skiaGameWebGl.endDirectDraw")]
    internal static partial void End(int contextUid, int previousHandle);

    [JSImport("globalThis.skiaGameWebGl.makeKniContextCurrent")]
    internal static partial int MakeContextCurrent(int contextUid);

    [JSImport("globalThis.skiaGameWebGl.restoreContext")]
    internal static partial void RestoreContext(int previousHandle);

    [JSImport("globalThis.skiaGameWebGl.releaseKniFramebuffer")]
    internal static partial void ReleaseFramebuffer(int framebufferId);

    [JSImport("globalThis.skiaGameWebGl.disposeKniContext")]
    internal static partial void DisposeContext(int contextUid);

    [JSImport("globalThis.skiaGameWebGl.isKniContextLost")]
    internal static partial bool IsContextLost(int contextUid);
}
