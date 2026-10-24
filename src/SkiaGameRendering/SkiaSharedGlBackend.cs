using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering
{
    internal sealed class GlTextureState
    {
        internal int TextureId;
    }

    /// <summary>
    /// The part every GL backend that draws on a second context sharing the engine's objects has in common:
    /// MonoGame DesktopGL, Android and iOS, FNA, KNI DesktopGL and KNI Android. A subclass supplies how to create and switch
    /// between the two contexts and where the engine keeps a texture's GL name. Everything else, including
    /// making the engine's context look at a rewritten texture again, lives here once, so a GL fix reaches
    /// all of them. Linked as source into each of those projects, like <see cref="SkiaBackend"/>.
    /// </summary>
    public abstract class SkiaSharedGlBackend : SkiaBackend
    {
        private protected GRContext _grContext = null!;
        private protected GlFunctions _gl = null!;

        public override GRContext GRContext => _grContext;

        private protected abstract void MakeSkiaContextCurrent();
        private protected abstract void MakeEngineContextCurrent();

        /// <summary>The GL name of <paramref name="texture"/>, called inside a <see cref="SkiaBackend.BeginDraw"/>/<see cref="SkiaBackend.EndDraw"/> pair.</summary>
        private protected abstract int CaptureGlTextureId(Texture2D texture);

        internal override void BeginDraw()
        {
            MakeSkiaContextCurrent();
            _grContext.ResetContext();
        }

        internal override void EndDraw() => MakeEngineContextCurrent();

        internal override object CaptureTextureHandle(Texture2D texture) =>
            new GlTextureState { TextureId = CaptureGlTextureId(texture) };

        /// <summary>
        /// Samples per pixel for render targets, from the SKIAGAMERENDERING_RENDERTARGET_MSAA environment variable
        /// (for example 4). Off by default: it cuts shape recording CPU, but costs memory, and shadow scenes got
        /// a few percent slower. Falls back to a plain surface when the GPU has no such sample count.
        /// </summary>
        private static readonly int RenderTargetSampleCount =
            int.TryParse(Environment.GetEnvironmentVariable("SKIAGAMERENDERING_RENDERTARGET_MSAA"), out var n) ? n : 0;

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            if (RenderTargetSampleCount > 1 && GlSkiaSurfaceFactory.TryCreateMultisampledSurface(
                    _grContext, ((GlTextureState)textureHandle).TextureId, width, height, colorType, RenderTargetSampleCount) is { } multisampled)
            {
                renderState = multisampled.state;
                return (multisampled.surface, multisampled.renderTarget);
            }

            var result = GlSkiaSurfaceFactory.CreateSurface(
                _grContext, _gl, ((GlTextureState)textureHandle).TextureId, width, height, colorType, out var framebufferState);
            renderState = framebufferState;
            return result;
        }

        // Skia leaves a multisampled surface over a texture unresolved at flush; taking a snapshot resolves it
        // into the texture on the GPU (verified by pixels: without it the texture stays empty).
        internal override void ResolveMultisampled(SKSurface surface)
        {
            if (RenderTargetSampleCount > 1)
                surface.Snapshot().Dispose();
        }

        internal override void BindForDrawing(object renderState) =>
            GlSkiaSurfaceFactory.BindForDrawing(_gl, (GlFramebufferState)renderState);

        internal override void UnbindAfterDrawing() => GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);

        // --- Teardown ---
        //
        // Every Skia-side GL object is freed on Skia's context, through RunOnSkiaContext (see SkiaGlTeardown
        // for why). There is deliberately no DisposeRenderState override: nothing frees a framebuffer
        // outside it.

        /// <summary>
        /// Runs <paramref name="teardown"/> on Skia's context, then hands the engine its context back. Gets
        /// false, and must make no GL calls, once the engine's device is disposed or Skia's context cannot
        /// be made current.
        /// </summary>
        private protected void RunOnSkiaContext(Action<bool> teardown) =>
            SkiaGlTeardown.Run(GraphicsDevice is { IsDisposed: false }, _grContext,
                MakeSkiaContextCurrent, MakeEngineContextCurrent, teardown);

        /// <summary>Destroys Skia's GL context. Called once its <see cref="GRContext"/> is gone; must be safe to call twice.</summary>
        private protected abstract void DestroySkiaContext();

        internal sealed override void DisposeTargetSkiaResources(SKSurface? surface, GRBackendRenderTarget? renderTarget, object? renderState) =>
            RunOnSkiaContext(glUsable =>
            {
                surface?.Dispose();
                renderTarget?.Dispose();
                if (glUsable && renderState != null)
                    GlSkiaSurfaceFactory.DisposeRenderState(_gl, (GlFramebufferState)renderState);
            });

        public sealed override void Dispose()
        {
            if (_grContext != null)
            {
                var grContext = _grContext;
                RunOnSkiaContext(glUsable =>
                {
                    DisposeDirectTargets(glUsable);
                    grContext.Dispose();
                });
                _grContext = null!;
            }
            DestroySkiaContext();
        }

        // Skia rewrites the shared texture on its own GL context while the engine's context keeps
        // sampling it, and a context only sees another context's writes to a shared texture when the
        // texture is bound again. An engine that caches its bindings (KNI skips glBindTexture for a
        // texture it thinks is already bound) never rebinds a texture reused frame after frame, and a
        // full-screen fill came out as blocky patches around whatever else was drawn. Rebinding here
        // costs about 13 microseconds a frame, so every GL backend does it instead of only the engines
        // known to need it.
        internal override void RefreshEngineView(object textureHandle) =>
            GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, ((GlTextureState)textureHandle).TextureId);

        // --- Drawing straight into the engine's current target (SkiaDirectCanvas) ---
        //
        // Skia's context is made current on the engine's own window, so its default framebuffer is the
        // window's back buffer. A bound RenderTarget2D lives in an FBO on the engine's context, and FBOs are
        // not shared, but its color attachment is: a texture, or for a multisampled target a renderbuffer the
        // engine resolves into the texture when the target is unbound. Skia's FBO attaches that same object.
        // An engine whose back buffer can be an FBO of its own (FNA3D's "faux backbuffer") gets the same
        // treatment, with the rows bottom-up like a window's.

        private sealed class DirectTarget
        {
            internal SKSurface Surface = null!;
            internal GRBackendRenderTarget RenderTarget = null!;
            internal GlFramebufferState Framebuffer = new();
            internal GlDrawTarget Gl;
            internal int Width;
            internal int Height;
        }

        private DirectTarget? _backBufferTarget;
        private readonly Dictionary<RenderTarget2D, DirectTarget> _renderTargetTargets = new();
        private readonly List<RenderTarget2D> _disposedRenderTargets = new();

        /// <summary>Whether this engine's subclass implements <see cref="GetBoundRenderTarget"/>.</summary>
        private protected virtual bool CanDrawDirect => false;

        /// <summary>
        /// The one <see cref="RenderTarget2D"/> the engine has bound, or null for the back buffer. Throws
        /// <see cref="NotSupportedException"/> for several bound targets or a non-2D one.
        /// </summary>
        private protected virtual RenderTarget2D? GetBoundRenderTarget() => throw new NotSupportedException();

        /// <summary>
        /// Whether the engine's back buffer can be an FBO it owns rather than the default framebuffer. Only
        /// FNA3D does this, when the back buffer and window sizes differ or the back buffer is multisampled.
        /// </summary>
        private protected virtual bool BackBufferCanBeFramebufferObject => false;

        /// <summary>
        /// Drops the cached back buffer surface at the next <see cref="SkiaDirectCanvas"/> pass. For an engine
        /// that can rebuild its back buffer FBO in place: a deleted renderbuffer's name can come back for the
        /// new one, which the cache cannot tell apart from the old.
        /// </summary>
        private protected void InvalidateBackBufferTarget() => _backBufferStale = true;

        private bool _backBufferStale;

        /// <summary>
        /// Whether the engine can delete its back buffer's color renderbuffer and make a new one under the
        /// same GL name, with nothing to tell this backend. MonoGame iOS does that on every view layout. An FBO
        /// of Skia's that still had the old renderbuffer attached would keep it alive, which stops the new one
        /// from taking over the view's layer, so the renderbuffer is only attached for the length of a pass.
        /// </summary>
        private protected virtual bool BackBufferRenderbufferCanBeReplaced => false;

        /// <summary>
        /// Makes Skia's context current with the engine's window as its default framebuffer. The desktop
        /// backends always make it current on the window, so this is <see cref="MakeSkiaContextCurrent"/>
        /// unless that can leave the context without one.
        /// </summary>
        private protected virtual void MakeSkiaContextCurrentOnEngineWindow() => MakeSkiaContextCurrent();

        internal override bool SupportsDirectCanvas => CanDrawDirect;

        private sealed class DirectPass
        {
            internal DirectTarget Target = null!;
            internal GlDrawTarget Gl;
            internal int SaveCount;
        }

        internal override SKCanvas BeginDirectDraw(out object pass)
        {
            if (!CanDrawDirect)
                throw DirectCanvasNotSupported();

            var renderTarget = GetBoundRenderTarget();
            if (renderTarget != null && renderTarget.Format != SurfaceFormat.Color)
                throw new NotSupportedException(
                    $"SkiaDirectCanvas supports SurfaceFormat.Color render targets, not {renderTarget.Format}.");

            // Still on the engine's context: what it is drawing into, as GL sees it.
            var glTarget = GlSkiaSurfaceFactory.QueryDrawTarget(_gl);
            if (renderTarget != null ? glTarget.IsDefaultFramebuffer
                    : !glTarget.IsDefaultFramebuffer && !BackBufferCanBeFramebufferObject)
                throw new InvalidOperationException(
                    "The engine's bound GL framebuffer does not match its current render target.");

            // Submit the engine's draws so far, so Skia's land on top of them.
            GlSkiaSurfaceFactory.Flush(_gl);

            MakeSkiaContextCurrentOnEngineWindow();
            _grContext.ResetContext();
            try
            {
                PruneDisposedRenderTargets();
                var target = renderTarget == null ? GetBackBufferTarget(glTarget) : GetRenderTargetTarget(renderTarget, glTarget);

                GlSkiaSurfaceFactory.BindForDrawing(_gl, target.Framebuffer);
                var canvas = target.Surface.Canvas;
                pass = new DirectPass { Target = target, Gl = glTarget, SaveCount = canvas.Save() };
                return canvas;
            }
            catch
            {
                UnbindAfterDrawing();
                EndDraw();
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
            }
            finally
            {
                if (BackBufferRenderbufferCanBeReplaced && directPass.Target == _backBufferTarget && !directPass.Gl.IsDefaultFramebuffer)
                    GlSkiaSurfaceFactory.DetachColor(_gl, directPass.Target.Framebuffer);
                // Also a glFlush, so the engine's later draws land on top of Skia's.
                UnbindAfterDrawing();
                EndDraw();
            }

            // A multisampled target's renderbuffer is only read by the engine's resolve blit, never sampled.
            if (directPass.Gl.IsTexture)
                GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, directPass.Gl.AttachmentName);
        }

        private DirectTarget GetBackBufferTarget(GlDrawTarget glTarget)
        {
            var parameters = GraphicsDevice.PresentationParameters;
            var width = parameters.BackBufferWidth;
            var height = parameters.BackBufferHeight;

            if (!_backBufferStale && _backBufferTarget is { } cached
                && cached.Gl == glTarget && cached.Width == width && cached.Height == height)
            {
                if (BackBufferRenderbufferCanBeReplaced && !glTarget.IsDefaultFramebuffer)
                    GlSkiaSurfaceFactory.AttachColor(_gl, cached.Framebuffer, glTarget);
                return cached;
            }

            if (_backBufferTarget != null)
                DisposeDirectTarget(_backBufferTarget);
            _backBufferTarget = null;
            _backBufferStale = false;

            var framebuffer = new GlFramebufferState();
            var (surface, backendRenderTarget) = glTarget.IsDefaultFramebuffer
                ? GlSkiaSurfaceFactory.CreateDefaultFramebufferSurface(_grContext, _gl, width, height, SKColorType.Rgba8888)
                : GlSkiaSurfaceFactory.CreateAttachmentSurface(_grContext, _gl, glTarget, width, height,
                    SKColorType.Rgba8888, out framebuffer, GRSurfaceOrigin.BottomLeft);
            _backBufferTarget = new DirectTarget
            {
                Surface = surface,
                RenderTarget = backendRenderTarget,
                Framebuffer = framebuffer,
                Gl = glTarget,
                Width = width,
                Height = height,
            };
            return _backBufferTarget;
        }

        private DirectTarget GetRenderTargetTarget(RenderTarget2D renderTarget, GlDrawTarget glTarget)
        {
            if (_renderTargetTargets.TryGetValue(renderTarget, out var cached))
            {
                if (cached.Gl == glTarget && cached.Width == renderTarget.Width && cached.Height == renderTarget.Height)
                    return cached;

                DisposeDirectTarget(cached);
                _renderTargetTargets.Remove(renderTarget);
            }

            var (surface, backendRenderTarget) = GlSkiaSurfaceFactory.CreateAttachmentSurface(
                _grContext, _gl, glTarget, renderTarget.Width, renderTarget.Height, SKColorType.Rgba8888,
                out var framebufferState);
            var target = new DirectTarget
            {
                Surface = surface,
                RenderTarget = backendRenderTarget,
                Framebuffer = framebufferState,
                Gl = glTarget,
                Width = renderTarget.Width,
                Height = renderTarget.Height,
            };
            _renderTargetTargets.Add(renderTarget, target);
            return target;
        }

        // A disposed RenderTarget2D's GL name can be handed to the next one the engine creates, so its
        // FBO goes as soon as the Skia context is next current.
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

        /// <summary>Only called with Skia's context current: inside a direct-draw pass, or from <see cref="DisposeDirectTargets"/>.</summary>
        private void DisposeDirectTarget(DirectTarget target, bool glUsable = true)
        {
            target.Surface.Dispose();
            target.RenderTarget.Dispose();
            if (glUsable)
                GlSkiaSurfaceFactory.DisposeRenderState(_gl, target.Framebuffer);
        }

        /// <summary>Frees the surfaces and FBOs <see cref="SkiaDirectCanvas"/> cached. Runs inside <see cref="Dispose"/>'s <see cref="RunOnSkiaContext"/>.</summary>
        private void DisposeDirectTargets(bool glUsable)
        {
            if (_backBufferTarget != null)
                DisposeDirectTarget(_backBufferTarget, glUsable);
            _backBufferTarget = null;

            foreach (var target in _renderTargetTargets.Values)
                DisposeDirectTarget(target, glUsable);
            _renderTargetTargets.Clear();
        }
    }
}
