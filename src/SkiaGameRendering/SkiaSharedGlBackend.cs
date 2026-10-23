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
    /// MonoGame DesktopGL, FNA, KNI DesktopGL and KNI Android. A subclass supplies how to create and switch
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

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var result = GlSkiaSurfaceFactory.CreateSurface(
                _grContext, _gl, ((GlTextureState)textureHandle).TextureId, width, height, colorType, out var framebufferState);
            renderState = framebufferState;
            return result;
        }

        internal override void BindForDrawing(object renderState) =>
            GlSkiaSurfaceFactory.BindForDrawing(_gl, (GlFramebufferState)renderState);

        internal override void UnbindAfterDrawing() => GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);

        internal override void DisposeRenderState(object renderState) =>
            GlSkiaSurfaceFactory.DisposeRenderState(_gl, (GlFramebufferState)renderState);

        // Skia rewrites the shared texture on its own GL context while the engine's context keeps
        // sampling it, and a context only sees another context's writes to a shared texture when the
        // texture is bound again. An engine that caches its bindings (KNI skips glBindTexture for a
        // texture it thinks is already bound) never rebinds a texture reused frame after frame, and a
        // full-screen fill came out as blocky patches around whatever else was drawn. Rebinding here
        // costs about 13 microseconds a frame, so every GL backend does it instead of only the engines
        // known to need it.
        internal override void RefreshEngineView(object textureHandle) =>
            GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, ((GlTextureState)textureHandle).TextureId);

        // --- Drawing straight into the engine's current target (SkiaRenderer.DrawToCurrentTarget) ---
        //
        // Skia's context is made current on the engine's own window, so its default framebuffer is the
        // window's back buffer. A bound RenderTarget2D lives in an FBO on the engine's context, and FBOs are
        // not shared, but its color attachment is: a texture, or for a multisampled target a renderbuffer the
        // engine resolves into the texture when the target is unbound. Skia's FBO attaches that same object.

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
        private protected virtual bool CanDrawToCurrentTarget => false;

        /// <summary>
        /// The one <see cref="RenderTarget2D"/> the engine has bound, or null for the back buffer. Throws
        /// <see cref="NotSupportedException"/> for several bound targets or a non-2D one.
        /// </summary>
        private protected virtual RenderTarget2D? GetBoundRenderTarget() => throw new NotSupportedException();

        internal override bool SupportsDrawToCurrentTarget => CanDrawToCurrentTarget;

        internal override void DrawToCurrentTarget(Action<SKCanvas> draw)
        {
            if (!CanDrawToCurrentTarget)
            {
                base.DrawToCurrentTarget(draw);
                return;
            }

            var renderTarget = GetBoundRenderTarget();
            if (renderTarget != null && renderTarget.Format != SurfaceFormat.Color)
                throw new NotSupportedException(
                    $"DrawToCurrentTarget supports SurfaceFormat.Color render targets, not {renderTarget.Format}.");

            // Still on the engine's context: what it is drawing into, as GL sees it.
            var glTarget = GlSkiaSurfaceFactory.QueryDrawTarget(_gl);
            if ((renderTarget == null) != glTarget.IsDefaultFramebuffer)
                throw new InvalidOperationException(
                    "The engine's bound GL framebuffer does not match its current render target.");

            // Submit the engine's draws so far, so Skia's land on top of them.
            GlSkiaSurfaceFactory.Flush(_gl);

            BeginDraw();
            try
            {
                PruneDisposedRenderTargets();
                var target = renderTarget == null ? GetBackBufferTarget() : GetRenderTargetTarget(renderTarget, glTarget);

                GlSkiaSurfaceFactory.BindForDrawing(_gl, target.Framebuffer);
                var canvas = target.Surface.Canvas;
                var saveCount = canvas.Save();
                try
                {
                    draw(canvas);
                }
                finally
                {
                    canvas.RestoreToCount(saveCount);
                    target.Surface.Flush();
                }
            }
            finally
            {
                // Also a glFlush, so the engine's later draws land on top of Skia's.
                UnbindAfterDrawing();
                EndDraw();
            }

            // A multisampled target's renderbuffer is only read by the engine's resolve blit, never sampled.
            if (glTarget.IsTexture)
                GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, glTarget.AttachmentName);
        }

        private DirectTarget GetBackBufferTarget()
        {
            var parameters = GraphicsDevice.PresentationParameters;
            var width = parameters.BackBufferWidth;
            var height = parameters.BackBufferHeight;

            if (_backBufferTarget is { } cached && cached.Width == width && cached.Height == height)
                return cached;

            if (_backBufferTarget != null)
                DisposeDirectTarget(_backBufferTarget);
            _backBufferTarget = null;

            var (surface, backendRenderTarget) = GlSkiaSurfaceFactory.CreateDefaultFramebufferSurface(
                _grContext, _gl, width, height, SKColorType.Rgba8888);
            _backBufferTarget = new DirectTarget
            {
                Surface = surface,
                RenderTarget = backendRenderTarget,
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

        private void DisposeDirectTarget(DirectTarget target)
        {
            target.Surface.Dispose();
            target.RenderTarget.Dispose();
            GlSkiaSurfaceFactory.DisposeRenderState(_gl, target.Framebuffer);
        }

        /// <summary>
        /// Frees the surfaces and FBOs <see cref="DrawToCurrentTarget"/> cached, on the Skia context. Call
        /// from <see cref="SkiaBackend.Dispose"/> before disposing the <see cref="GRContext"/>.
        /// </summary>
        private protected void DisposeDirectTargets()
        {
            if (_backBufferTarget == null && _renderTargetTargets.Count == 0)
                return;

            MakeSkiaContextCurrent();
            try
            {
                if (_backBufferTarget != null)
                    DisposeDirectTarget(_backBufferTarget);
                _backBufferTarget = null;

                foreach (var target in _renderTargetTargets.Values)
                    DisposeDirectTarget(target);
                _renderTargetTargets.Clear();
            }
            finally
            {
                MakeEngineContextCurrent();
            }
        }
    }
}
