using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace SkiaGameRendering
{
    public abstract class SkiaBackend : IDisposable
    {
        public GraphicsDevice GraphicsDevice { get; protected set; } = null!;
        public abstract GRContext GRContext { get; }

        /// <summary>
        /// Resolves once this backend's underlying graphics context is ready to be initialized.
        /// Desktop backends complete immediately - their context already exists by construction.
        /// <see cref="Initialize"/> must not be called until this completes.
        /// </summary>
        public virtual Task Ready => Task.CompletedTask;

        public abstract void Initialize(GraphicsDevice graphicsDevice);

        internal abstract void BeginDraw();
        internal abstract void EndDraw();

        /// <summary>
        /// Eagerly allocates the texture and GPU render-target resources for a fixed-size target.
        /// </summary>
        internal virtual SkiaTarget CreateTarget(int width, int height, SKColorType colorType)
        {
            var texture = CreateTexture(width, height, ToSurfaceFormat(colorType));

            try
            {
                var target = new NativeSkiaTarget(this, texture, CaptureTextureHandle(texture));
                BeginDraw();
                try
                {
                    target.EnsureSurface(colorType);
                }
                finally
                {
                    EndDraw();
                }
                return target;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Begins a render pass into <paramref name="target"/>: switches to the Skia GPU context,
        /// binds the target, and returns the canvas to draw on.
        /// </summary>
        internal virtual SKCanvas BeginRender(SkiaTarget target, bool clear)
        {
            var nativeTarget = target as NativeSkiaTarget
                ?? throw new ArgumentException("The target was not created by this backend.", nameof(target));

            BeginDraw();
            BindForDrawing(nativeTarget.RenderState!);

            if (clear)
                nativeTarget.Surface!.Canvas.Clear();

            return nativeTarget.Surface!.Canvas;
        }

        /// <summary>
        /// Ends the render pass started by <see cref="BeginRender"/>: flushes Skia's queued GPU
        /// work, unbinds the target, and switches back to the host engine's GPU context.
        /// </summary>
        internal virtual void EndRender(SkiaTarget target)
        {
            var nativeTarget = target as NativeSkiaTarget
                ?? throw new ArgumentException("The target was not created by this backend.", nameof(target));

            try
            {
                nativeTarget.Surface!.Flush();
            }
            finally
            {
                UnbindAfterDrawing();
                EndDraw();
            }
        }

        internal virtual Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new Texture2D(GraphicsDevice, width, height, false, format);
        }

        // Only called by the default CreateTarget/BeginRender/EndRender above, for backends that
        // draw directly into a captured native GPU texture handle (SkiaGlBackend, SkiaAngleBackend).
        // A backend that overrides all three of those (e.g. SkiaWebGlBackend, which composites via
        // canvas upload instead) never calls these and doesn't need to implement them - the shared
        // NotSupportedException default here means it doesn't have to remember five boilerplate
        // overrides just to satisfy the compiler.
        internal virtual object CaptureTextureHandle(Texture2D texture) => throw NativeTextureHandlePathNotSupported();
        internal virtual (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState) =>
            throw NativeTextureHandlePathNotSupported();
        internal virtual void BindForDrawing(object renderState) => throw NativeTextureHandlePathNotSupported();
        internal virtual void UnbindAfterDrawing() => throw NativeTextureHandlePathNotSupported();
        internal virtual void DisposeRenderState(object renderState) => throw NativeTextureHandlePathNotSupported();

        private NotSupportedException NativeTextureHandlePathNotSupported([CallerMemberName] string? memberName = null) =>
            new($"{GetType().Name} does not support the native-texture-handle interop path ({memberName}). " +
                "It overrides CreateTarget/BeginRender/EndRender directly instead.");

        public abstract void Dispose();

        internal static SurfaceFormat ToSurfaceFormat(SKColorType color)
        {
            return color switch
            {
                SKColorType.Rgba1010102 => SurfaceFormat.Rgba1010102,
                SKColorType.Rgba16161616 => SurfaceFormat.Rgba64,
                SKColorType.Alpha8 => SurfaceFormat.Alpha8,
#if !FNA
                SKColorType.Bgra8888 => SurfaceFormat.Bgra32,
#endif
                SKColorType.Rg1616 => SurfaceFormat.Rg32,
                _ => SurfaceFormat.Color,
            };
        }

        private sealed class NativeSkiaTarget : SkiaTarget
        {
            private readonly SkiaBackend _backend;
            private Texture2D? _texture;
            private object? _textureHandle;
            private SKSurface? _surface;
            private GRBackendRenderTarget? _backendRenderTarget;
            private object? _renderState;

            internal NativeSkiaTarget(SkiaBackend backend, Texture2D texture, object textureHandle)
            {
                _backend = backend;
                _texture = texture;
                _textureHandle = textureHandle;
            }

            public override Texture2D Texture => _texture
                ?? throw new ObjectDisposedException(nameof(NativeSkiaTarget));
            internal SKSurface? Surface => _surface;
            internal object? RenderState => _renderState;

            internal void EnsureSurface(SKColorType colorType)
            {
                if (_surface != null && _backendRenderTarget != null)
                    return;

                var result = _backend.CreateSurface(
                    _textureHandle!, Texture, Texture.Width, Texture.Height, colorType, out var renderState);
                _surface = result.surface;
                _backendRenderTarget = result.renderTarget;
                _renderState = renderState;
            }

            internal override void DisposeSkiaResources()
            {
                _surface?.Dispose();
                _surface = null;
                _backendRenderTarget?.Dispose();
                _backendRenderTarget = null;

                if (_renderState != null)
                {
                    _backend.DisposeRenderState(_renderState);
                    _renderState = null;
                }

                _textureHandle = null;
            }

            internal override void DisposeGraphicsResources()
            {
                _texture?.Dispose();
                _texture = null;
            }
        }
    }
}
